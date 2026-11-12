using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;
using InputKey=UnityEngine.InputSystem.Key;

namespace ZeroSector.Editor
{
    /// <summary>
    /// Exercises the three saved, authored campaign scenes and their real runtime components.
    /// Input is supplied through Unity's virtual devices, never through Windows input.
    /// All saves use an explicit project-local test profile; microphone/webcam stay off.
    /// </summary>
    [InitializeOnLoad]
    public static class CampaignIntegrationCheck
    {
        const string Key="Doppelganger.CampaignCheck", Previous="Doppelganger.CampaignCheck.Scene", Background="Doppelganger.CampaignCheck.Background";
        const string Environment=Key+".Environment", InputId=Key+".InputId", InputPath=Key+".InputPath", InputJson=Key+".InputJson", FixtureInputId=Key+".FixtureInputId", PreviousTimeScale=Key+".TimeScale";
        const string InputFlags=Key+".InputFlags", LastValidInputJson=Key+".LastValidInputJson";
        const string Report="Diagnostics/CampaignPreviews/campaign-check.txt";
        const string Profile="Temp/DoppelgangerChecks/campaign-check-save.json";
        static CampaignGame game;
        static Keyboard keyboard;
        static Mouse mouse;
        static int phase=-1,bridgeStep,initialSamples,initialUpdates,initialShots,coreUpdates;
        static double begun,waitUntil;
        static bool completed;
        static string frozenPolicy,realSave;
        static byte[] realSaveBefore;
        static float beforeBossHealth,beforeBossShield;
        static CampaignGame beforeContinue;
        static CampaignSession.SaveData bridgeSnapshot;
        static int initialBossShots,initialBossCrouches,initialBossHits;
        static bool sawBossWarning,sawEarthReply;
        static double sourceShotTime;
        static double nextBossFixturePlacement;
        static Vector3 bossStart,mirrorStep;
        static string mirrorMove;
        static float healthBeforeEcho;
        static bool sawPrologueImpact,sawPrologueCamera,sawPrologueCommander,sawAutonomousWarning;
        static bool sawBoarding;
        static int initialAutonomousShots;
        static int pistolShots,pistolHits,pistolReserve;
        static Quaternion antennaRest;
        static Vector3 briefingPlayerPosition;
        static float briefingElapsed,briefingRadioProgress;
        static bool inputCrouch,inputInteract,inputForward,sawStandingRecovery;
        static int dynamicInputUpdates,interactEdges,interactBeginFrame,sealInteractEdges;
        static double bossCrouchAt,commanderVisibleAt;

        static CampaignIntegrationCheck()
        {
            EditorApplication.playModeStateChanged+=ModeChanged;
            if(SessionState.GetBool(Key,false)) Attach();
            else if(SessionState.GetBool(Environment,false)) RestoreEnvironment();
        }

        public static void Start()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Stop Play Mode and finish importing before checking the campaign.");
            var current=SceneManager.GetActiveScene();
            if(current.isDirty || string.IsNullOrEmpty(current.path))
                throw new InvalidOperationException("Save the current scene before checking the campaign.");
            foreach(string path in CampaignGame.ScenePaths)
                if(!File.Exists(path)) throw new FileNotFoundException("Build the three playable episodes first: "+path);
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            Directory.CreateDirectory(Path.GetDirectoryName(Profile));
            File.WriteAllText(Report,"DOPPELGANGER / authored campaign integration check\n"+
                "Ramming prologue, boarding decks, three scenes, pistol, physical cargo, flight correction, checkpoints, guard combat, adaptive ADAM, EVA and radio.\n"+
                "The log and A-17 terminal use actual focus and keyboard input. The player walks through the open gate before and after Continue.\n"+
                "Other encounters place the player at validated free positions and use APIs for deterministic progression.\n");
            SessionState.SetString(Previous,current.path);
            CaptureEnvironment();
            SessionState.SetBool(Key,true);
            try
            {
                EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0],OpenSceneMode.Single);
                Attach(); EditorApplication.EnterPlaymode();
            }
            catch
            {
                SessionState.EraseBool(Key);
                EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
                RemoveVirtualInput(); RestoreEnvironment();
                EditorSceneManager.OpenScene(current.path,OpenSceneMode.Single); throw;
            }
        }

        static void Attach()
        {
            phase=-1; bridgeStep=0; completed=false; game=null; keyboard=null; mouse=null;
            beforeContinue=null; bridgeSnapshot=null; sawBossWarning=sawEarthReply=false;
            sawPrologueImpact=sawPrologueCamera=sawPrologueCommander=sawAutonomousWarning=false;
            sawBoarding=false;
            commanderVisibleAt=-1;
            inputCrouch=inputInteract=inputForward=sawStandingRecovery=false;
            dynamicInputUpdates=interactEdges=interactBeginFrame=sealInteractEdges=0;
            begun=EditorApplication.timeSinceStartup; waitUntil=begun+1;
            EditorApplication.update-=Tick; EditorApplication.update+=Tick;
            Application.logMessageReceived-=Log; Application.logMessageReceived+=Log;
        }
        static void Log(string message,string stack,LogType type)
        {
            if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)
                Finish(false,"Runtime error: "+message+"\n"+stack);
        }
        static void Check(bool condition,string label)
        {
            File.AppendAllText(Report,(condition ? "PASS: " : "FAIL: ")+label+"\n");
            if(!condition) throw new InvalidOperationException(label);
        }
        static void Begin(int next)
        {
            phase=next; begun=EditorApplication.timeSinceStartup;
            File.AppendAllText(Report,"PHASE "+next+"\n");
            EditorApplication.QueuePlayerLoopUpdate();
        }
        static bool Elapsed(double seconds) => EditorApplication.timeSinceStartup-begun>=seconds;
        static bool SceneReady(int chapter)
        {
            game=UnityEngine.Object.FindAnyObjectByType<CampaignGame>();
            return game && game.chapter==chapter && game.Devices && game.presentation && game.presentation.eyeHeight;
        }
        static CampaignInteractable Interaction(string id)
        {
            var result=UnityEngine.Object.FindObjectsByType<CampaignInteractable>().FirstOrDefault(item=>item.id==id);
            if(!result) throw new InvalidOperationException("Missing authored interaction: "+id);
            return result;
        }
        static void Use(string id) { game.Interact(Interaction(id)); }
        static string PolicyJson() => JsonUtility.ToJson(CampaignSession.Policy.Export());
        static FieldInfo Private(string name) => typeof(CampaignGame).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(CampaignGame).FullName,name);

        static void CheckBridgeView()
        {
            var tracking=new HeadViewTracking();
            for(int sample=0;sample<7;sample++) tracking.Observe(new Vector2(2,-3),sample*.04f);
            tracking.Observe(new Vector2(28,4),.29f);
            tracking.Tick(.29f,.1f);
            Quaternion bodyBefore=game.player.transform.rotation;
            game.presentation.SetHeadViewOffset(tracking.Offset);
            game.presentation.ApplyLook(0);
            float yaw=Vector3.Dot(game.player.transform.right,game.eyes.transform.forward);
            Check(tracking.IsCalibrated && yaw>.95f && game.player.transform.rotation==bodyBefore,
                "calibrated head yaw turns the actual bridge camera without turning the player's body");
            Vector3 position=game.player.transform.position;
            Vector3 gaze=Vector3.ProjectOnPlane(game.eyes.transform.forward,Vector3.up).normalized;
            Private("frameHeadOffset").SetValue(game,tracking.Offset);
            Check(game.Commands.Execute("forward"),"movement is accepted with calibrated head rotation");
            typeof(CampaignGame).GetMethod("ApplyPlayerInput",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(game,new object[]{.035f,null,null});
            Vector3 walked=Vector3.ProjectOnPlane(game.player.transform.position-position,Vector3.up);
            Check(walked.magnitude>.06f && Vector3.Dot(walked.normalized,gaze)>.94f,
                "movement follows the same gaze angle rendered by the camera");
            game.Commands.StopMotion();
            Private("frameHeadOffset").SetValue(game,Vector2.zero);
            tracking.LoseFace();
            tracking.Tick(.6f,.4f);
            Check(tracking.Offset.magnitude>0 && tracking.Offset.magnitude<16,"tracking loss eases rather than snapping away from the last valid gaze");
            tracking.Tick(1.2f,.6f);
            Check(tracking.Offset.magnitude<.4f,"tracking loss settles the view at the calibrated center");
            tracking.Recenter();
            Check(!tracking.IsCalibrated && tracking.Offset==Vector2.zero,"recenter clears the old head calibration");
            game.presentation.SetHeadViewOffset(Vector2.zero);
            game.presentation.ApplyLook(0);
        }

        static void CheckCombatRead()
        {
            var read=new AdamCombatRead();
            for(int sample=0;sample<10;sample++) read.Observe(.05f,true,Vector3.right*3,Vector3.right,false);
            Check(read.PredictingStrafe && read.AimAt(Vector3.zero).x>.45f,"repeated visible lateral movement changes the autonomous aim lead");
            read.Observe(.05f,false,Vector3.right*3,Vector3.right,false);
            Check(read.AimAt(Vector3.zero)==Vector3.zero,"hidden player movement cannot update aim prediction");
            for(int sample=0;sample<15;sample++) read.Observe(.05f,true,Vector3.zero,Vector3.right,true);
            Check(read.HoldingCover,"holding a crouched position makes an alternate approach eligible");
            read.RegisterShot(); read.RegisterShot();
            Check(read.RepeatedFire,"repeated player fire is recognized as a reflection opportunity");
        }

        static void CheckCameraSelection()
        {
            var cameras=new[]{"OBS Virtual Camera","USB Webcam","Windows Hello IR Camera","BisonCam,NB Pro"};
            Check(MultimodalCommandInput.ChooseCamera(cameras,"")=="BisonCam,NB Pro",
                "built-in BisonCam is selected even when OBS and an external camera come first");
            Check(MultimodalCommandInput.ChooseCamera(cameras,"OBS Virtual Camera")=="BisonCam,NB Pro",
                "a stored OBS choice cannot override the physical camera");
            Check(MultimodalCommandInput.ChooseCamera(cameras,"usb webcam")=="USB Webcam",
                "an explicit physical selection is respected");
            Check(MultimodalCommandInput.ChooseCamera(new[]{"OBS Virtual Camera","Windows Hello IR Camera"},null)==null,
                "only virtual or infrared devices leave the camera off");
            string[] detected=Array.ConvertAll(WebCamTexture.devices,device=>device.name);
            string chosen=MultimodalCommandInput.ChooseCamera(detected,null);
            File.AppendAllText(Report,"Unity devices (enumerated without capture): "+string.Join(" | ",detected)+"; automatic choice: "+(chosen ?? "none")+"\n");
            if(Array.Exists(detected,name=>name.StartsWith("BisonCam",StringComparison.OrdinalIgnoreCase)))
                Check(chosen!=null && chosen.StartsWith("BisonCam",StringComparison.OrdinalIgnoreCase),"actual machine's BisonCam is the default");
        }

        static void QueueInput(bool crouch=false,bool interact=false,bool forward=false)
        {
            inputCrouch=crouch; inputInteract=interact; inputForward=forward;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        static void CaptureEnvironment()
        {
            // A domain reload clears static references, but SessionState retains the exact
            // original object/configuration. Never mutate or save the project's settings asset.
            if(SessionState.GetBool(Environment,false)) RestoreEnvironment();
            var settings=RepairInputSettings();
            SessionState.SetString(InputId,EntityId.ToULong(settings.GetEntityId()).ToString());
            SessionState.SetString(InputPath,AssetDatabase.GetAssetPath(settings));
            SessionState.SetString(InputJson,EditorJsonUtility.ToJson(settings));
            SessionState.SetString(LastValidInputJson,EditorJsonUtility.ToJson(settings));
            SessionState.SetInt(InputFlags,(int)settings.hideFlags);
            SessionState.SetBool(Background,Application.runInBackground);
            SessionState.SetFloat(PreviousTimeScale,Time.timeScale);
            SessionState.SetBool(Environment,true);
        }
        static void InstallVirtualInput()
        {
            var original=RepairInputSettings();
            // InputManager.settings owns default objects tagged EXACTLY HideAndDontSave
            // and destroys them when another object is assigned. Retain the original
            // native object across the swap, including Unity's default editor settings.
            // Keep DontSave/DontUnload flags; remove only NotEditable until restoration.
            if(original.hideFlags==HideFlags.HideAndDontSave) original.hideFlags&=~HideFlags.NotEditable;
            var settings=UnityEngine.Object.Instantiate(original);
            settings.name="Campaign check / temporary virtual input settings";
            settings.hideFlags=HideFlags.HideAndDontSave;
            settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            SessionState.SetString(FixtureInputId,EntityId.ToULong(settings.GetEntityId()).ToString());
            InputSystem.settings=settings;
            Application.runInBackground=true; Time.timeScale=1;
            // Recover only this fixture's devices if scripts were reloaded during a run.
            foreach(var device in InputSystem.devices.Where(item=>item.name=="CampaignCheckKeyboard" || item.name=="CampaignCheckMouse").ToArray())
                InputSystem.RemoveDevice(device);
            keyboard=InputSystem.AddDevice<Keyboard>("CampaignCheckKeyboard");
            mouse=InputSystem.AddDevice<Mouse>("CampaignCheckMouse");
            InputSystem.onBeforeUpdate-=BeforeInput; InputSystem.onBeforeUpdate+=BeforeInput;
            InputSystem.onAfterUpdate-=AfterInput; InputSystem.onAfterUpdate+=AfterInput;
            Check(keyboard.enabled && mouse.enabled && InputSystem.settings.backgroundBehavior==InputSettings.BackgroundBehavior.IgnoreFocus &&
                InputSystem.settings.editorInputBehaviorInPlayMode==InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView,
                "isolated virtual input uses dynamic player updates independent of editor/Game View focus");
        }
        static void BeforeInput()
        {
            if(InputState.currentUpdateType!=InputUpdateType.Dynamic || keyboard==null || mouse==null) return;
            var held=new[]{inputCrouch ? InputKey.C : InputKey.None,inputInteract ? InputKey.E : InputKey.None,
                inputForward ? InputKey.W : InputKey.None,phase==34 || phase==36 ? InputKey.Enter : InputKey.None,
                phase==35 ? InputKey.F1 : InputKey.None}.Where(key=>key!=InputKey.None).ToArray();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(held));
            InputSystem.QueueStateEvent(mouse,new MouseState());
        }
        static void AfterInput()
        {
            if(InputState.currentUpdateType!=InputUpdateType.Dynamic || keyboard==null || mouse==null) return;
            // Physical editor devices may become current while their events are processed.
            // Gameplay must read only our fixture devices in the following MonoBehaviour.Update.
            keyboard.MakeCurrent(); mouse.MakeCurrent(); dynamicInputUpdates++;
            if(inputInteract && keyboard.eKey.wasPressedThisFrame) interactEdges++;
        }
        static void RemoveVirtualInput()
        {
            InputSystem.onBeforeUpdate-=BeforeInput; InputSystem.onAfterUpdate-=AfterInput;
            foreach(var device in InputSystem.devices.Where(item=>item.name=="CampaignCheckKeyboard" || item.name=="CampaignCheckMouse").ToArray())
                InputSystem.RemoveDevice(device);
            keyboard=null; mouse=null;
        }
        static void RestoreEnvironment()
        {
            if(!SessionState.GetBool(Environment,false)) return;
            var original=StoredSettings(InputId);
            bool recreated=false;
            string path=SessionState.GetString(InputPath,"");
            if(!original && !string.IsNullOrEmpty(path)) original=AssetDatabase.LoadAssetAtPath<InputSettings>(path);
            if(!original)
            {
                recreated=true;
                original=ScriptableObject.CreateInstance<InputSettings>();
                EditorJsonUtility.FromJsonOverwrite(SessionState.GetString(InputJson,"{}"),original);
                // A fallback created while playing must survive leaving Play Mode.
                // Ordinary transient ScriptableObjects are destroyed on that boundary.
                original.hideFlags=HideFlags.HideAndDontSave;
            }
            InputSystem.settings=original;
            var originalFlags=(HideFlags)SessionState.GetInt(InputFlags,(int)HideFlags.HideAndDontSave);
            original.hideFlags=recreated ? originalFlags|HideFlags.DontSave : originalFlags;
            var fixture=StoredSettings(FixtureInputId);
            if(fixture && fixture!=original && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(fixture))) UnityEngine.Object.DestroyImmediate(fixture);
            Application.runInBackground=SessionState.GetBool(Background,false);
            Time.timeScale=SessionState.GetFloat(PreviousTimeScale,1);
            SessionState.EraseBool(Environment); SessionState.EraseBool(Background);
            SessionState.EraseString(InputId); SessionState.EraseString(FixtureInputId);
            SessionState.EraseString(InputPath); SessionState.EraseString(InputJson); SessionState.EraseFloat(PreviousTimeScale);
            SessionState.EraseInt(InputFlags);
        }
        // Repairs a stale reference left by older fixtures without touching the
        // package, assets or project configuration. This runs before Play Mode's
        // runtime initializer asserts that InputSettings is a live Unity object.
        public static InputSettings RepairInputSettings()
        {
            if(InputSystem.settings) return InputSystem.settings;
            InputSettings settings;
            if(!EditorBuildSettings.TryGetConfigObject("com.unity.input.settings",out settings) || !settings)
            {
                settings=ScriptableObject.CreateInstance<InputSettings>();
                string previous=SessionState.GetString(LastValidInputJson,"");
                if(!string.IsNullOrEmpty(previous)) EditorJsonUtility.FromJsonOverwrite(previous,settings);
                settings.name="Input System / recovered temporary editor settings";
                settings.hideFlags=HideFlags.HideAndDontSave;
            }
            InputSystem.settings=settings;
            if(!InputSystem.settings) throw new InvalidOperationException("InputSettings recovery did not retain its native object.");
            return settings;
        }
        static InputSettings StoredSettings(string key) => ulong.TryParse(SessionState.GetString(key,""),out ulong id)
            ? EditorUtility.EntityIdToObject(EntityId.FromULong(id)) as InputSettings : null;
        static void PlaceFacingInteraction(string id)
        {
            var target=Interaction(id);
            var center=new Vector3(target.FocusPoint.x,.05f,target.FocusPoint.z);
            for(float distance=2;distance>=1.4f;distance-=.2f) for(int step=0;step<24;step++)
            {
                float angle=step*Mathf.PI/12;
                var point=center+new Vector3(Mathf.Sin(angle)*distance,0,Mathf.Cos(angle)*distance);
                bool floor=Physics.Raycast(point+Vector3.up,Vector3.down,1.5f,~0,QueryTriggerInteraction.Ignore);
                bool obstacle=Physics.OverlapCapsule(point+Vector3.up*.45f,point+Vector3.up*1.35f,.38f,~0,QueryTriggerInteraction.Ignore)
                    .Any(item=>item && !(item is CharacterController));
                if(!floor || obstacle) continue;
                var route=game.Navigation.Path(game.player.transform.position,point);
                if((route.Count==0 && Vector3.Distance(point,game.player.transform.position)>1) ||
                    (route.Count>0 && Vector3.Distance(route.Last(),point)>1.6f)) continue;
                var eye=point+Vector3.up*1.65f;
                if(Physics.Linecast(eye,target.FocusPoint,out var hit,~0,QueryTriggerInteraction.Ignore) &&
                    !hit.transform.IsChildOf(target.transform) && !target.transform.IsChildOf(hit.transform)) continue;
                PlacePlayerFacing(point,target.FocusPoint-point);
                float pitch=-Mathf.Asin((target.FocusPoint-game.eyes.transform.position).normalized.y)*Mathf.Rad2Deg;
                Private("pitch").SetValue(game,pitch); game.presentation.ApplyLook(pitch);
                File.AppendAllText(Report,"Focus position: "+point+"; "+id+" "+target.FocusPoint+"; navigable route nodes "+route.Count+"\n");
                return;
            }
            throw new InvalidOperationException("No free, reachable focus position within two metres of "+id+".");
        }

        static void PlacePlayerFacing(Vector3 position,Vector3 forward)
        {
            game.player.enabled=false;
            game.player.transform.SetPositionAndRotation(position,Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,Vector3.up)));
            game.player.enabled=true;
            Private("pitch").SetValue(game,0f); game.presentation.ApplyLook(0);
            Physics.SyncTransforms();
        }

        static Vector3 GateApproach => new(game.Boarding.Entry.position.x,.04f,-8.2f);
        static bool PressureGateBlocked(out RaycastHit hit) =>
            Physics.SphereCast(GateApproach+Vector3.up*1.05f,.30f,Vector3.forward,out hit,2.75f,~0,QueryTriggerInteraction.Ignore);

        static void CheckModelOnDeck(RobotPresentation model,Transform actor,string label,float minimumHead)
        {
            Check(model && model.head, label+" has a real animated head bone");
            var floor=Physics.RaycastAll(actor.position+Vector3.up*.65f,Vector3.down,1.5f,~0,QueryTriggerInteraction.Ignore)
                .Where(hit=>!(hit.collider is CharacterController) && !hit.transform.IsChildOf(actor))
                .OrderBy(hit=>hit.distance).FirstOrDefault();
            Check(floor.collider,label+" stands over a solid deck collider");
            var body=model.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(renderer=>renderer.enabled &&
                renderer.sharedMesh && renderer.name.Contains("Body",StringComparison.OrdinalIgnoreCase));
            Check(body,label+" has visible, skinned body geometry");
            var mesh=new Mesh();
            try
            {
                body.BakeMesh(mesh);
                float sole=mesh.vertices.Min(vertex=>body.transform.TransformPoint(vertex).y)-floor.point.y;
                float head=model.head.position.y-floor.point.y;
                Check(sole>=-.08f && sole<=.18f && head>=minimumHead,
                    $"{label} stays above the deck (lowest visible body vertex {sole:F3} m, head {head:F3} m)");
            }
            finally { UnityEngine.Object.Destroy(mesh); }
        }
        static void PlaceNearSecurity()
            => PlaceNearOpponent(game.Enemies.First(enemy=>enemy && !enemy.Dead),3,5);

        static void PlaceNearOpponent(CampaignEnemy target,int minimum,int maximum,bool verifySideStep=false)
        {
            for(float distance=minimum;distance<=maximum;distance++) for(int step=0;step<24;step++)
            {
                float angle=step*Mathf.PI/12;
                var point=target.transform.position+new Vector3(Mathf.Sin(angle)*distance,0,Mathf.Cos(angle)*distance);
                bool floor=Physics.Raycast(point+Vector3.up,Vector3.down,1.5f,~0,QueryTriggerInteraction.Ignore);
                bool obstacle=Physics.OverlapCapsule(point+Vector3.up*.45f,point+Vector3.up*1.35f,.38f,~0,QueryTriggerInteraction.Ignore)
                    .Any(item=>item && !(item is CharacterController));
                if(!floor || obstacle || !target.VisibleTo(point+Vector3.up*1.6f)) continue;
                var forward=target.transform.position-point; forward.y=0;
                if(verifySideStep)
                {
                    Vector3 right=Vector3.Cross(Vector3.up,forward.normalized);
                    if(ClearSideStep(point,right) && ClearSideStep(target.transform.position,PlayerMirrorTimeline.Reflect(right)))
                    { mirrorMove="right"; mirrorStep=PlayerMirrorTimeline.Reflect(right); }
                    else if(ClearSideStep(point,-right) && ClearSideStep(target.transform.position,PlayerMirrorTimeline.Reflect(-right)))
                    { mirrorMove="left"; mirrorStep=PlayerMirrorTimeline.Reflect(-right); }
                    else continue;
                }
                game.player.enabled=false; game.player.transform.position=point;
                game.player.transform.rotation=Quaternion.LookRotation(forward);
                game.player.enabled=true; Physics.SyncTransforms();
                File.AppendAllText(Report,"Actual return-fire position: "+point+"; "+(target.boss ? "ADAM" : "guard "+target.number)+"\n");
                return;
            }
            throw new InvalidOperationException("No unobstructed return-fire position near "+(target.boss ? "ADAM." : "authored guards."));
        }

        static bool ClearSideStep(Vector3 start,Vector3 direction)
        {
            return !Physics.SphereCast(start+Vector3.up*.82f,.39f,direction,out _,1.65f,~0,QueryTriggerInteraction.Ignore) &&
                Physics.Raycast(start+direction*1.65f+Vector3.up*.7f,Vector3.down,1.2f,~0,QueryTriggerInteraction.Ignore);
        }

        static void DropBattery()
        {
            if(game.Cargo.Drop()) return;
            // The focus fixture can stand against a console; rotate to a real clear lane.
            var initial=game.player.transform.rotation;
            for(int n=1;n<8;n++)
            {
                game.player.transform.rotation=initial*Quaternion.Euler(0,n*45,0);
                Physics.SyncTransforms();
                if(game.Cargo.Drop()) return;
            }
            throw new InvalidOperationException("No unobstructed lane to drop the held battery at the bridge focus position.");
        }

        static void AlignFlight()
        {
            const float step=.05f;
            for(int frame=0;frame<160 && game.Flight.Error.sqrMagnitude>.36f;frame++)
                game.Flight.Tick(Vector3.ClampMagnitude(game.Flight.Error*.19f,1),step);
            Check(game.Flight.Error.sqrMagnitude<1,"actual attitude input corrects all three axes");
            for(int frame=0;frame<30 && !game.Flight.Ready;frame++) game.Flight.Tick(Vector3.zero,step);
            Check(game.Flight.Ready && game.Flight.Stability>=1,"stable neutral input settles the corrected flight solution");
        }

        static void RestoreBridgeAndFly()
        {
            Check(game.Carrying && !game.Outside && game.Cargo.transform.parent==game.eyes.transform &&
                game.presentation.weaponPivot && !game.presentation.weaponPivot.gameObject.activeSelf,
                "real Continue reload restores carried battery and holstered weapon");
            game.presentation.CaptureAmmo(out var magazines,out var reserves);
            Check(magazines.SequenceEqual(bridgeSnapshot.magazines) && reserves.SequenceEqual(bridgeSnapshot.reserves) &&
                game.presentation.equippedIndex==bridgeSnapshot.equipped,
                "real Continue reload restores both weapon magazines, reserves and selected slot");
            Check(Mathf.Abs(Mathf.DeltaAngle(game.player.transform.eulerAngles.y,bridgeSnapshot.yaw))<.05f &&
                Mathf.Abs((float)Private("pitch").GetValue(game)-bridgeSnapshot.pitch)<.05f &&
                Mathf.Abs(game.Health-bridgeSnapshot.health)<.05f && game.Energy==bridgeSnapshot.energy,
                "real Continue reload restores saved aim, health and energy mode");
            Use("power");
            Check(CampaignSession.Has("power") && !game.Carrying && !game.Cargo.gameObject.activeSelf,
                "installing the carried battery powers the ship and consumes the physical item");
            Use("nav");
            Check(game.TerminalOpen && !game.Running && game.Flight.Active,"navigation opens the real attitude-control panel and suspends walking");
            game.CloseTerminal(1);
            Check(game.TerminalOpen && !CampaignSession.Has("nav"),"unsettled attitude cannot confirm the route or unlock the airlock");
            AlignFlight();
            game.CloseTerminal(1);
            Check(game.Running && game.Energy==1 && !game.Flight.Active && CampaignSession.Has("nav"),
                "confirmed flight returns camera and player control, commits nav and sets shield priority");
            CampaignSession.Data.credits=175; game.SaveCheckpoint(false);
            var checkpoint=CampaignSession.Data.checkpoint; string savedPolicy=PolicyJson();
            CampaignSession.Data.credits=0;
            Check(CampaignSession.Load(0) && CampaignSession.Data.credits==175 && CampaignSession.Has("nav") && CampaignSession.Data.checkpoint==checkpoint && PolicyJson()==savedPolicy,
                "save/load restores chapter, checkpoint, objective flags, currency and exact learned weights");
            var data=CampaignSession.Data;
            Check(!CampaignSession.Load(1) && ReferenceEquals(data,CampaignSession.Data) && CampaignSession.Data.chapter==0,
                "wrong-chapter checkpoint load is rejected without replacing live session data");
        }
        static void Capture(string name)
        {
            // Unity captures its own Game View at the end of the rendered frame, including IMGUI.
            // This does not activate an editor window or send desktop input.
            ScreenCapture.CaptureScreenshot("Diagnostics/CampaignPreviews/"+name+"-gameplay.png");
        }

        static void CheckBulletMarkOnCargoWall()
        {
            game.presentation.ResetView();
            bool foundWall=false;
            RaycastHit wall=default;
            foreach(float z in new[]{2f,4.5f,8.5f,10.5f,13f})
            {
                PlacePlayerFacing(game.Boarding.transform.TransformPoint(new Vector3(9f,.05f,z)),Vector3.right);
                if(Physics.Raycast(game.eyes.transform.position,game.eyes.transform.forward,out wall,3,~0,QueryTriggerInteraction.Ignore) &&
                    wall.collider.name=="Hold side bulkhead")
                {
                    foundWall=true;
                    break;
                }
            }
            Check(foundWall,"test sight is aimed at the actual cargo wall collision proxy");
            var priorMarks=game.GetComponentsInChildren<MeshRenderer>().Count(item=>item.name=="Bullet mark");
            typeof(CampaignGame).GetMethod("Fire",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(game,null);
            var marks=game.GetComponentsInChildren<MeshRenderer>().Where(item=>item.name=="Bullet mark").ToArray();
            var fresh=marks.LastOrDefault(item=>(item.transform.position-wall.point).sqrMagnitude<.000001f);
            Check(marks.Length==priorMarks+1 && fresh && !fresh.GetComponent<Collider>() &&
                fresh.sharedMaterial.shader.name=="Doppelganger/Bullet Mark",
                "the real weapon ray leaves a colliderless depth-projected mark at the wall impact");
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) return;

            const int size=384;
            var target=RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
            var pixels=new Texture2D(size,size,TextureFormat.RGB24,false);
            var previousTarget=RenderTexture.active;
            try
            {
                var request=new UniversalRenderPipeline.SingleCameraRequest { destination=target };
                fresh.enabled=false;
                RenderPipeline.SubmitRenderRequest(game.eyes,request);
                RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,size,size),0,0);
                var before=pixels.GetPixels32();
                fresh.enabled=true;
                RenderPipeline.SubmitRenderRequest(game.eyes,request);
                RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,size,size),0,0);
                var after=pixels.GetPixels32();
                int changed=0;
                for(int y=size/2-13;y<=size/2+13;y++)
                for(int x=size/2-13;x<=size/2+13;x++)
                {
                    int i=y*size+x;
                    if(Mathf.Abs(after[i].r-before[i].r)+Mathf.Abs(after[i].g-before[i].g)+Mathf.Abs(after[i].b-before[i].b)>24)
                        changed++;
                }
                File.WriteAllBytes("Diagnostics/CampaignPreviews/bullet-mark-cargo.png",pixels.EncodeToPNG());
                Check(changed>=6,"URP renders the mark on the visible cargo wall in front of its collision proxy ("+changed+" changed pixels)");
            }
            finally
            {
                fresh.enabled=true;
                RenderTexture.active=previousTarget;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(pixels);
            }
        }

        static void Tick()
        {
            if(completed || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if(EditorApplication.isPaused) throw new InvalidOperationException("Play Mode paused during campaign check.");
                if(Elapsed(60)) throw new TimeoutException("Campaign phase "+phase+" exceeded 60 seconds.");
                QueueInput(phase==4 || phase==10 || phase==31,phase==0 && bridgeStep==2 || phase==25,phase==27 || phase==29 || phase==33);
                switch(phase)
                {
                    case -1:
                        if(EditorApplication.timeSinceStartup<waitUntil || !SceneReady(0)) return;
                        realSave=CampaignSession.SavePath;
                        realSaveBefore=File.Exists(realSave) ? File.ReadAllBytes(realSave) : null;
                        CampaignSession.EditorTestSavePath=Path.GetFullPath(Profile);
                        Check(Path.GetFullPath(CampaignSession.SavePath)==Path.GetFullPath(Profile),"all save APIs point to the isolated project-local test profile");
                        CampaignSession.NewGame();
                        InstallVirtualInput();
                        beforeContinue=game;
                        game.StartNewGame(); Begin(14); break;
                    case 14:
                        if(!SceneReady(0) || ReferenceEquals(game,beforeContinue)) break;
                        if(game.PrologueActive)
                        {
                            var opening=UnityEngine.Object.FindAnyObjectByType<CampaignPrologue>();
                            if(!sawPrologueCamera)
                            {
                                Check(opening && opening.Lens && opening.Lens.enabled && !game.eyes.enabled && !game.Running,
                                    "starting a new operation hands view to the cinematic and locks player movement");
                                sawPrologueCamera=true;
                            }
                            if(opening && opening.ImpactOccurred && !sawPrologueImpact)
                            {
                                sawPrologueImpact=true;
                                Check(true,"authored impact cue fires during the live cinematic");
                            }
                            if(opening && opening.CommanderVisible && !sawPrologueCommander)
                            {
                                if(commanderVisibleAt<0) commanderVisibleAt=EditorApplication.timeSinceStartup;
                                if(EditorApplication.timeSinceStartup-commanderVisibleAt>=.98)
                                {
                                    var cameo=opening.GetComponentInChildren<CommanderAvatar>();
                                    Check(cameo && cameo.transform.parent,"the cinematic shows the actual commander outfit on the bridge");
                                    CheckModelOnDeck(cameo.Motion,cameo.transform.parent,"Commander bracing after impact",1.1f);
                                    sawPrologueCommander=true;
                                }
                            }
                            if(opening && opening.BoardingOccurred && !sawBoarding)
                            {
                                sawBoarding=true;
                                Check(true,"the live prologue shows the assault robots entering through the breach");
                            }
                            if(sawPrologueCommander && sawBoarding) opening.RequestSkip();
                            break;
                        }
                        Check(sawPrologueCamera && sawPrologueImpact && sawPrologueCommander && sawBoarding &&
                            game.BriefingOpen && game.BriefingStartsOperation && !game.Running && game.eyes.enabled,
                            "skipping after impact restores the bridge camera and shows the new-operation briefing");
                        briefingPlayerPosition=game.player.transform.position;
                        briefingElapsed=CampaignSession.Data.seconds;
                        Capture("01_briefing"); Begin(33); break;
                    case 33:
                        if(!Elapsed(.3)) break;
                        Check(game.BriefingOpen && (game.player.transform.position-briefingPlayerPosition).sqrMagnitude<.000001f &&
                            CampaignSession.Data.seconds==briefingElapsed && !game.Radio.IsPlaying,
                            "briefing holds movement, the mission timer and the opening radio line");
                        Begin(34); break;
                    case 34:
                        if(game.BriefingOpen) break;
                        Check(game.Running && !game.BriefingStartsOperation && game.Radio.IsPlaying,
                            "Enter dismisses the opening briefing and starts live controls and Hera's radio line");
                        Begin(37); break;
                    case 37:
                        if(string.IsNullOrEmpty(game.Radio.Line)) break;
                        var openingHera=Resources.Load<AudioClip>("Doppelganger/Radio/HeraBridgeIntro");
                        Check(game.Radio.Speaker=="ГЕРА" && game.Radio.Line.StartsWith("Командир Алекс,",StringComparison.Ordinal) &&
                            openingHera && game.Radio.GetComponents<AudioSource>().Any(source=>source.isPlaying && source.clip==openingHera),
                            "the opening radio plays the packaged Hera recording and addresses Commander Alex in the subtitles");
                        Begin(35); break;
                    case 35:
                        if(!game.BriefingOpen) break;
                        Check(!game.Running && !game.BriefingStartsOperation,"F1 reopens the briefing and suspends gameplay");
                        briefingRadioProgress=game.Radio.Progress;
                        Begin(36); break;
                    case 36:
                        if(game.BriefingOpen || game.Radio.Progress<=briefingRadioProgress) break;
                        Check(game.Running,"closing the reopened briefing restores gameplay and resumes the paused transmission");
                        Begin(0); break;
                    case 0:
                        if(bridgeStep==0)
                        {
                            Check(game.Running && game.Alive==0 && game.enemySpawns.Length==0,"bridge is an exploration chapter without a central arena enemy");
                            Check(game.presentation.weapons.Count==3 && game.presentation.HasWeapon &&
                                game.presentation.weapons.Any(slot=>slot.id==FirstPersonPresentation.ServicePistolId && slot.muzzle && slot.shotSound),
                                "the campaign installs two long guns and a dedicated P-7 combat model, muzzle and sound");
                            game.presentation.RestoreAmmo(new[]{14,8},new[]{28,16},1);
                            Check(game.presentation.equippedIndex==1 && game.presentation.MagazineAmmo==8,
                                "two-weapon saves restore their original equipped weapon and ammunition");
                            game.presentation.RefillAmmo();
                            game.presentation.Equip(0);
                            var trail=Resources.Load<Material>("Doppelganger/PulseTrail");
                            Check(trail && trail.shader && trail.shader.name=="Doppelganger/Pulse Trail" &&
                                Private("beamMaterial").GetValue(game) is Material,"pulse material is loaded through an explicit build dependency and instantiated at startup");
                            Check(!game.Devices.VoiceEnabled && !game.Devices.CameraEnabled,"microphone and camera stay disabled throughout automated gameplay");
                            CheckCameraSelection();
                            CheckBridgeView();
                            CheckCombatRead();
                            Check(SpokenCommandCatalog.TryResolve("Гера, проверь связь",out string linkAction) && game.Commands.Execute(linkAction),
                                "the recognized Russian link command starts an onboard diagnostic");
                            Check(game.Radio.Speaker=="ГЕРА" && game.Radio.Line.Contains("инженерной палубы") &&
                                game.Radio.GetComponents<AudioSource>().Any(source=>source.clip && source.clip.length>2),
                                "Gera replies about the engineering deck using a loaded speech clip");
                            Check(CampaignRadio.RequiredClipNames().All(name=>Resources.Load<AudioClip>("Doppelganger/Radio/"+name)),
                                "every dialogue, status reply and voice acknowledgment has a packaged recording");
                            var applyPhrase=typeof(MultimodalCommandInput).GetMethod("ApplyPhrase",BindingFlags.Instance|BindingFlags.NonPublic);
                            applyPhrase.Invoke(game.Devices,new object[]{"пауза"});
                            Check(game.Paused && game.Radio.SuppressesVoiceInput &&
                                game.Radio.GetComponents<AudioSource>().Any(source=>source.isPlaying && source.clip && source.clip.name=="HeraCommandPause"),
                                "a recognized command gets a recorded Hera reply even while paused, and speaker audio gates recognition");
                            game.Radio.Clear();
                            game.SetPaused(false);
                            Check(game.Commands.Execute("pistol") && game.presentation.BridgeSidearmVisible,
                                "the bridge pistol action displays the sidearm");
                            Check(game.Commands.Execute("rifle") && !game.presentation.BridgeSidearmVisible &&
                                game.presentation.Equipped.id=="UnityFpsSample.Terraformer",
                                "the rifle action returns the standard automatic weapon without modifying combat slots");
                            PlaceFacingInteraction("log"); bridgeStep=1; break;
                        }
                        if(bridgeStep==1)
                        {
                            var log=Interaction("log");
                            if(string.IsNullOrEmpty(game.Prompt) || !game.Prompt.Contains(log.title)) break;
                            Check((CampaignInteractable)Private("focused").GetValue(game)==log,"actual update/focus ray selects the visible commander log");
                            interactBeginFrame=Time.frameCount; QueueInput(false,true); bridgeStep=2; break;
                        }
                        // Keep the press stable across actual player frames, not merely one
                        // EditorApplication.update callback which may have no player Update.
                        if(!CampaignSession.Has("log") || Time.frameCount-interactBeginFrame<2) break;
                        Check(dynamicInputUpdates>0 && interactEdges==1,"one virtual E down edge was processed by a real dynamic input update");
                        Check(CampaignSession.Has("log"),"one virtual E key press activates the actual focused commander log");
                        QueueInput();
                        Check(game.Cargo && game.Cargo.Interaction==Interaction(CampaignCargo.Id) &&
                            game.Cargo.GetComponent<Rigidbody>() && game.Cargo.GetComponent<Collider>() && game.Cargo.GetComponentInChildren<MeshRenderer>(),
                            "authored battery has a visible model, interaction anchor, collider and rigidbody");
                        Use("power"); Check(!CampaignSession.Has("power"),"empty socket cannot restore emergency power");
                        game.presentation.Equip(1);
                        Check(game.presentation.TryConsumeRound(),"a nondefault weapon slot and spent round are ready for the save fixture");
                        game.SetEnergy(2); game.DamagePlayer(19);
                        Use(CampaignCargo.Id);
                        Check(game.Carrying && game.Cargo.transform.parent==game.eyes.transform && !game.presentation.weaponPivot.gameObject.activeSelf,
                            "picking up battery puts the prop in view and holsters the weapon");
                        DropBattery();
                        Check(!game.Carrying && game.Cargo.transform.parent==null && game.Cargo.GetComponent<Collider>().enabled &&
                            !game.Cargo.GetComponent<Rigidbody>().isKinematic && game.presentation.weaponPivot.gameObject.activeSelf,
                            "dropping the carried battery releases a real physics object and frees the player's hands");
                        Use(CampaignCargo.Id); Check(game.Carrying,"dropped battery remains recoverable by its interaction");
                        game.player.transform.rotation=Quaternion.Euler(0,131,0);
                        Private("pitch").SetValue(game,9f); game.presentation.ApplyLook(9f); Physics.SyncTransforms();
                        game.SaveCheckpoint(false); bridgeSnapshot=CampaignSession.Data;
                        Check(CampaignSession.Error==null && bridgeSnapshot.hasCheckpoint && bridgeSnapshot.snapshot && bridgeSnapshot.cargoHeld && !bridgeSnapshot.outside,
                            "checkpoint snapshot records real cargo and interior state");
                        beforeContinue=game;
                        game.ContinueGame(); Begin(9); break;
                    case 9:
                        if(!SceneReady(0) || !game.Running || ReferenceEquals(game,beforeContinue)) break;
                        RestoreBridgeAndFly();
                        Capture("01_bridge"); Begin(1); break;
                    case 1:
                        if(!Elapsed(.35)) break;
                        Use("exit"); Check(!game.Running && CampaignSession.Data.chapter==1 && !CampaignSession.Data.hasCheckpoint,"bridge exit initiates real chapter travel and clears old spawn coordinates");
                        Begin(2); break;
                    case 2:
                        if(!SceneReady(1) || !game.Running) break;
                        Check(game.Boarding && game.OnBoardingRoute && game.Alive==2 &&
                            Vector3.Distance(game.player.transform.position,game.Boarding.Entry.position)<1,
                            "chapter travel reaches the breached lock with two cargo-deck boarders");
                        Check(game.NextObjective()==Interaction(BoardingRoute.SealId),
                            "the emergency seal is the first active target");
                        Use(BoardingRoute.TransitId);
                        Check(game.Running && game.OnBoardingRoute,"the lift cannot skip the boarding encounter");
                        Capture("02_breached_lock");
                        Check(PressureGateBlocked(out var leafHit) &&
                            leafHit.collider.GetComponentInParent<CampaignDoor>()?.unlockId==BoardingRoute.SealId,
                            "the bound A-17 leaf physically blocks the closed doorway");
                        PlaceFacingInteraction(BoardingRoute.SealId); Begin(24); break;
                    case 24:
                        if((CampaignInteractable)Private("focused").GetValue(game)!=Interaction(BoardingRoute.SealId)) break;
                        sealInteractEdges=interactEdges;
                        Begin(25); break;
                    case 25:
                        if(!CampaignSession.Has(BoardingRoute.SealId)) break;
                        Check(interactEdges==sealInteractEdges+1 && CampaignSession.Data.hasCheckpoint && CampaignSession.Error==null,
                            "one E press at the seal console restores pressure and saves the checkpoint");
                        QueueInput(); Begin(26); break;
                    case 26:
                        if(PressureGateBlocked(out _) && !Elapsed(4)) break;
                        Check(!PressureGateBlocked(out _),"the real moving leaf clears the physical doorway");
                        PlacePlayerFacing(GateApproach,Vector3.forward);
                        Begin(27); break;
                    case 27:
                        if(game.player.transform.position.z<=-5.35f && !Elapsed(4)) break;
                        Check(game.player.transform.position.z>-5.35f && game.player.isGrounded,
                            "holding W walks the real player controller through A-17 into cargo");
                        QueueInput(); beforeContinue=game;
                        game.ContinueGame(); Begin(28); break;
                    case 28:
                        if(!SceneReady(1) || !game.Running || ReferenceEquals(game,beforeContinue)) break;
                        Check(CampaignSession.Has(BoardingRoute.SealId) && game.OnBoardingRoute && !PressureGateBlocked(out _),
                            "Continue restores the already-pressurized gate open from the saved campaign flag");
                        PlacePlayerFacing(GateApproach,Vector3.forward);
                        Begin(29); break;
                    case 29:
                        if(game.player.transform.position.z<=-5.35f && !Elapsed(4)) break;
                        Check(game.player.transform.position.z>-5.35f && game.player.isGrounded,
                            "the saved gate is physically traversable after scene reload");
                        QueueInput(); Begin(20); break;
                    case 20:
                        if(!Elapsed(1.1)) break;
                        PlaceNearOpponent(game.Enemies.First(enemy=>enemy && !enemy.Dead),3,5);
                        var pistolTarget=game.Enemies.First(enemy=>enemy && !enemy.Dead);
                        var aim=(pistolTarget.transform.TransformPoint(pistolTarget.body.center)-game.eyes.transform.position).normalized;
                        float pistolPitch=-Mathf.Asin(aim.y)*Mathf.Rad2Deg;
                        Private("pitch").SetValue(game,pistolPitch); game.presentation.ApplyLook(pistolPitch);
                        Physics.SyncTransforms();
                        Check(game.presentation.Equipped.id==FirstPersonPresentation.ServicePistolId &&
                            game.presentation.MagazineAmmo==12 && !game.presentation.Equipped.automatic,
                            "a fresh boarding encounter equips the 12-round semiautomatic P-7");
                        pistolShots=CampaignSession.Data.shots; pistolHits=CampaignSession.Data.hits;
                        pistolReserve=game.presentation.ReserveAmmo;
                        Check(SpokenCommandCatalog.TryResolve("Достать пистолет",out string pistolAction) &&
                            game.Commands.Execute(pistolAction) && game.Commands.Execute("fire"),"Russian pistol and fire actions route to the combat inventory");
                        Capture("02_cargo_pistol"); Begin(21); break;
                    case 21:
                        if(CampaignSession.Data.shots==pistolShots) break;
                        Check(CampaignSession.Data.shots==pistolShots+1 && CampaignSession.Data.hits==pistolHits+1 &&
                            game.presentation.MagazineAmmo==11,"the P-7 spends a round and hits a real boarder");
                        var spentCase=GameObject.Find("Spent casing");
                        Check(spentCase && spentCase.transform.parent.name=="Ejected shell casings" &&
                            spentCase.GetComponent<MeshRenderer>().sharedMaterial.shader.isSupported &&
                            !spentCase.GetComponent<Collider>(),
                            "the real P-7 shot ejects a visible shell without adding a collider");
                        Check(game.presentation.BeginReload(),"the pistol begins a distinct timed reload");
                        Begin(22); break;
                    case 22:
                        if(game.presentation.IsReloading) break;
                        Check(game.presentation.MagazineAmmo==12 && game.presentation.ReserveAmmo==pistolReserve-1,
                            "the P-7 reload moves one cartridge from its reserve");
                        foreach(var guard in game.Enemies.ToArray()) guard.Damage(10000);
                        Check(game.Alive==0 && game.NextObjective()==Interaction(BoardingRoute.ManifestId),
                            "clearing the cargo deck makes its boarding log the next objective");
                        CheckBulletMarkOnCargoWall();
                        Use(BoardingRoute.ManifestId); Use(BoardingRoute.TransitId);
                        Check(!game.Running && CampaignSession.Has(BoardingRoute.ManifestId),
                            "the cargo log enables the in-chapter transfer");
                        Begin(23); break;
                    case 23:
                        if(!game.Running) break;
                        Check(!game.OnBoardingRoute && CampaignSession.Has(BoardingRoute.TransitId) &&
                            CampaignSession.Data.checkpoint.x<25,
                            "the working lift lands in engineering and saves the transferred stage");
                        Check(game.Alive==3 && game.Enemies.All(enemy=>enemy.GetComponentInChildren<RobotPresentation>()),"engineering loads three real animated ADAM security robots");
                        Check(game.Enemies.All(enemy=>{
                            var rig=enemy.GetComponentInChildren<RobotPresentation>();
                            return rig.IsAnimating && rig.walkClip.isHumanMotion && rig.runClip && rig.runClip.isHumanMotion &&
                                rig.fireClip && rig.fireClip.isHumanMotion && rig.runClip!=rig.walkClip;
                        }),"each active guard has a real walk cycle, separate humanoid run and a firing clip");
                        PlaceNearSecurity();
                        initialSamples=CampaignSession.Data.samples; initialUpdates=CampaignSession.Policy.Updates;
                        Begin(3); break;
                    case 3:
                        if(CampaignSession.Data.underFire==0 || CampaignSession.Data.samples-initialSamples<15 || !game.Enemies.Any(enemy=>enemy.Hits>0))
                        {
                            if(game.Dead) throw new InvalidOperationException("Player died before actual return-fire training produced examples.");
                            break;
                        }
                        Check(game.Enemies.Sum(enemy=>enemy.Shots)>0 && game.Enemies.Sum(enemy=>enemy.Hits)>0,"actual security Tick fired projectiles and hit the live player's controller");
                        Check(CampaignSession.Policy.Updates>initialUpdates && CampaignSession.Replay.Any(example=>example.State[10]>0),"actual received enemy fire appears in live demonstrations and updates model weights");
                        CampaignSession.Replay.Clear();
                        Private("sampleClock").SetValue(game,.12f);
                        initialShots=CampaignSession.Data.shots;
                        typeof(CampaignGame).GetMethod("Fire",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(game,null);
                        Check(CampaignSession.Data.shots==initialShots+1,"real Fire consumes a round between scheduled training samples");
                        Begin(4); break;
                    case 4:
                        if(!CampaignSession.Replay.Any(example=>example.Fire) || !CampaignSession.Replay.Any(example=>example.Crouch)) break;
                        Check(CampaignSession.Replay.Any(example=>example.Fire),"a shot between 5 Hz sample ticks survives into the next fire label");
                        Check(CampaignSession.Replay.Any(example=>example.Crouch),"virtual C input becomes a real crouched player demonstration");
                        Capture("02_engineering"); CampaignSession.Replay.Clear(); Begin(5); break;
                    case 5:
                        // Release C before collecting standing idle examples.
                        if(!CampaignSession.Replay.Any(example=>example.Move==0 && !example.Fire && !example.Crouch)) break;
                        Check(CampaignSession.Replay.Any(example=>example.Move==0 && !example.Fire && !example.Crouch),"standing pauses and no-fire actions remain learnable combat examples");
                        foreach(var enemy in game.Enemies.ToArray()) enemy.Damage(10000);
                        Check(game.Alive==0,"security deaths update actual encounter completion");
                        var finalCorpse=game.Enemies.Last().GetComponent<CampaignInteractable>();
                        var registered=(CampaignInteractable[])Private("interactions").GetValue(game);
                        Check(finalCorpse && registered.Contains(finalCorpse),"last corpse pickup is registered immediately after the final kill");
                        game.DamagePlayer(Mathf.Min(10,game.Health*.25f));
                        float previousHealth=game.Health;
                        game.presentation.TryConsumeRound(); game.Interact(finalCorpse);
                        Check(CampaignSession.Has(finalCorpse.id) && game.Health>previousHealth && game.presentation.MagazineAmmo==game.presentation.MagazineCapacity,
                            "actual corpse interaction restores health/ammunition and consumes pickup once");
                        Use("relayA"); Use("relayB"); Check(CampaignSession.Has("relayA") && CampaignSession.Has("relayB"),"both authored engineering relays persist their objective flags");
                        frozenPolicy=PolicyJson(); coreUpdates=CampaignSession.Policy.Updates;
                        Use("exit"); Begin(6); break;
                    case 6:
                        if(!SceneReady(2) || !game.Running) break;
                        Check(game.Alive==1 && game.Boss && game.Boss.GetComponentInChildren<RobotPresentation>(),"real SceneManager travel loads the ADAM Doppelganger encounter");
                        Check(game.Boss.GetComponentInChildren<CommanderAvatar>() && game.player.GetComponentInChildren<CommanderAvatar>(),
                            "ADAM and the player use the shared full-body commander outfit");
                        Check(CampaignSession.Data.samples>=15,"demonstrations survive travel into the final chapter");
                        beforeBossHealth=game.Boss.health; beforeBossShield=game.Boss.shield;
                        game.Boss.Damage(10000);
                        Check(!game.Boss.Dead && game.Boss.health==beforeBossHealth && game.Boss.shield==beforeBossShield,"protected Doppelganger cannot be killed before coolant objectives");
                        Capture("03_core"); Begin(7); break;
                    case 7:
                        if(!Elapsed(.65)) break;
                        Use("coolantA"); Use("coolantB"); Check(game.CoreExposed,"both coolant terminals expose the final enemy");
                        PlaceNearOpponent(game.Boss,8,10); game.SetEnergy(1);
                        initialAutonomousShots=game.Boss.AutonomousShots;
                        nextBossFixturePlacement=EditorApplication.timeSinceStartup+1.5;
                        Begin(15); break;
                    case 15:
                        if(game.Boss.Phase==CampaignEnemy.BossPhase.Recovery && !sawStandingRecovery &&
                            Vector3.Distance(game.player.transform.position,game.Boss.transform.position)>7)
                        {
                            Check(!game.Boss.Crouched,"ADAM stays upright while recovering between attacks");
                            sawStandingRecovery=true;
                        }
                        if(game.Boss.Phase==CampaignEnemy.BossPhase.Aim && !sawAutonomousWarning)
                        {
                            Check(game.Boss.IsWarning,"ADAM telegraphs its independently aimed attack");
                            sawAutonomousWarning=true;
                        }
                        if(game.Boss.IsMirroring)
                        {
                            Check(sawStandingRecovery && sawAutonomousWarning && game.Boss.AutonomousShots>initialAutonomousShots,
                                "ADAM attacks autonomously before selecting the timed reflection ability");
                            Begin(16); break;
                        }
                        if(!game.Boss.VisibleTo(game.eyes.transform.position) && EditorApplication.timeSinceStartup>=nextBossFixturePlacement)
                        {
                            PlaceNearOpponent(game.Boss,6,10);
                            nextBossFixturePlacement=EditorApplication.timeSinceStartup+1.5;
                        }
                        break;
                    case 16:
                        PlaceNearOpponent(game.Boss,6,10,true); game.SetEnergy(0);
                        game.presentation.Equip(0); game.presentation.RefillAmmo();
                        initialBossShots=game.Boss.Shots; initialBossCrouches=game.Boss.Crouches; initialBossHits=game.Boss.Hits;
                        bossStart=game.Boss.transform.position; healthBeforeEcho=game.Health; sourceShotTime=game.Mirror.Now;
                        typeof(CampaignGame).GetMethod("Fire",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(game,null);
                        Check(game.Mirror.PeekShot(out var original) && original.Target==game.player.transform.TransformPoint(game.player.center),
                            "accepted player fire records the exact world position occupied at the shot");
                        Check(game.Commands.Execute(mirrorMove),"strafe uses the real player movement command path");
                        Begin(10); break;
                    case 10:
                        if(!sawBossWarning && game.Boss.MirrorShotPending)
                        {
                            Check(game.Boss.Shots==initialBossShots && game.Boss.MirrorShotProgress<1,
                                "the delayed shot has a warning before it can fire");
                            sawBossWarning=true; Capture("03_adam_warning");
                        }
                        if(game.Mirror.Now-sourceShotTime<PlayerMirrorTimeline.Delay)
                        {
                            if(game.Boss.Shots!=initialBossShots || game.Boss.Crouches!=initialBossCrouches)
                                throw new InvalidOperationException("An action escaped the mirror's half-second buffer.");
                            break;
                        }
                        if(game.Boss.Crouches<=initialBossCrouches || Vector3.Dot(game.Boss.transform.position-bossStart,mirrorStep)<.28f) break;
                        Check(sawBossWarning && game.Boss.Shots==initialBossShots+1 && game.Boss.Crouched,
                            "ADAM repeats the actual shot once, then mirrors movement and crouch in the scene");
                        Check(game.Boss.Hits==initialBossHits && Mathf.Approximately(game.Health,healthBeforeEcho),
                            "player sidestepping the recorded position dodges the delayed shot");
                        game.Commands.StopMotion();
                        bossCrouchAt=game.Mirror.Now; Begin(31); break;
                    case 31:
                        if(game.Mirror.Now-bossCrouchAt<.25) break;
                        Check(game.Boss.Crouched && game.Boss.IsMirroring,"ADAM settles into its deliberate mirrored crouch");
                        CheckModelOnDeck(game.Boss.GetComponentInChildren<CommanderAvatar>().Motion,game.Boss.transform,"Crouched ADAM",1.0f);
                        Begin(32); break;
                    case 32:
                        var bossMotion=game.Boss.GetComponentInChildren<CommanderAvatar>().Motion;
                        if((game.Boss.Crouched || bossMotion.CrouchAmount>.12f) && !Elapsed(3)) break;
                        Check(!game.Boss.Crouched && bossMotion.CrouchAmount<=.12f,"ADAM returns upright after the player releases crouch");
                        CheckModelOnDeck(bossMotion,game.Boss.transform,"Upright ADAM",1.35f);
                        Check(CampaignSession.Policy.Updates==coreUpdates && PolicyJson()==frozenPolicy,
                            "the mirror encounter leaves prior engineering telemetry and saved model weights intact");
                        game.Boss.Damage(10000); Check(game.Boss.Dead && game.Alive==0,"exposed Doppelganger takes damage and completes the boss encounter");
                        Use("eva"); Check(game.Running && !game.Outside,"external airlock remains locked until core shutdown");
                        Use("core"); Check(CampaignSession.Has("core") && !game.Victory && !CampaignSession.Data.finished,
                            "core shutdown unlocks the EVA objective instead of immediately winning");
                        Check(game.NextObjective()==Interaction("eva"),"the world objective now points to the real airlock anchor");
                        Use("eva"); Check(!game.Running && !game.Outside,"airlock starts the actual transition before placing the player outside");
                        Begin(11); break;
                    case 11:
                        if(!game.Running || !game.Outside) break;
                        var outside=(GameObject)Private("exterior").GetValue(game);
                        var dish=(Transform)Private("dishPivot").GetValue(game);
                        Check(outside && outside.activeInHierarchy && dish && dish.IsChildOf(outside.transform) &&
                            Interaction("antennaAlign").transform.IsChildOf(outside.transform) && Interaction("antennaSend").transform.IsChildOf(outside.transform) &&
                            Interaction("evaReturn").transform.IsChildOf(outside.transform),
                            "EVA activates the authored exterior, dish, two physical terminals and return anchor");
                        Check(Vector3.Distance(game.player.transform.position,outside.transform.position)<10 &&
                            Physics.Raycast(game.player.transform.position+Vector3.up*.15f,Vector3.down,.65f,~0,QueryTriggerInteraction.Ignore) &&
                            !game.presentation.weaponPivot.gameObject.activeSelf && CampaignSession.Data.snapshot && CampaignSession.Data.outside,
                            "airlock puts the player on a solid hull deck, holsters weapons and saves exterior state");
                        Capture("04_eva");
                        Use("antennaSend"); Check(!game.AwaitingEarth && !game.Victory && !CampaignSession.Has("antennaSend"),
                            "SOS is blocked until the exterior antenna is aligned");
                        antennaRest=dish.localRotation; Use("antennaAlign");
                        Check(CampaignSession.Has("antennaAlign") && game.NextObjective()==Interaction("antennaSend"),
                            "real alignment interaction advances the exterior objective to the transmitter");
                        Begin(12); break;
                    case 12:
                        if(Quaternion.Angle(antennaRest,((Transform)Private("dishPivot").GetValue(game)).localRotation)<3) break;
                        Check(game.Oxygen<100 && game.Oxygen>90,"antenna animates and oxygen is consumed during the live exterior scene");
                        Use("antennaSend");
                        Check(game.AwaitingEarth && !game.Victory && !CampaignSession.Has("antennaSend") && game.Radio.IsPlaying,
                            "sending SOS queues radio and waits for Earth before awarding victory");
                        Begin(13); break;
                    case 13:
                        if(!game.Victory)
                        {
                            if(game.Radio.Speaker=="ЗЕМЛЯ" && !sawEarthReply)
                            {
                                var reply=Resources.Load<AudioClip>("Doppelganger/Radio/EarthReply");
                                Check(reply && reply.length>1 && game.GetComponents<AudioSource>().Any(source=>source.clip==reply) &&
                                    game.AwaitingEarth && game.Radio.Line.Contains("Земля на связи"),
                                    "Earth transmission actually assigns the packaged spoken reply, with victory still gated");
                                sawEarthReply=true; Capture("04_earth_reply"); break;
                            }
                            if(!string.IsNullOrEmpty(game.Radio.Speaker)) game.Radio.Skip();
                            break;
                        }
                        Check(sawEarthReply && CampaignSession.Data.finished && CampaignSession.Has("antennaSend") && !game.AwaitingEarth,
                            "only resolving the actual final radio queue completes the campaign");
                        Check(CampaignSession.Load(2) && CampaignSession.Data.finished && CampaignSession.Data.snapshot && CampaignSession.Data.outside &&
                            CampaignSession.Has("core") && CampaignSession.Has("antennaAlign") && CampaignSession.Has("antennaSend") && PolicyJson()==frozenPolicy,
                            "completed EVA and unchanged learned model round-trip through isolated save");
                        Check(!game.Devices.VoiceEnabled && !game.Devices.CameraEnabled,"campaign check did not activate either physical input device");
                        Capture("04_victory"); Begin(8); break;
                    case 8:
                        if(!Elapsed(.5)) break;
                        Finish(true,"The grounded commander poses, physical A-17 gate on a new and resumed run, guard combat, ADAM reflection, campaign checkpoints and EVA passed."); break;
                }
            }
            catch(Exception exception) { Finish(false,exception.ToString()); }
        }

        static void Finish(bool passed,string detail)
        {
            if(completed) return;
            completed=true;
            if(!string.IsNullOrEmpty(realSave))
            {
                byte[] after=File.Exists(realSave) ? File.ReadAllBytes(realSave) : null;
                bool untouched=realSaveBefore==null ? after==null : after!=null && realSaveBefore.SequenceEqual(after);
                File.AppendAllText(Report,(untouched ? "PASS: " : "FAIL: ")+"real player save remains byte-for-byte untouched\n");
                if(!untouched) { passed=false; detail+="\nReal player save changed unexpectedly."; }
            }
            EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
            try
            {
                string previousSettings=SessionState.GetString(InputJson,"");
                RemoveVirtualInput(); RestoreEnvironment();
                bool restored=string.IsNullOrEmpty(previousSettings) || EditorJsonUtility.ToJson(InputSystem.settings)==previousSettings;
                File.AppendAllText(Report,(restored ? "PASS: " : "FAIL: ")+"complete original input configuration restored after virtual-device check\n");
                if(!restored) { passed=false; detail+="\nOriginal input configuration differs after cleanup."; }
            }
            catch(Exception exception) { passed=false; detail+="\nVirtual input cleanup failed: "+exception; }
            File.AppendAllText(Report,(passed ? "RESULT: PASSED\n" : "RESULT: FAILED\n")+detail+"\n");
            if(EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        }
        static void ModeChanged(PlayModeStateChange change)
        {
            if(change!=PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key,false)) return;
            SessionState.EraseBool(Key); EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
            CampaignSession.EditorTestSavePath=null; RemoveVirtualInput(); RestoreEnvironment();
            string previous=SessionState.GetString(Previous,""); SessionState.EraseString(Previous);
            EditorApplication.delayCall+=()=>{ if(!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous,OpenSceneMode.Single); };
        }
    }
}
