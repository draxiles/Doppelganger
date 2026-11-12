using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // Offline checks: real MediaPipe photo inference + synthetic 21-point poses.
    // Does not start any physical camera or microphone, does not enter Play mode.
    public static class HandGestureCheck
    {
        [Serializable] sealed class Batch { public Example[] Results; }
        [Serializable] sealed class Example { public string File; public HandGestureHand[] Hands; }

        public static void Run()
        {
            string report="Diagnostics/CampaignPreviews/hand-gesture-check.txt";
            var log=new StringBuilder("MediaPipe Hand Landmarker / offline photo and geometry checks\n");
            try
            {
                string path="ThirdPartyStaging/HandGestures/inference-results.json";
                Require(File.Exists(path),"Real model photo inference report exists");
                var batch=JsonUtility.FromJson<Batch>(File.ReadAllText(path));
                Require(batch?.Results!=null && batch.Results.Length>=4,"Official model examples were actually inferred");
                foreach(var example in batch.Results)
                {
                    var result=new HandGestureResult { Hands=example.Hands };
                    HandGestureRecognizer.Classify(result);
                    string file=Path.GetFileName(example.File),expected="";
                    if(file=="thumbs_down.jpg") expected="crouch";
                    if(file=="thumbs_up.jpg") expected="jump";
                    if(file=="victory.jpg") expected="run";
                    if(file=="pointing_up.jpg") expected="forward";
                    log.AppendLine($"Actual ML photo {file}: {result.Hands?.Length??0} hand(s), action={result.Action}, handedness confidence={result.Confidence:F3}");
                    if(expected!="") Require(result.Action==expected,$"Actual inference {file}: expected {expected}, got {result.Action}");
                    if(file=="pointing_up.jpg" || file=="victory.jpg")
                    {
                        string bridgeAction=file=="pointing_up.jpg" ? "rifle" : "pistol";
                        HandGestureRecognizer.Classify(result,bridgeGestures:true);
                        Require(result.Action==bridgeAction,$"Bridge inference {file}: expected {bridgeAction}, got {result.Action}");
                    }
                }
                Expect(Make(0,false),"shield"); Expect(Make(15,true),"stop"); Expect(Make(15,false),"stand");
                Expect(Make(1,false),"forward"); Expect(Rotate(Make(1,false),180),"backward");
                Expect(Rotate(Make(1,false),-90),"left"); Expect(Rotate(Make(1,false),90),"right");
                Expect(Make(3,false),"run"); Expect(Make(3,true),"walk"); Expect(Make(0,true),"jump"); Expect(Rotate(Make(0,true),180),"crouch");
                Expect(Make(7,false),"reload"); Expect(Make(8,false),"inspect");
                Expect(Make(9,false),"switch"); Expect(Make(9,true),"weapon");
                Expect(Make(1,false),"rifle",bridgeGestures:true);
                Expect(Make(3,false),"pistol",bridgeGestures:true);
                Expect(Make(0,false),"shield",bridgeGestures:true);
                Expect(Make(1,true),"",bridgeGestures:true);
                Expect(Make(3,true),"",bridgeGestures:true);
                CheckBridgeGestureGate();
                var ok=Make(14,false); ok.Landmarks[4]=ok.Landmarks[8]; Expect(ok,"interact");
                ExpectTwo(Make(15,true),Make(15,true),"pause");
                ExpectTwo(Make(0,false),Make(0,false),"aim");
                ExpectTwo(Make(15,true),Make(0,false),"fire");
                ExpectTwo(Make(3,false),Make(1,false),"");
                var uncertain=Make(0,false); uncertain.Confidence=.4f; Expect(uncertain,"");
                ExpectTwo(Make(0,false),uncertain,"");
                var malformed=Make(0,false); malformed.Landmarks[8]=new Vector3(float.NaN,0,0); Expect(malformed,"");
                var empty=new HandGestureResult { Hands=Array.Empty<HandGestureHand>() }; HandGestureRecognizer.Classify(empty);
                Require(empty.Action=="","No hand is neutral");
                string runtime=Path.Combine(Application.streamingAssetsPath,"HandGestures");
                Require(File.Exists(Path.Combine(runtime,"hand-worker.zip")),"Autonomous worker and model ZIP is bundled offline");
                Require(File.Exists("ThirdPartyStaging/HandGestures/worker-protocol-check.json"),"Packaged worker binary frame protocol tested");
                CheckWrapper(log);
                log.AppendLine("PASSED: real ML photo inference; bridge weapon/shield mapping and hold gate; ambiguous/uncertain/invalid/no-hand rejection; packaged protocol. No physical webcam tested.");
            }
            catch(Exception error) { log.AppendLine("FAILED: "+error); }
            Directory.CreateDirectory(Path.GetDirectoryName(report)); File.WriteAllText(report,log.ToString());
            Debug.Log(log.ToString());
        }

        static void CheckWrapper(StringBuilder log)
        {
            Texture2D texture=null;
            try
            {
                texture=new Texture2D(2,2,TextureFormat.RGB24,false);
                Require(texture.LoadImage(File.ReadAllBytes("ThirdPartyStaging/HandGestures/test-images/unity-pointing-up.png")),"Photo fixture decodes in Unity");
                using(var recognizer=new HandGestureRecognizer())
                {
                    recognizer.Start();
                    var clock=System.Diagnostics.Stopwatch.StartNew();
                    while(!recognizer.IsReady && clock.Elapsed.TotalSeconds<45) Thread.Sleep(40);
                    Require(recognizer.IsReady,"Actual C# sidecar startup/cache: "+recognizer.Status);
                    recognizer.Submit(texture.GetPixels32(),texture.width,texture.height,0,false);
                    HandGestureResult result=null;
                    clock.Restart();
                    while(clock.Elapsed.TotalSeconds<8 && !recognizer.TryGetResult(out result)) Thread.Sleep(20);
                    Require(result!=null && result.Action=="forward","Actual Unity RGB -> portable ML -> C# pose: "+(result?.Action??"timeout"));
                    log.AppendLine($"Actual C# worker bridge: action={result.Action}, {result.Hands.Length} hand, inference={result.DurationMs:F1}ms, cache prepared without UI thread inference.");
                    recognizer.Stop(); Require(!recognizer.IsReady,"Stop revokes camera recognition immediately");
                }
            }
            finally { if(texture) UnityEngine.Object.DestroyImmediate(texture); }
        }

        static void Expect(HandGestureHand hand,string expected,bool bridgeGestures=false)
        {
            var result=new HandGestureResult { Hands=new[]{hand} };
            HandGestureRecognizer.Classify(result,bridgeGestures);
            Require(result.Action==expected,$"Synthetic expected {expected}, got {result.Action} ({result.Pose})");
        }
        static void CheckBridgeGestureGate()
        {
            var gate=new GestureCommandGate();
            Require(!gate.Update("pistol",.99f,0),"Pistol does not trigger from one frame");
            Require(!gate.Update("pistol",.99f,.2f) && !gate.Update("pistol",.99f,.4f),"Pistol requires a sustained pose");
            Require(gate.Update("pistol",.99f,.65f),"A sustained pistol pose is accepted");
            Require(!gate.Update("pistol",.99f,.90f) && !gate.Update("pistol",.99f,1.15f),"Holding the pose emits one action");
        }
        static void ExpectTwo(HandGestureHand first,HandGestureHand second,string expected)
        {
            var result=new HandGestureResult { Hands=new[]{first,second} }; HandGestureRecognizer.Classify(result);
            Require(result.Action==expected,$"Two-hand expected {expected}, got {result.Action}");
        }
        static HandGestureHand Make(int mask,bool thumb)
        {
            var p=new Vector3[21]; p[0]=new Vector3(.5f,.72f,0);
            p[1]=new Vector3(.43f,.65f,0);
            if(thumb) { p[2]=new Vector3(.35f,.58f,0); p[3]=new Vector3(.27f,.51f,0); p[4]=new Vector3(.19f,.44f,0); }
            else { p[2]=new Vector3(.37f,.59f,0); p[3]=new Vector3(.46f,.55f,0); p[4]=new Vector3(.54f,.55f,0); }
            for(int f=0;f<4;f++)
            {
                int i=5+f*4; float x=.43f+f*.07f,y=f==1?.48f:(f==3?.53f:.5f);
                p[i]=new Vector3(x,y,0); p[i+1]=new Vector3(x,y-.075f,0);
                if((mask&(1<<f))!=0) { p[i+2]=new Vector3(x,y-.15f,0); p[i+3]=new Vector3(x,y-.22f,0); }
                else { p[i+2]=new Vector3(x,y+.02f,0); p[i+3]=new Vector3(x,y+.04f,0); }
            }
            return new HandGestureHand { Side="Left", Confidence=.99f, Landmarks=p };
        }
        static HandGestureHand Rotate(HandGestureHand hand,float degrees)
        {
            float a=degrees*Mathf.Deg2Rad;
            for(int i=0;i<hand.Landmarks.Length;i++)
            {
                Vector3 p=hand.Landmarks[i]-new Vector3(.5f,.5f,0);
                hand.Landmarks[i]=new Vector3(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a)+.5f,p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a)+.5f,p.z);
            }
            return hand;
        }
        static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    }
}
