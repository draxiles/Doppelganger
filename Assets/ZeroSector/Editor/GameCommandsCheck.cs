using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // The actual campaign input step, real CharacterController and imported weapon
    // prefabs in a disposable preview scene. No profile, device or model is opened.
    public static class GameCommandsCheck
    {
        const string Folder="Diagnostics/CampaignPreviews";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before checking semantic commands.");
            Directory.CreateDirectory(Folder);
            var log=new StringBuilder("Campaign semantic command integration / real player and weapon components\n");
            var scene=EditorSceneManager.NewPreviewScene();
            GameObject fixture=null; Keyboard keyboard=null; var previousKeyboard=Keyboard.current;
            var previousLock=Cursor.lockState; bool previousVisible=Cursor.visible;
            var dataField=typeof(CampaignSession).GetField("<Data>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic);
            var originalData=CampaignSession.Data;
            var originalFlags=new System.Collections.Generic.List<string>(CampaignSession.Flags);
            try
            {
                dataField.SetValue(null,new CampaignSession.SaveData());
                CampaignSession.Flags.Clear();
                fixture=new GameObject("Isolated semantic command fixture"); fixture.SetActive(false); SceneManager.MoveGameObjectToScene(fixture,scene);
                var game=fixture.AddComponent<CampaignGame>(); game.chapter=1; Set(game,"MainMenu",false);
                var actor=new GameObject("Actual character controller",typeof(CharacterController)); actor.transform.SetParent(fixture.transform,false);
                actor.transform.position=new Vector3(10000,10000,10000);
                var body=actor.GetComponent<CharacterController>(); body.height=1.8f; body.center=Vector3.up*.9f; body.radius=.3f; body.skinWidth=.02f; body.minMoveDistance=0;
                var eye=new GameObject("Command check camera",typeof(Camera)); eye.transform.SetParent(actor.transform,false); eye.transform.localPosition=Vector3.up*1.65f;
                var camera=eye.GetComponent<Camera>(); camera.enabled=false;
                var view=fixture.AddComponent<FirstPersonPresentation>();
                view.weaponPivot=new GameObject("Real weapons").transform; view.weaponPivot.SetParent(camera.transform,false);
                foreach(string kind in new[]{"Terraformer","Robot"})
                {
                    var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DoppelgangerGenerated/Weapons/"+kind+".prefab");
                    if(!asset) throw new InvalidOperationException("Missing actual weapon prefab: "+kind);
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(asset,view.weaponPivot);
                    var binding=instance.GetComponent<WeaponArtBinding>();
                    if(!binding) throw new InvalidOperationException("Missing weapon art binding: "+kind);
                    var specification=binding.specification; specification.root=instance.transform; view.weapons.Add(specification);
                }
                fixture.SetActive(true); view.Initialize(body,camera);
                game.player=body; game.eyes=camera; game.presentation=view;
                typeof(CampaignGame).GetField("crouch",Private).SetValue(game,new CommanderCrouch(body,view.eyeHeight));
                typeof(CampaignGame).GetField("interactions",Private).SetValue(game,Array.Empty<CampaignInteractable>());
                float now=0; var commands=new GameCommandController(game,()=>now); Set(game,"Commands",commands);
                var input=typeof(CampaignGame).GetMethod("ApplyPlayerInput",Private);
                void Step(float dt=.05f,Keyboard keys=null) { input.Invoke(game,new object[]{dt,keys,null}); now+=dt; }
                void Require(bool condition,string label)
                { log.AppendLine((condition ? "PASS " : "FAIL ")+label); if(!condition) throw new InvalidOperationException(label); }

                Vector3 initial=body.transform.position; Require(commands.Execute("forward"),"Forward request accepted in active gameplay"); Step();
                Require(body.transform.position.z>initial.z+.15f,"Forward request physically moves the actual CharacterController without a keyboard/mouse");
                now+=3.1f; initial=body.transform.position; Step();
                Require(Mathf.Abs(body.transform.position.z-initial.z)<.001f && commands.Movement==Vector2.zero,"Speech movement expires automatically after three seconds");
                foreach(string action in new[]{"backward","left","right"})
                {
                    initial=body.transform.position; commands.Execute(action); Step();
                    Vector3 delta=body.transform.position-initial;
                    Require(action=="backward" ? delta.z<-.15f : action=="left" ? delta.x<-.15f : delta.x>.15f,"Actual directional movement / "+action);
                }
                commands.Execute("stop"); initial=body.transform.position; Step();
                Require(Mathf.Abs(body.transform.position.x-initial.x)<.001f && Mathf.Abs(body.transform.position.z-initial.z)<.001f,"Stop releases remote movement immediately");
                commands.RenewMovement("forward",.35f); Step(); now+=.4f;
                Require(commands.Movement==Vector2.zero,"Camera movement lease expires when landmark frames stop arriving");
                game.SetEnergy(0); float stamina=game.Stamina; commands.Execute("run"); initial=body.transform.position; Step();
                Require(view.IsSprinting && game.Stamina<stamina && body.transform.position.z-initial.z>.3f && game.Energy==0,"Run uses real sprint, stamina and speed independently of suit energy mode");
                commands.Execute("walk"); Step(); Require(!view.IsSprinting,"Walk returns to normal locomotion"); commands.Execute("stop");

                keyboard=InputSystem.AddDevice<Keyboard>("SemanticCommandCheckKeyboard");
                InputState.Change(keyboard,new KeyboardState(Key.S)); commands.Execute("forward"); initial=body.transform.position; Step(.05f,keyboard);
                Require(body.transform.position.z<initial.z && commands.Movement==Vector2.zero,"Physical WASD overrides and cancels remote movement");
                InputState.Change(keyboard,new KeyboardState());
                commands.Execute("crouch"); Step(); Require(body.height<1.2f && commands.WantsCrouch,"Crouch changes actual capsule and eye height through shared stance logic");
                commands.Execute("stand"); Step(); Require(Mathf.Abs(body.height-1.8f)<.001f,"Stand restores the actual standing capsule when clear");
                commands.Execute("crouch"); Step(); float before=body.transform.position.y; commands.Execute("jump"); Step();
                Require(body.transform.position.y<=before,"Jump cannot bypass crouch/grounded restrictions"); commands.Execute("stand"); Step();

                commands.Execute("shield"); Step(); Require(game.Energy==1,"Shield action selects actual deployed-field mode");
                commands.Execute("weapon"); Step(); Require(game.Energy==0,"Equip weapon action retracts shield through actual weapon mode");
                view.RefillAmmo(); int magazine=view.MagazineAmmo,shots=CampaignSession.Data.shots;
                commands.Execute("fire"); Step();
                Require(view.MagazineAmmo==magazine-1 && CampaignSession.Data.shots==shots+1,"Fire command spends one real round and records one actual shot");
                for(int i=0;i<5;i++) Step();
                Require(view.MagazineAmmo==magazine-1,"Fire is a single shot, not a stuck automatic trigger");
                commands.Execute("reload"); Step(); Require(view.IsReloading && !view.CanFire,"Reload command starts native weapon reload and blocks fire");
                view.ResetView(); commands.Execute("inspect"); Step(); Require(view.IsInspecting,"Inspect command starts the real weapon inspection animation");
                view.ResetView(); commands.Execute("switch"); Step(); Require(view.IsSwitching,"Switch command starts the real lower/swap/raise weapon animation");
                view.Equip(0); commands.Execute("aim"); Step(); Require(commands.WantsAim && !view.IsSprinting,"Aim enters the common aiming state");
                commands.Execute("unaim"); Step(); Require(!commands.WantsAim,"Lower aim clears the remote aiming state");

                var terminalObject=new GameObject("Actual focus target",typeof(CampaignInteractable)); terminalObject.transform.SetParent(fixture.transform,false);
                terminalObject.transform.position=camera.transform.position+camera.transform.forward*1.3f;
                var terminal=terminalObject.GetComponent<CampaignInteractable>(); terminal.id="nav"; terminal.title="Test nav"; terminal.range=3;
                typeof(CampaignGame).GetField("interactions",Private).SetValue(game,new[]{terminal}); game.chapter=0;
                commands.Execute("interact"); Step();
                Require(game.Notice.Contains("питание") && !game.TerminalOpen,"Interact uses actual camera focus and the terminal's gameplay prerequisite rather than activating arbitrary distant objects");
                game.chapter=1;

                commands.Execute("forward"); commands.Execute("fire"); commands.Execute("pause");
                Require(game.Paused && commands.Movement==Vector2.zero && !commands.WantsAim,"Pause clears movement and queued gameplay actions");
                Require(!commands.Execute("reload") && !commands.Execute("forward"),"Paused gameplay rejects movement and weapon actions");
                Require(commands.Execute("resume") && game.Running,"Resume safely restores active gameplay");
                magazine=view.MagazineAmmo; Step(); Require(view.MagazineAmmo==magazine,"A queued shot cannot leak through pause/resume");
                Set(game,"TerminalOpen",true); Require(!commands.Execute("run") && commands.Movement==Vector2.zero,"Open terminals reject gameplay movement");
                Set(game,"TerminalOpen",false); Set(game,"Dead",true); Require(!commands.Execute("fire"),"Dead players cannot fire through remote input");
                Set(game,"Dead",false); Set(game,"MainMenu",true); Require(!commands.Execute("run"),"Main menu rejects gameplay commands");
                Set(game,"MainMenu",false); Require(!commands.Execute("unrecognised-command"),"Unknown commands cannot execute arbitrary actions");
                // Grounded positive jump through the actual shared controller path.
                // Place a broad local floor away from every authored scene collider.
                var floor=new GameObject("Semantic commands grounded jump floor",typeof(BoxCollider)); floor.transform.SetParent(fixture.transform,false);
                floor.transform.position=new Vector3(10000,9999.5f,10000); floor.GetComponent<BoxCollider>().size=new Vector3(12,1,12);
                commands.Clear(); body.enabled=false; body.transform.position=new Vector3(10000,10000.05f,10000); body.enabled=true;
                typeof(CampaignGame).GetField("vertical",Private).SetValue(game,-2f);
                Physics.SyncTransforms();
                for(int i=0;i<8;i++) Step();
                Require(body.isGrounded,"Actual character controller settles on the isolated floor");
                before=body.transform.position.y; commands.Execute("jump"); Step();
                Require(body.transform.position.y>before+.15f,"Jump command produces an actual upward grounded jump");

                // A semantic inspect arriving alongside F is consumed exactly once.
                commands.Clear(); view.ResetView(); InputState.Change(keyboard,new KeyboardState(Key.F));
                commands.Execute("inspect"); Step(.05f,keyboard); Require(view.IsInspecting,"Physical F and semantic inspect share one action frame");
                view.ResetView(); InputState.Change(keyboard,new KeyboardState()); Step();
                Require(!view.IsInspecting,"Simultaneous physical and remote input cannot duplicate inspection on the next frame");
                log.AppendLine("RESULT: PASSED");
            }
            catch(Exception exception) { log.AppendLine("RESULT: FAILED\n"+exception); throw; }
            finally
            {
                if(keyboard!=null) InputSystem.RemoveDevice(keyboard);
                if(previousKeyboard!=null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if(fixture) UnityEngine.Object.DestroyImmediate(fixture);
                EditorSceneManager.ClosePreviewScene(scene);
                dataField.SetValue(null,originalData);
                CampaignSession.Flags.Clear(); foreach(string flag in originalFlags) CampaignSession.Flags.Add(flag);
                Cursor.lockState=previousLock; Cursor.visible=previousVisible;
                File.WriteAllText(Folder+"/game-commands-check.txt",log.ToString());
            }
        }
        static void Set<T>(CampaignGame game,string property,T value) =>
            typeof(CampaignGame).GetField("<"+property+">k__BackingField",Private).SetValue(game,value);
    }
}
