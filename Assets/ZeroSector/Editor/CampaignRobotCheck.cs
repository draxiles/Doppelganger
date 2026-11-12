using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // Real ADAM skin and bones, over successive Play Mode frames. No campaign,
    // profile, learned model or physical input component is created by this fixture.
    [InitializeOnLoad]
    public static class CampaignRobotCheck
    {
        const string BatchRun="Doppelganger.CombatMotion.BatchRun";
        const string PreviousCaptureRate="Doppelganger.CombatMotion.CaptureRate";
        const string Key="Doppelganger.RobotCheck", Original=Key+".Scene", Background=Key+".Background", Scale=Key+".TimeScale", Finished=Key+".Finished";
        const string RestorePending=Key+".RestorePending", Passed=Key+".Passed", Detail=Key+".Detail", GlobalsSaved=Key+".GlobalsSaved";
        const string Report="Diagnostics/CampaignPreviews/robot-animation-check.txt", Folder="Diagnostics/CampaignPreviews", Root="Isolated ADAM animation regression";
        static RobotPresentation view;
        static CharacterController controller;
        static Camera camera;
        static Transform head;
        static SkinnedMeshRenderer[] skins;
        static Mesh[] bakedMeshes;
        static List<Vector3>[] vertexBuffers;
        static int soleFrame=-1;
        static float leftSoleY,rightSoleY;
        static Transform[] bones;
        static PoseSample first,idlePose,standingPose,firePose;
        static Vector3 modelLocalPosition,modelLocalScale,actualVelocity,desiredVelocity;
        static Vector3 aimTarget,cannonAtRest;
        static Quaternion modelLocalRotation;
        static bool crouch,fire,damage,dead,sprint,completed;
        static int phase=-1,direction;
        static float phaseBegan,lastTime,earlyCrouchHead;
        static float minFoot,maxFoot,minHead,maxHead,minHips,maxHips;
        static double deadline;
        static readonly Vector3[] Directions={Vector3.forward,Vector3.back,Vector3.left,Vector3.right};
        static readonly string[] DirectionNames={"forward","backward","strafe-left","strafe-right"};
        sealed class PoseSample
        {
            public Quaternion[] rotations;
            public Vector3[] vertices;
            public Vector3 leftFoot,rightFoot,hips;
            public float head,rootY,leftFootWorldY,rightFootWorldY,leftSoleWorldY,rightSoleWorldY;
        }
        static CampaignRobotCheck()
        {
            EditorApplication.playModeStateChanged+=Mode;
            if(SessionState.GetBool(Key,false) && !SessionState.GetBool(Finished,false)) Attach();
            if(SessionState.GetBool(RestorePending,false)) QueueSceneRestore();
        }

        public static void RunBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0],OpenSceneMode.Single);
                SessionState.SetBool(BatchRun,true);
                SessionState.SetInt(PreviousCaptureRate,Time.captureFramerate);
                Start();
            }
            catch(Exception error)
            {
                SessionState.EraseBool(BatchRun);
                RestoreCaptureRate();
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static void RestoreCaptureRate()
        {
            Time.captureFramerate=SessionState.GetInt(PreviousCaptureRate,0);
            SessionState.EraseInt(PreviousCaptureRate);
        }

        public static void Start()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and stop Play Mode before checking ADAM.");
            if(SessionState.GetBool(RestorePending,false)) throw new InvalidOperationException("Wait for the previous ADAM check to restore its scene.");
            string previous=SceneManager.GetActiveScene().path;
            if(string.IsNullOrEmpty(previous)) throw new InvalidOperationException("Open a saved scene before checking ADAM.");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AdamRobotAuthoring.PrefabPath);
            if(!prefab) throw new InvalidOperationException("Generate the real ADAM robot prefab first.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Report,"ADAM animation regression: real humanoid bones and baked skins over time.\nIsolated temporary scene; no campaign/profile/model or microphone/webcam touched.\n");
            SessionState.SetString(Original,previous); SessionState.SetBool(Background,Application.runInBackground); SessionState.SetFloat(Scale,Time.timeScale); SessionState.SetBool(GlobalsSaved,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                var actor=(GameObject)PrefabUtility.InstantiatePrefab(prefab); actor.name=Root;
                actor.transform.SetPositionAndRotation(new Vector3(0,.02f,0),Quaternion.identity);
                var body=actor.AddComponent<CharacterController>(); body.height=1.85f; body.radius=.36f; body.center=Vector3.up*.925f; body.stepOffset=.25f;
                body.skinWidth=.02f; body.minMoveDistance=0;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name="Test floor collision";
                floor.transform.position=Vector3.down*.1f; floor.transform.localScale=new Vector3(50,.2f,50);
                var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Temporary ADAM test floor"};
                floorMaterial.SetColor("_BaseColor",new Color(.115f,.15f,.19f)); floorMaterial.SetFloat("_Smoothness",.15f);
                floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
                var lens=new GameObject("ADAM test camera",typeof(Camera)).GetComponent<Camera>();
                lens.clearFlags=CameraClearFlags.SolidColor; lens.backgroundColor=new Color(.023f,.035f,.065f); lens.fieldOfView=43; lens.nearClipPlane=.03f;
                lens.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                AddLight("Warm front key",new Vector3(25,-145,0),new Color(1,.88f,.75f),2.2f);
                AddLight("Cool fill",new Vector3(40,30,0),new Color(.4f,.64f,1),1.5f);
                RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.23f,.28f,.35f);
                SessionState.SetBool(Finished,false); SessionState.SetBool(Key,true); Application.runInBackground=true; Attach(); EditorApplication.EnterPlaymode();
            }
            catch { SessionState.EraseBool(Key); Restore(); EditorSceneManager.OpenScene(previous,OpenSceneMode.Single); throw; }
        }
        static void AddLight(string name,Vector3 rotation,Color color,float intensity)
        {
            var light=new GameObject(name,typeof(Light)).GetComponent<Light>(); light.type=LightType.Directional;
            light.transform.eulerAngles=rotation; light.color=color; light.intensity=intensity; light.shadows=LightShadows.None;
        }
        static void Attach()
        {
            phase=-1; direction=0; completed=false; soleFrame=-1; deadline=EditorApplication.timeSinceStartup+120;
            desiredVelocity=actualVelocity=Vector3.zero; crouch=fire=damage=dead=sprint=false;
            EditorApplication.update-=Tick; EditorApplication.update+=Tick; Application.logMessageReceived-=Log; Application.logMessageReceived+=Log;
        }
        static void Log(string message,string stack,LogType type)
        {
            if(type==LogType.Exception || type==LogType.Error || type==LogType.Assert) Finish(false,"Runtime error: "+message+"\n"+stack);
        }
        static void Check(bool condition,string label)
        {
            File.AppendAllText(Report,(condition ? "PASS: " : "FAIL: ")+label+"\n");
            if(!condition) throw new InvalidOperationException(label);
        }
        static void Begin(int next) { phase=next; phaseBegan=Time.time; EditorApplication.QueuePlayerLoopUpdate(); }
        static bool Elapsed(float seconds) => Time.time-phaseBegan>=seconds;
        static float RotationDelta(PoseSample a,PoseSample b)
        {
            float result=0; for(int i=0;i<a.rotations.Length;i++) result=Mathf.Max(result,Quaternion.Angle(a.rotations[i],b.rotations[i])); return result;
        }
        static float MeshDelta(PoseSample a,PoseSample b)
        {
            if(a.vertices.Length!=b.vertices.Length) throw new InvalidOperationException("Skinned vertex count changed during animation.");
            float result=0; for(int i=0;i<a.vertices.Length;i++) result=Mathf.Max(result,Vector3.Distance(a.vertices[i],b.vertices[i])); return result;
        }
        static PoseSample Sample()
        {
            var sample=SampleAnchors(); sample.rotations=bones.Select(b=>b.localRotation).ToArray();
            var vertices=new List<Vector3>();
            foreach(var skin in skins)
            {
                var mesh=new Mesh();
                try
                {
                    skin.BakeMesh(mesh);
                    foreach(var vertex in mesh.vertices) vertices.Add(controller.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex)));
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            sample.vertices=vertices.ToArray(); return sample;
        }
        static PoseSample SampleAnchors()
        {
            FindPhysicalSoles();
            var root=controller.transform;
            return new PoseSample {head=root.InverseTransformPoint(head.position).y,hips=root.InverseTransformPoint(view.hips.position),rootY=root.position.y,
                leftFoot=root.InverseTransformPoint(view.leftFoot.position),rightFoot=root.InverseTransformPoint(view.rightFoot.position),
                leftFootWorldY=view.leftFoot.position.y,rightFootWorldY=view.rightFoot.position.y,leftSoleWorldY=leftSoleY,rightSoleWorldY=rightSoleY};
        }
        static void FindPhysicalSoles()
        {
            if(soleFrame==Time.frameCount) return;
            soleFrame=Time.frameCount; leftSoleY=rightSoleY=float.PositiveInfinity;
            Vector3 left=view.leftFoot.position,right=view.rightFoot.position;
            for(int i=0;i<skins.Length;i++)
            {
                var skin=skins[i]; if(!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                skin.BakeMesh(bakedMeshes[i]); bakedMeshes[i].GetVertices(vertexBuffers[i]);
                foreach(var vertex in vertexBuffers[i])
                {
                    Vector3 point=skin.transform.TransformPoint(vertex);
                    float l=new Vector2(point.x-left.x,point.z-left.z).sqrMagnitude;
                    float r=new Vector2(point.x-right.x,point.z-right.z).sqrMagnitude;
                    // Select the bottom of the actual foot skin near each ankle,
                    // assigning overlapping regions to the nearest foot. Imported
                    // meshes need not be CPU-readable and no importer is changed.
                    if(l<.28f*.28f && l<=r+.0004f && point.y>=left.y-.32f && point.y<=left.y+.04f) leftSoleY=Mathf.Min(leftSoleY,point.y);
                    if(r<.28f*.28f && r<=l+.0004f && point.y>=right.y-.32f && point.y<=right.y+.04f) rightSoleY=Mathf.Min(rightSoleY,point.y);
                }
            }
        }
        static void WriteAnchors(string name,PoseSample sample)
        {
            File.AppendAllText(Report,$"ANCHORS {name}: root worldY={sample.rootY:F3}; actor-local head={sample.head:F3}, hips={sample.hips.y:F3}, ankles L={sample.leftFoot.y:F3} R={sample.rightFoot.y:F3}; world ankles L={sample.leftFootWorldY:F3} R={sample.rightFootWorldY:F3}; actual shoe soles above floor L={sample.leftSoleWorldY:F3} R={sample.rightSoleWorldY:F3}m.\n");
        }
        static void ValidateAnchors(string label,PoseSample sample,bool walking,bool crouched)
        {
            // The flat fixture floor is exactly world Y=0. Ankle centers sit inside
            // the boot and must not be mistaken for the actual shoe sole surface.
            float top=walking ? .28f : .08f;
            bool feet=sample.leftSoleWorldY>=-.05f && sample.rightSoleWorldY>=-.05f && sample.leftSoleWorldY<=top && sample.rightSoleWorldY<=top;
            // A swinging ankle may be above the other sole cluster during crossing steps.
            float ankleClearance=walking ? .55f : .30f;
            feet &= sample.leftFootWorldY-sample.leftSoleWorldY<ankleClearance && sample.rightFootWorldY-sample.rightSoleWorldY<ankleClearance;
            if(walking) feet &= Mathf.Min(sample.leftSoleWorldY,sample.rightSoleWorldY)<=.08f;
            bool torso=sample.head>=(crouched ? .65f : 1.25f) && sample.head<1.95f && sample.hips.y>=(crouched ? .12f : .4f) && sample.hips.y<1.3f;
            if(!feet || !torso) WriteAnchors("INVALID "+label,sample);
            if(!feet) throw new InvalidOperationException(label+": ADAM feet are buried, floating, or both lose contact with the flat floor.");
            if(!torso) throw new InvalidOperationException(label+": head/hips are not anchored at an anatomical height above the actor collision root.");
        }
        static void ResetRange()
        { minFoot=minHead=minHips=float.PositiveInfinity; maxFoot=maxHead=maxHips=float.NegativeInfinity; }
        static void TrackGrounding(string label,bool isCrouched)
        {
            var sample=SampleAnchors(); ValidateAnchors(label,sample,true,isCrouched);
            minFoot=Mathf.Min(minFoot,sample.leftSoleWorldY,sample.rightSoleWorldY); maxFoot=Mathf.Max(maxFoot,sample.leftSoleWorldY,sample.rightSoleWorldY);
            minHead=Mathf.Min(minHead,sample.head); maxHead=Mathf.Max(maxHead,sample.head); minHips=Mathf.Min(minHips,sample.hips.y); maxHips=Mathf.Max(maxHips,sample.hips.y);
        }
        static void WriteRange(string label)
        { File.AppendAllText(Report,$"GROUND RANGE {label}: actual shoe soles above floor [{minFoot:F3}, {maxFoot:F3}]m; actor-local head [{minHead:F3}, {maxHead:F3}], hips [{minHips:F3}, {maxHips:F3}].\n"); }
        static void Capture(string name)
        {
            if(SessionState.GetBool(BatchRun,false) && name!="idle" && name!="forward-b" && name!="run-a" &&
                name!="run-b" && name!="sprint" && name!="aim" && name!="fire" && !name.StartsWith("failed-",StringComparison.Ordinal)) return;
            var root=controller.transform; camera.transform.position=root.position+new Vector3(2.7f,1.35f,4.3f); camera.transform.LookAt(root.position+Vector3.up*.95f);
            string path=Folder+"/adam-"+name+".png"; var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
            RenderTexture previous=RenderTexture.active; Texture2D texture=null;
            try
            {
                target.Create(); RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target}); RenderTexture.active=target;
                texture=new Texture2D(1280,720,TextureFormat.RGB24,false); texture.ReadPixels(new Rect(0,0,1280,720),0,0); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG());
                if(new FileInfo(path).Length<10000) throw new InvalidOperationException("ADAM preview image appears empty: "+path);
            }
            finally { RenderTexture.active=previous; if(texture) UnityEngine.Object.DestroyImmediate(texture); target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
        static void Drive(float dt)
        {
            // Input is measured after collision exactly as in the live enemy controller.
            actualVelocity=Vector3.MoveTowards(actualVelocity,desiredVelocity,dt*7);
            var before=controller.transform.position; controller.Move((actualVelocity+Vector3.down*3)*dt);
            view.Tick((controller.transform.position-before)/Mathf.Max(.001f,dt),crouch,fire,damage,dead,sprint);
            fire=false;
            if(dead) return;
            if(Vector3.Distance(view.model.localPosition,modelLocalPosition)>.025f) throw new InvalidOperationException("Animation moved the visible model away from its collision root.");
            if(Vector3.Distance(view.model.localScale,modelLocalScale)>.0001f) throw new InvalidOperationException("Animation/crouch squashed the entire ADAM model.");
        }

        static void ProbeController()
        {
            Vector3 origin=controller.transform.position;
            foreach(float step in new[]{1f/60,.05f})
            {
                foreach(bool split in new[]{false,true})
                {
                    Reset();
                    Vector3 forward=Vector3.forward*(3.65f*step), gravity=Vector3.down*(3*step);
                    float min=float.PositiveInfinity,max=0,total=0;
                    for(int i=0;i<36;i++)
                    {
                        Vector3 before=controller.transform.position;
                        if(split) { controller.Move(forward); controller.Move(gravity); }
                        else controller.Move(forward+gravity);
                        float speed=(controller.transform.position.z-before.z)/step;
                        min=Mathf.Min(min,speed); max=Mathf.Max(max,speed); total+=speed;
                    }
                    File.AppendAllText(Report,$"CONTROLLER PROBE: split={split}, step={step:F3}, desired=3.65, actual min/mean/max={min:F2}/{total/36:F2}/{max:F2} m/s.\n");
                }
            }
            Reset();
            void Reset()
            {
                controller.enabled=false; controller.transform.position=origin; controller.enabled=true;
                Physics.SyncTransforms();
            }
        }
        static void Tick()
        {
            if(completed || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if(EditorApplication.timeSinceStartup>deadline) { Finish(false,"ADAM check exceeded its 120-second wall clock timeout."); return; }
            if(!EditorApplication.isPlaying) return;
            try
            {
                if(EditorApplication.isPaused) throw new InvalidOperationException("Play Mode was paused during ADAM check.");
                if(phase==-1)
                {
                    var actor=GameObject.Find(Root); if(!actor) throw new InvalidOperationException("The isolated scene did not enter Play Mode.");
                    view=actor.GetComponent<RobotPresentation>(); controller=actor.GetComponent<CharacterController>(); camera=UnityEngine.Object.FindAnyObjectByType<Camera>();
                    Check(view && view.animator && view.animator.avatar && view.animator.isHuman && view.walkClip && view.idleClip,"real ADAM avatar and separate imported walk/idle clips are connected");
                    Check(view.runClip && view.runClip.isHumanMotion && view.runClip!=view.walkClip && view.sprintClip && view.sprintClip.isHumanMotion &&
                        view.fireClip && view.fireClip.isHumanMotion && !view.fireClip.isLooping,"running, sprinting and one-shot attack use distinct imported full-body clips");
                    Check(view.leftFoot && view.rightFoot && view.rightHand && view.hips,"ADAM feet, hips and firing hand are connected");
                    Check(view.guardCannon && view.guardCannon.parent==view.rightHand && view.muzzle.IsChildOf(view.guardCannon) &&
                        view.guardCannon.GetComponentInChildren<SkinnedMeshRenderer>(),"visible textured cannon and physical muzzle follow the actual wrist");
                    head=view.animator.GetBoneTransform(HumanBodyBones.Head); Check(head,"humanoid head available for stance checks");
                    skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.enabled && s.gameObject.activeInHierarchy).ToArray();
                    Check(skins.Length>0,"real visible ADAM skinned meshes are active");
                    Check(skins.Where(s=>!s.transform.IsChildOf(view.guardCannon)).SelectMany(s=>s.sharedMaterials).Distinct().Count(m=>
                        m && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") && m.GetTexture("_BumpMap"))>=5,
                        "character skin uses the pack's detailed surface and normal maps");
                    bakedMeshes=skins.Select(s=>new Mesh{name="ADAM test skin bake"}).ToArray();
                    vertexBuffers=skins.Select(s=>new List<Vector3>()).ToArray();
                    bones=new[]{view.leftUpperLeg,view.leftLowerLeg,view.leftFoot,view.rightUpperLeg,view.rightLowerLeg,view.rightFoot,view.hips,view.rightHand,head}; Check(bones.All(b=>b),"all sampled bones exist");
                    if(SessionState.GetBool(BatchRun,false))
                    {
                        // The editor resets capture timing on entering Play Mode.
                        // Set it here so the fixture matches the campaign's 60 Hz cadence.
                        Time.captureFramerate=60;
                        ProbeController();
                    }
                    modelLocalPosition=view.model.localPosition; modelLocalScale=view.model.localScale; modelLocalRotation=view.model.localRotation; Time.timeScale=1; lastTime=Time.time; Begin(0);
                }
                float dt=Mathf.Clamp(Time.time-lastTime,0,.08f); lastTime=Time.time;
                if(dt<=0) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                Drive(dt);
                if(phase==2 || phase==3) TrackGrounding(DirectionNames[direction],false);
                if(phase==7 || phase==8) TrackGrounding("crouch-walk",true);
                switch(phase)
                {
                    case 0:
                        if(!Elapsed(.8f)) break; Check(view.IsAnimating,"real humanoid animation graph is evaluating"); first=Sample();
                        Capture("idle"); WriteAnchors("idle",first); ValidateAnchors("planted idle",first,false,false); Check(true,"actual idle shoe soles and torso stand at the visible floor, independent of other animation poses");
                        Begin(1); break;
                    case 1:
                        if(!Elapsed(.32f)) break; idlePose=Sample();
                        File.AppendAllText(Report,"Idle bone change "+RotationDelta(first,idlePose).ToString("F4")+"deg; mesh change "+MeshDelta(first,idlePose).ToString("F5")+"m.\n");
                        Check(RotationDelta(first,idlePose)>.015f && MeshDelta(first,idlePose)>.00001f,"idle remains subtly animated instead of a frozen walk frame");
                        desiredVelocity=Directions[direction]*1.55f; ResetRange(); Begin(2); break;
                    case 2:
                        // Include at least one complete source cycle after acceleration,
                        // rather than accepting only two fortunate grounded frames.
                        float walkDuration=SessionState.GetBool(BatchRun,false) ? 1.25f : .65f+view.walkClip.length;
                        if(!Elapsed(walkDuration)) break; Check(view.LocomotionWeight>.6f && view.PlanarSpeed>1,"measured actor speed drives the locomotion blend"); first=Sample();
                        WriteAnchors(DirectionNames[direction]+"-a",first); Capture(DirectionNames[direction]+"-a"); Begin(3); break;
                    case 3:
                        if(!Elapsed(.26f)) break; var walking=Sample();
                        File.AppendAllText(Report,DirectionNames[direction]+" bone change "+RotationDelta(first,walking).ToString("F3")+"deg; mesh change "+MeshDelta(first,walking).ToString("F4")+"m.\n");
                        Check(RotationDelta(first,walking)>1 && MeshDelta(first,walking)>.003f,"real skeleton and skin animate during "+DirectionNames[direction]); WriteAnchors(DirectionNames[direction]+"-b",walking); WriteRange(DirectionNames[direction]);
                        Check(true,"feet stay above the floor and one foot stays planted during sampled "+DirectionNames[direction]+" locomotion"); Capture(DirectionNames[direction]+"-b");
                        if(++direction<Directions.Length && !SessionState.GetBool(BatchRun,false))
                        { desiredVelocity=Directions[direction]*1.55f; ResetRange(); Begin(2); }
                        else { desiredVelocity=Vector3.forward*3.65f; Begin(16); }
                        break;
                    case 16:
                        bool runningSettled=view.RunAmount>.85f && view.PlanarSpeed>3.2f;
                        if(!runningSettled && !Elapsed(1.8f)) break;
                        Check(runningSettled,$"post-collision travel activates the run layer at combat speed (weight={view.RunAmount:F2}, speed={view.PlanarSpeed:F2}, request={desiredVelocity.magnitude:F2}, pre-collision={actualVelocity.magnitude:F2}, dt={dt:F3}, flags={controller.collisionFlags})");
                        first=Sample(); Capture("run-a"); Begin(17); break;
                    case 17:
                        if(!Elapsed(.19f)) break;
                        var running=Sample();
                        Check(Quaternion.Angle(first.rotations[0],running.rotations[0])>2 && MeshDelta(first,running)>.012f,
                            "the actual thigh and skinned surface move over the running cycle");
                        Capture("run-b"); sprint=true; desiredVelocity=Vector3.forward*5.5f; Begin(18); break;
                    case 18:
                        bool sprintSettled=view.SprintAmount>.8f && view.PlanarSpeed>4.5f;
                        if(!sprintSettled && !Elapsed(1.8f)) break;
                        Check(sprintSettled,$"a sprint input activates the separate sprint cycle (weight={view.SprintAmount:F2}, speed={view.PlanarSpeed:F2} m/s)");
                        Capture("sprint"); sprint=false; desiredVelocity=Vector3.zero; Begin(4); break;
                    case 4:
                        if(!Elapsed(.8f) || view.LocomotionWeight>=.08f && !Elapsed(1.4f)) break;
                        standingPose=Sample(); Check(view.LocomotionWeight<.08f,"stopping blends back from locomotion to idle");
                        WriteAnchors("standing after stop",standingPose); ValidateAnchors("standing after stop",standingPose,false,false);
                        Check(Mathf.Abs(standingPose.head-idlePose.head)<.12f,"stop restores standing height without animation root drift");
                        if(SessionState.GetBool(BatchRun,false))
                        { view.SetAimTarget(controller.transform.position+Vector3.forward*10+Vector3.up*1.3f,true); Begin(10); }
                        else { crouch=true; Begin(5); }
                        break;
                    case 5:
                        if(!Elapsed(.055f)) break; earlyCrouchHead=Sample().head; Begin(6); break;
                    case 6:
                        if(!Elapsed(.65f)) break; var crouchPose=Sample(); float drop=standingPose.head-crouchPose.head,earlyDrop=standingPose.head-earlyCrouchHead;
                        Capture("crouch"); WriteAnchors("crouch",crouchPose); ValidateAnchors("planted crouch",crouchPose,false,true); Check(true,"actual crouch shoe soles rest near the visible floor and torso stays above the collision root");
                        File.AppendAllText(Report,"Crouch head drop "+drop.ToString("F3")+"m; early-frame drop "+earlyDrop.ToString("F3")+"m.\n");
                        Check(drop>.18f && drop<.8f,"crouch bends ADAM into a meaningfully lower stance"); Check(earlyDrop<drop*.95f,"crouch transitions across frames instead of snapping immediately");
                        Check(Mathf.Abs(crouchPose.leftFoot.y-standingPose.leftFoot.y)<.12f && Mathf.Abs(crouchPose.rightFoot.y-standingPose.rightFoot.y)<.12f,"crouch preserves planted foot heights");
                        Check(Vector3.Distance(view.model.localScale,modelLocalScale)<.0001f,"crouch preserves original robot proportions"); desiredVelocity=Vector3.forward*.8f; ResetRange(); Begin(7); break;
                    case 7:
                        if(!Elapsed(.5f+view.walkClip.length*1.55f/.8f)) break; first=Sample(); Begin(8); break;
                    case 8:
                        if(!Elapsed(.3f)) break; var crouchWalking=Sample(); Check(RotationDelta(first,crouchWalking)>.5f && MeshDelta(first,crouchWalking)>.002f,"crouched locomotion continues to animate real legs and skin");
                        WriteAnchors("crouch-walk",crouchWalking); WriteRange("crouch-walk"); Check(true,"crouched feet remain grounded over a complete slower stride");
                        Capture("crouch-walk"); desiredVelocity=Vector3.zero; crouch=false; Begin(9); break;
                    case 9:
                        if(!Elapsed(.9f)) break; var upright=Sample(); Check(Mathf.Abs(upright.head-standingPose.head)<.12f,"standing up restores ADAM height");
                        view.SetAimTarget(controller.transform.position+Vector3.forward*10+Vector3.up*1.3f,true); Begin(10); break;
                    case 10:
                        if(!Elapsed(.5f)) break;
                        first=Sample(); aimTarget=controller.transform.position+Vector3.forward*8+Vector3.right*8+Vector3.up*1.3f;
                        view.SetAimTarget(aimTarget,true); Begin(11); break;
                    case 11:
                        if(!Elapsed(.5f)) break; var aim=Sample(); Check(view.AimAmount>.7f && (Quaternion.Angle(first.rotations[7],aim.rotations[7])>1 || MeshDelta(first,aim)>.02f),"actual firing arm tracks a changed world-space target");
                        float barrelError=Vector3.Angle(view.muzzle.forward,aimTarget-view.muzzle.position);
                        Check(barrelError<8,$"physical muzzle aims along the player sightline ({barrelError:F2}° error)");
                        Capture("aim"); firePose=aim; cannonAtRest=view.guardCannon.localPosition; fire=true; Begin(12); break;
                    case 12:
                        if(!Elapsed(.065f)) break; var shooting=Sample();
                        Check(view.ShotPoseAmount>.4f && (Quaternion.Angle(firePose.rotations[7],shooting.rotations[7])>.1f || MeshDelta(firePose,shooting)>.005f),
                            "one shot plays the authored upper-body layer and changes the actual arm or skinned surface");
                        Check(Vector3.Distance(cannonAtRest,view.guardCannon.localPosition)>.007f,"the visible cannon physically kicks back on fire");
                        Check(view.muzzle && view.muzzle.GetComponent<Light>() && view.muzzle.GetComponent<Light>().enabled,"actual wrist emitter flashes during fire"); Capture("fire"); Begin(13); break;
                    case 13:
                        if(!Elapsed(.8f)) break;
                        Check(!view.muzzle.GetComponent<Light>().enabled && view.ShotPoseAmount<.01f,"the flash and upper-body layer both settle after one shot");
                        Check(Vector3.Distance(cannonAtRest,view.guardCannon.localPosition)<.001f,"cannon returns to its resting mount after the impulse");
                        if(SessionState.GetBool(BatchRun,false))
                            Finish(true,"The actual guard passed walking, running, sprinting, root anchoring, aiming and upper-body firing checks.");
                        else { damage=true; Begin(14); }
                        break;
                    case 14:
                        if(!Elapsed(.15f)) break; Capture("hit"); damage=false; dead=true; Begin(15); break;
                    case 15:
                        if(!Elapsed(1.1f)) break; Check(Vector3.Distance(view.model.localScale,modelLocalScale)<.0001f,"death preserves ADAM proportions");
                        Check(Quaternion.Angle(view.model.localRotation,modelLocalRotation)>15 || RotationDelta(standingPose,Sample())>8,"dead ADAM leaves the live guard pose"); Capture("death");
                        Check(!UnityEngine.Object.FindAnyObjectByType<CampaignGame>(),"no campaign/profile or physical input component was instantiated");
                        Finish(true,"The actual guard passed walking, running, sprinting, root anchoring, crouch/stand, aiming and upper-body firing checks."); break;
                }
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch(Exception exception)
            {
                try { if(camera && controller) Capture("failed-phase-"+phase); } catch { }
                Finish(false,exception.ToString());
            }
        }
        static void Finish(bool passed,string detail)
        {
            if(completed) return; completed=true; SessionState.SetBool(Finished,true);
            SessionState.SetBool(Passed,passed); SessionState.SetString(Detail,detail); SessionState.SetBool(RestorePending,true);
            File.AppendAllText(Report,(passed ? "ANIMATION CHECKS: PASSED\n" : "ANIMATION CHECKS: FAILED\n")+detail+"\nRestoring original editor scene before publishing final result.\n");
            EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
            if(bakedMeshes!=null) foreach(var mesh in bakedMeshes) if(mesh) UnityEngine.Object.DestroyImmediate(mesh);
            bakedMeshes=null; vertexBuffers=null;
            QueueSceneRestore();
            if(EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); else Restore();
        }
        static void Restore()
        {
            if(!SessionState.GetBool(GlobalsSaved,false)) return;
            Application.runInBackground=SessionState.GetBool(Background,false); Time.timeScale=SessionState.GetFloat(Scale,1);
            SessionState.EraseBool(Background); SessionState.EraseFloat(Scale); SessionState.EraseBool(GlobalsSaved);
        }
        static void Mode(PlayModeStateChange change)
        {
            if(change!=PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key,false)) return;
            if(!SessionState.GetBool(Finished,false))
            {
                SessionState.SetBool(Passed,false); SessionState.SetString(Detail,"Play Mode ended before all animation checks completed.");
                SessionState.SetBool(RestorePending,true);
            }
            SessionState.EraseBool(Key); SessionState.EraseBool(Finished); EditorApplication.update-=Tick; Application.logMessageReceived-=Log; Restore();
            QueueSceneRestore();
        }
        static void QueueSceneRestore()
        {
            // A delayCall closure can be lost on the Play Mode domain reload. Keep
            // both the pending operation and original path in SessionState instead.
            EditorApplication.update-=TryRestoreScene; EditorApplication.update+=TryRestoreScene;
        }
        static void TryRestoreScene()
        {
            if(!SessionState.GetBool(RestorePending,false)) { EditorApplication.update-=TryRestoreScene; return; }
            // Mode() clears the active key only after EnteredEditMode. Avoid opening
            // the original scene while Unity is still restoring its Play Mode backup.
            if(SessionState.GetBool(Key,false)) return;
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string path=SessionState.GetString(Original,"");
            bool restored=false;
            try
            {
                if(string.IsNullOrEmpty(path) || !File.Exists(path)) throw new FileNotFoundException("Original ADAM test scene path is missing: "+path);
                if(SceneManager.GetActiveScene().path!=path) EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                restored=SceneManager.GetActiveScene().path==path;
                if(!restored) throw new InvalidOperationException("Scene restore returned a different active scene.");
                File.AppendAllText(Report,"PASS: original editor scene restored after Play Mode/domain reload: "+path+"\n");
            }
            catch(Exception exception)
            {
                SessionState.SetBool(Passed,false); File.AppendAllText(Report,"FAIL: editor scene restoration: "+exception+"\n");
            }
            bool passed=SessionState.GetBool(Passed,false) && restored;
            File.AppendAllText(Report,passed ? "RESULT: PASSED\n" : "RESULT: FAILED\n");
            SessionState.EraseBool(RestorePending); SessionState.EraseBool(Passed); SessionState.EraseString(Detail);
            if(restored) SessionState.EraseString(Original); // Preserve evidence if restoration ever fails.
            EditorApplication.update-=TryRestoreScene;
            if(SessionState.GetBool(BatchRun,false))
            { SessionState.EraseBool(BatchRun); RestoreCaptureRate(); EditorApplication.Exit(passed ? 0 : 1); }
        }
    }
}

