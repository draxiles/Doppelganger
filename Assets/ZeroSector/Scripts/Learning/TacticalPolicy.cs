using System;

namespace ZeroSector.Learning
{
    // Small CPU behavioral-cloning network. No Unity dependency: the same code is tested offline.
    public sealed class TacticalPolicy
    {
        public const int Inputs = 12, Hidden = 24, Outputs = 10;
        public readonly float[] W1 = new float[Inputs * Hidden];
        public readonly float[] B1 = new float[Hidden];
        public readonly float[] W2 = new float[Hidden * Outputs];
        public readonly float[] B2 = new float[Outputs];
        readonly float[] hidden = new float[Hidden], output = new float[Outputs];
        readonly float[] delta = new float[Outputs], hiddenDelta = new float[Hidden];
        public int Updates { get; private set; }

        public TacticalPolicy(int seed = 18)
        {
            var random = new Random(seed);
            Initialize(W1, random, Math.Sqrt(6.0 / (Inputs + Hidden)));
            Initialize(W2, random, Math.Sqrt(6.0 / (Hidden + Outputs)));
        }

        static void Initialize(float[] weights, Random random, double scale)
        {
            for (int i = 0; i < weights.Length; i++) weights[i] = (float)((random.NextDouble() * 2 - 1) * scale);
        }

        public void Predict(float[] input, float[] result)
        {
            Forward(input);
            Array.Copy(output, result, Outputs);
        }

        void Forward(float[] input)
        {
            if (input == null || input.Length != Inputs) throw new ArgumentException("Expected 12 observations.");
            for (int j = 0; j < Hidden; j++)
            {
                float sum = B1[j];
                for (int i = 0; i < Inputs; i++) sum += input[i] * W1[j * Inputs + i];
                hidden[j] = (float)Math.Tanh(sum);
            }
            for (int k = 0; k < Outputs; k++)
            {
                float sum = B2[k];
                for (int j = 0; j < Hidden; j++) sum += hidden[j] * W2[k * Hidden + j];
                output[k] = sum;
            }
            Softmax(output, 0, 5);
            output[5] = 1f / (1f + (float)Math.Exp(-Math.Max(-30, Math.Min(30, output[5]))));
            Softmax(output, 6, 3);
            output[9] = 1f / (1f + (float)Math.Exp(-Math.Max(-30, Math.Min(30, output[9]))));
        }

        static void Softmax(float[] values, int start, int count)
        {
            float max = values[start], sum = 0;
            for (int i = start + 1; i < start + count; i++) max = Math.Max(max, values[i]);
            for (int i = start; i < start + count; i++) { values[i] = (float)Math.Exp(values[i] - max); sum += values[i]; }
            for (int i = start; i < start + count; i++) values[i] /= sum;
        }

        public float Loss(Example sample)
        {
            Forward(sample.State);
            return (float)(-Math.Log(Math.Max(1e-7, output[sample.Move]))
                - Math.Log(Math.Max(1e-7, sample.Fire ? output[5] : 1 - output[5]))
                - Math.Log(Math.Max(1e-7, output[6 + sample.Power]))
                - Math.Log(Math.Max(1e-7, sample.Crouch ? output[9] : 1 - output[9]))) / 4;
        }

        public void Train(Example sample, float rate = .008f)
        {
            Forward(sample.State);
            for (int i = 0; i < Outputs; i++) delta[i] = output[i];
            delta[sample.Move] -= 1;
            delta[5] -= sample.Fire ? 1 : 0;
            delta[6 + sample.Power] -= 1;
            delta[9] -= sample.Crouch ? 1 : 0;
            // Calculate hidden gradients before changing the output weights.
            for (int j = 0; j < Hidden; j++)
            {
                float sum = 0;
                for (int k = 0; k < Outputs; k++) sum += W2[k * Hidden + j] * delta[k];
                hiddenDelta[j] = sum * (1 - hidden[j] * hidden[j]);
            }
            for (int k = 0; k < Outputs; k++)
            {
                B2[k] -= rate * Clip(delta[k]);
                for (int j = 0; j < Hidden; j++) W2[k * Hidden + j] -= rate * Clip(delta[k] * hidden[j]);
            }
            for (int j = 0; j < Hidden; j++)
            {
                B1[j] -= rate * Clip(hiddenDelta[j]);
                for (int i = 0; i < Inputs; i++) W1[j * Inputs + i] -= rate * Clip(hiddenDelta[j] * sample.State[i]);
            }
            Updates++;
        }

        static float Clip(float value) => Math.Max(-1, Math.Min(1, value));

        public static int ArgMax(float[] probabilities, int start, int count)
        {
            int best = start;
            for (int i = start + 1; i < start + count; i++) if (probabilities[i] > probabilities[best]) best = i;
            return best - start;
        }

        public static int Sample(float[] probabilities, int start, int count, Random random)
        {
            double value = random.NextDouble();
            for (int i = 0; i < count; i++)
            {
                value -= probabilities[start + i];
                if (value <= 0) return i;
            }
            return count - 1;
        }

        public PolicyData Export() => new PolicyData
        {
            version = 2, updates = Updates,
            w1 = (float[])W1.Clone(), b1 = (float[])B1.Clone(),
            w2 = (float[])W2.Clone(), b2 = (float[])B2.Clone()
        };

        public void Import(PolicyData data)
        {
            if (data == null || (data.version != 1 && data.version != 2) || data.updates < 0) throw new ArgumentException("Unknown model format.");
            Validate(data.w1, W1.Length); Validate(data.b1, B1.Length);
            int oldOutputs = data.version == 1 ? 9 : Outputs;
            Validate(data.w2, Hidden * oldOutputs); Validate(data.b2, oldOutputs);
            Array.Copy(data.w1, W1, W1.Length); Array.Copy(data.b1, B1, B1.Length);
            Array.Copy(data.w2, W2, data.w2.Length); Array.Copy(data.b2, B2, data.b2.Length);
            if(data.version == 1)
            {
                // Preserve all learned v1 actions. The new stance head has no demonstrations yet.
                Array.Clear(W2,9 * Hidden,Hidden);
                B2[9] = -6;
            }
            Updates = data.updates;
        }

        static void Validate(float[] values, int length)
        {
            if (values == null || values.Length != length) throw new ArgumentException("Incorrect weight dimensions.");
            foreach (float value in values)
                if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 10000) throw new ArgumentException("Invalid weight.");
        }

        public sealed class Example
        {
            public readonly float[] State;
            public readonly int Move, Power;
            public readonly bool Fire, Crouch;
            public Example(float[] state, int move, bool fire, int power, bool crouch = false)
            {
                if (state == null || state.Length != Inputs || move < 0 || move > 4 || power < 0 || power > 2)
                    throw new ArgumentException("Invalid demonstration.");
                State = (float[])state.Clone(); Move = move; Fire = fire; Power = power; Crouch = crouch;
            }
        }

        [Serializable]
        public sealed class PolicyData
        {
            public int version, updates;
            public float[] w1, b1, w2, b2;
        }
    }
}
