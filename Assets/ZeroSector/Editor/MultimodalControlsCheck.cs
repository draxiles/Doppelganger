using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // Exercises the same temporal gate and command routing as live devices. The
    // model itself is tested separately by HandGestureCheck / RussianVoiceCheck.
    public static class MultimodalControlsCheck
    {
        public static void Run()
        {
            const string report="Diagnostics/CampaignPreviews/multimodal-controls-check.txt";
            var log=new StringBuilder("Rich speech/gesture routing; no physical devices\n");
            GameObject fixture=null; var oldLock=Cursor.lockState; bool oldCursor=Cursor.visible;
            try
            {
                foreach(float step in new[]{.1f,.2f})
                {
                    var gate=new GestureCommandGate(); float now=0; int events=0;
                    for(int frame=0;frame<25;frame++) { if(gate.Update("shield",.95f,now)) events++; now+=step; }
                    Require(events==1 && gate.AcceptedAction=="shield","Stable pose fires once at "+1/step+" Hz");
                    for(int frame=0;frame<5;frame++) { gate.Update(null,0,now); now+=step; }
                    for(int frame=0;frame<10;frame++) { if(gate.Update("shield",.95f,now)) events++; now+=step; }
                    Require(events==2,"Neutral release rearms the same pose");
                    for(int frame=0;frame<20;frame++) { Require(!gate.Update("fire",.4f,now),"Low-confidence pose cannot fire"); now+=step; }
                    gate.Reset(); now=0;
                    Require(!gate.Update("stop",1,now),"An instantaneous palm does not flicker a command");
                    now=.21f; Require(gate.Update("stop",1,now),"Emergency stop uses the short hold");
                    gate.Reset(); gate.Update("forward",1,0); gate.SuppressCurrent();
                    Require(!gate.Update("forward",1,1),"Keyboard override suppresses the already-held camera pose");
                }
                foreach(var entry in SpokenCommandCatalog.Entries) foreach(string phrase in entry.Phrases)
                    Require(SpokenCommandCatalog.TryResolve(phrase,out string action) && action==entry.Action,"Shared phrase mapping: "+phrase);
                fixture=new GameObject("Rich input routing fixture") { hideFlags=HideFlags.HideAndDontSave }; fixture.SetActive(false);
                var game=fixture.AddComponent<CampaignGame>(); var input=fixture.AddComponent<MultimodalCommandInput>();
                typeof(CampaignGame).GetField("<MainMenu>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(game,false);
                game.EnsureCommands();
                typeof(MultimodalCommandInput).GetField("game",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(input,game);
                var speak=typeof(MultimodalCommandInput).GetMethod("ApplyPhrase",BindingFlags.Instance|BindingFlags.NonPublic);
                var pose=typeof(MultimodalCommandInput).GetMethod("ProcessGesture",BindingFlags.Instance|BindingFlags.NonPublic);
                speak.Invoke(input,new object[]{"вперёд"});
                Require(input.VoiceCommandCount==1 && game.Commands.Movement.y>0,"Recognized speech reaches actual movement executor");
                speak.Invoke(input,new object[]{"стоп"});
                Require(input.VoiceCommandCount==2 && game.Commands.Movement==Vector2.zero,"Spoken stop releases movement");
                speak.Invoke(input,new object[]{"перезарядка"});
                Require(input.VoiceCommandCount==3 && game.Commands.CurrentAction.Contains("Перезарядка"),"Speech routes weapon actions rather than energy presets");
                int count=input.VoiceCommandCount; speak.Invoke(input,new object[]{"неизвестная фраза"});
                Require(input.VoiceCommandCount==count,"Unknown speech is not counted as a command");
                for(int frame=0;frame<9;frame++) pose.Invoke(input,new object[]{new HandGestureResult{Action="forward",Pose="Указательный вверх",Confidence=1,Hands=Array.Empty<HandGestureHand>()},frame*.1f});
                Require(input.CameraCommandCount==1 && game.Commands.Movement.y>0,"Stable recognized gesture routes and renews the real camera movement lease");
                pose.Invoke(input,new object[]{new HandGestureResult{Confidence=0,Hands=Array.Empty<HandGestureHand>()},1f});
                Require(game.Commands.Movement==Vector2.zero,"Losing the hand stops camera-owned movement immediately");
                log.AppendLine("RESULT: PASSED");
            }
            catch(Exception error) { log.AppendLine("RESULT: FAILED\n"+error); throw; }
            finally
            {
                if(fixture) UnityEngine.Object.DestroyImmediate(fixture);
                Cursor.lockState=oldLock; Cursor.visible=oldCursor;
                Directory.CreateDirectory(Path.GetDirectoryName(report)); File.WriteAllText(report,log.ToString());
            }
            void Require(bool condition,string text) { log.AppendLine((condition ? "PASS " : "FAIL ")+text); if(!condition) throw new InvalidOperationException(text); }
        }
    }
}
