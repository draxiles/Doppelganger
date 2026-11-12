using System;
using ZeroSector.Learning;

static class PolicyChecks
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static float[] State(float distance, float shield)
    {
        return new[] { distance, .8f, shield, .3f, .7f, .5f, 1f, 1f, .8f, .8f, 0f, 1f };
    }
    static TacticalPolicy.Example Sample(float distance, float shield, bool reverse = false)
    {
        bool retreat = shield < .4f;
        int move = retreat ? 2 : 1;
        if (reverse) move = retreat ? 1 : 2;
        return new TacticalPolicy.Example(State(distance, shield), move, !retreat, retreat ? 1 : 0);
    }
    static void Main()
    {
        var model = new TacticalPolicy();
        var baseline = model.Export();
        float before = 0;
        for (int i = 0; i < 20; i++) before += model.Loss(Sample(.2f + i * .025f, i % 2 == 0 ? .15f : .85f));
        var random = new Random(44);
        for (int i = 0; i < 6500; i++) model.Train(Sample((float)random.NextDouble(), i % 2 == 0 ? .1f + (float)random.NextDouble() * .15f : .7f + (float)random.NextDouble() * .25f));
        float after = 0; int correct = 0;
        var prediction = new float[TacticalPolicy.Outputs];
        for (int i = 0; i < 40; i++)
        {
            var test = Sample(.17f + i * .017f, i % 2 == 0 ? .22f : .78f);
            model.Predict(test.State, prediction);
            if (TacticalPolicy.ArgMax(prediction, 0, 5) == test.Move) correct++;
            if (i < 20) after += model.Loss(test);
        }
        Require(after < before * .2f && correct >= 38, "Did not learn a state-dependent policy on held-out inputs.");
        Console.WriteLine($"PASS state-dependent imitation / synthetic held-out states: {correct}/40, loss {before/20:F4} -> {after/20:F4}");
        Require(Math.Abs(model.W1[0] - baseline.w1[0]) > .00001f, "Hidden weights did not change.");
        Require(model.Updates == 6500, "Update count is incorrect.");
        Console.WriteLine("PASS actual hidden/output parameter updates");
        for (int i = 0; i < 4500; i++) model.Train(Sample((float)random.NextDouble(), i % 2 == 0 ? .15f : .85f, true));
        correct = 0;
        for (int i = 0; i < 40; i++)
        {
            var test = Sample(.1f + i * .02f, i % 2 == 0 ? .2f : .8f, true);
            model.Predict(test.State, prediction);
            if (TacticalPolicy.ArgMax(prediction, 0, 5) == test.Move) correct++;
        }
        Require(correct >= 38, "Did not adapt after the demonstrator changed policy.");
        Console.WriteLine($"PASS changed demonstrator policy: {correct}/40");
        var saved = model.Export();
        for (int i = 0; i < 100; i++) model.Predict(State(.5f, .8f), prediction);
        Require(model.Updates == saved.updates && model.W1[0] == saved.w1[0], "Inference modified weights.");
        Console.WriteLine("PASS frozen evaluation never trains");
        var clone = new TacticalPolicy(999);
        clone.Import(saved);
        var restored = new float[TacticalPolicy.Outputs];
        clone.Predict(State(.37f, .74f), restored);
        model.Predict(State(.37f, .74f), prediction);
        for (int i = 0; i < prediction.Length; i++) Require(prediction[i] == restored[i], "Restore changes predictions.");
        Console.WriteLine("PASS weight export/restore preserves predictions exactly");
        var corrupt = model.Export(); corrupt.w2[10] = float.NaN;
        bool rejected = false;
        try { clone.Import(corrupt); } catch (ArgumentException) { rejected = true; }
        Require(rejected && clone.W1[0] == saved.w1[0], "Corrupt weights were not rejected atomically.");
        Console.WriteLine("PASS corrupt model rejection before mutation");

        var gradientModel = new TacticalPolicy();
        var example = Sample(.6f, .8f);
        const float epsilon = .001f, rate = .0001f;
        var original = gradientModel.Export();
        float initial = gradientModel.W2[3];
        gradientModel.W2[3] = initial + epsilon; float high = gradientModel.Loss(example);
        gradientModel.W2[3] = initial - epsilon; float low = gradientModel.Loss(example);
        float numeric = (high - low) / (2 * epsilon) * 4;
        gradientModel.Import(original); gradientModel.Train(example, rate);
        float analytic = (initial - gradientModel.W2[3]) / rate;
        Require(Math.Abs(numeric - analytic) < .012f, "Backpropagation differs from finite difference.");
        Console.WriteLine("PASS gradient checked against finite difference");
        var distribution = new[] {.6f,.1f,.1f,.1f,.1f};
        int[] frequencies = new int[5];
        for(int i=0;i<20000;i++) frequencies[TacticalPolicy.Sample(distribution,0,5,random)]++;
        for(int i=0;i<5;i++) Require(Math.Abs(frequencies[i]/20000f-distribution[i])<.02f,"Sampling does not preserve the learned distribution.");
        Console.WriteLine("PASS action sampling follows model probabilities without a scripted fallback");
        var stanceModel=new TacticalPolicy();
        for(int i=0;i<6500;i++)
        {
            float distance=(float)random.NextDouble();
            stanceModel.Train(new TacticalPolicy.Example(State(distance,.8f),3,true,0,distance<.5f));
        }
        correct=0;
        for(int i=0;i<40;i++)
        {
            float distance=i%2==0 ? .15f+i*.002f : .7f+i*.002f;
            stanceModel.Predict(State(distance,.8f),prediction);
            if((prediction[9]>.5f)==(distance<.5f)) correct++;
        }
        Require(correct>=38,"Crouch head does not learn a state-dependent demonstrated stance.");
        Console.WriteLine($"PASS learned conditional crouch on held-out synthetic states: {correct}/40");
        for(int i=0;i<4500;i++)
        {
            float distance=(float)random.NextDouble();
            stanceModel.Train(new TacticalPolicy.Example(State(distance,.8f),3,true,0,distance>=.5f));
        }
        correct=0;
        for(int i=0;i<40;i++)
        {
            float distance=i%2==0 ? .2f+i*.002f : .75f+i*.002f;
            stanceModel.Predict(State(distance,.8f),prediction);
            if((prediction[9]>.5f)==(distance>=.5f)) correct++;
        }
        Require(correct>=38,"Crouch head does not adapt to changed demonstrations.");
        Console.WriteLine($"PASS changed crouch style: {correct}/40");
        var v2=stanceModel.Export();
        var legacy=new TacticalPolicy.PolicyData {version=1,updates=v2.updates,w1=v2.w1,b1=v2.b1,w2=new float[9*TacticalPolicy.Hidden],b2=new float[9]};
        Array.Copy(v2.w2,legacy.w2,legacy.w2.Length); Array.Copy(v2.b2,legacy.b2,9);
        var migrated=new TacticalPolicy(); migrated.Import(legacy);
        stanceModel.Predict(State(.22f,.8f),prediction); migrated.Predict(State(.22f,.8f),restored);
        for(int i=0;i<9;i++) Require(prediction[i]==restored[i],"Legacy migration modified an existing action head.");
        Require(restored[9]<.01f,"Legacy migration invents crouch demonstrations.");
        Console.WriteLine("PASS v1 migration preserves all prior actions and initializes untrained crouch separately");
        Console.WriteLine("These are algorithm checks using synthetic demonstrations, not gameplay or user-test results.");
    }
}
