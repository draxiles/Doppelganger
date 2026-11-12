using System;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    /// <summary>
    /// A reproducible staged film of the real authored Unity scenes and imported rigs.
    /// No generated gameplay images, live input, network, webcam, microphone or player save.
    /// </summary>
    [InitializeOnLoad]
    public static class CampaignTrailerCapture
    {
        const string Key="Doppelganger.TrailerCapture", Pending=Key+".Restore";
        const string Folder="../Deliverables/Lab08", AssetFolder=Folder+"/assets";
        const string Report="Diagnostics/CampaignPreviews/trailer-capture.txt";
        const int Width=1280, Height=720, Fps=24, AudioRate=48000;
        static readonly int[] Seconds={5,5,7,4,6,5,4};
        static readonly int[] Chapters={0,0,0,1,2,2,2};
        static readonly string[] Shots={"earth","bridge","weapons","engineering","adam","core","ending"};
        static CampaignGame game;
        static FirstPersonPresentation weapon;
        static RobotPresentation robot;
        static Transform robotRoot;
        static Light cinematicKey,cinematicRim;
        static Camera camera;
        static Canvas canvas;
        static CanvasGroup overlay;
        static Text title,subtitle,kicker,disclosure;
        static Image black;
        static MediaEncoder encoder;
        static RenderTexture target;
        static Texture2D image;
        static NativeArray<float> sound;
        static int shot,frame,total,lastFrame=-1,warmFrames;
        static bool ready,done,loading;
        static double deadline;
        static string output;
        static byte[] realSaveBefore;
        static string realSavePath;
        public static bool IsRunning => SessionState.GetBool(Key,false);

        static CampaignTrailerCapture()
        {
            EditorApplication.playModeStateChanged+=Mode;
            if(IsRunning) Attach();
            if(SessionState.GetBool(Pending,false)) QueueRestore();
        }

        public static void Start()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Finish importing and stop Play Mode before filming.");
            var scene=SceneManager.GetActiveScene();
            if(scene.isDirty || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save the current scene before filming.");
            foreach(string path in CampaignGame.ScenePaths) if(!File.Exists(path)) throw new FileNotFoundException(path);
            Directory.CreateDirectory(AssetFolder); Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report,"DOPPELGANGER / staged in-engine cinematic\n36 seconds, 1280 x 720, 24 fps, real scene geometry and animated imported rigs.\nCamera/actor staging is not an autonomous AI gameplay benchmark.\n");
            SessionState.SetString(Key+".Scene",scene.path);
            SessionState.SetBool(Key+".Background",Application.runInBackground);
            SessionState.SetFloat(Key+".Scale",Time.timeScale);
            SessionState.SetInt(Key+".Capture",Time.captureFramerate);
            SessionState.SetInt(Key+".Rate",Application.targetFrameRate);
            SessionState.SetInt(Key+".Vsync",QualitySettings.vSyncCount);
            SessionState.SetString(Key+".Profile",CampaignSession.EditorTestSavePath ?? "");
            SessionState.SetBool(Key,true);
            try { EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0]); Attach(); EditorApplication.EnterPlaymode(); }
            catch { SessionState.SetBool(Key,false); RestoreGlobals(); throw; }
        }

        static void Attach()
        {
            shot=frame=total=0; lastFrame=-1; warmFrames=0; ready=done=loading=false;
            deadline=EditorApplication.timeSinceStartup+1200;
            EditorApplication.update-=Tick; EditorApplication.update+=Tick;
            Application.logMessageReceived-=Log; Application.logMessageReceived+=Log;
        }
        static void Log(string text,string stack,LogType type)
        {
            if(!done && (type==LogType.Exception || type==LogType.Error || type==LogType.Assert))
                Finish(false,"Runtime/encoder error: "+text+"\n"+stack);
        }

        static void Tick()
        {
            if(done || !IsRunning || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if(EditorApplication.timeSinceStartup>deadline) { Finish(false,"Capture exceeded 20-minute timeout."); return; }
            if(!EditorApplication.isPlaying) return;
            if(EditorApplication.isPaused) { Finish(false,"Play Mode paused during capture."); return; }
            try
            {
                if(!ready || loading)
                {
                    game=UnityEngine.Object.FindAnyObjectByType<CampaignGame>();
                    if(!game || game.chapter!=Chapters[shot] || !game.Devices || !game.presentation || !game.presentation.eyeHeight)
                    { EditorApplication.QueuePlayerLoopUpdate(); return; }
                    if(!ready) InitializeEncoder();
                    SetupScene(); ready=true; loading=false; warmFrames=4; lastFrame=Time.frameCount;
                    Stage(); EditorApplication.QueuePlayerLoopUpdate(); return;
                }
                if(Time.frameCount==lastFrame) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                lastFrame=Time.frameCount;
                if(warmFrames>0) { warmFrames--; Stage(); EditorApplication.QueuePlayerLoopUpdate(); return; }
                ComposeCamera(); ComposeOverlay(); Canvas.ForceUpdateCanvases();
                Capture();
                if(frame==Mathf.Min(24,Seconds[shot]*Fps-1)) SaveStill(Shots[shot]+"-cinematic.png",true);
                if(shot==2 && frame==16) SaveStill("weapon-terraformer.png",false);
                if(shot==2 && frame==115) SaveStill("weapon-robot.png",false);
                if(shot==4 && frame==24) Portrait();
                if(shot==6 && frame==24) LogOrientation("IDLE / ending");
                if(!encoder.AddFrame(image)) throw new InvalidOperationException("Unity native encoder rejected frame "+total);
                AddSound(); total++; frame++;
                if(frame%Fps==0) File.AppendAllText(Report,$"FRAME {total} / 864 / {Shots[shot]}\n");
                if(frame>=Seconds[shot]*Fps)
                {
                    int previous=Chapters[shot]; frame=0; shot++;
                    if(shot>=Seconds.Length) { Finish(true,"All seven shots captured from actual Unity scenes."); return; }
                    if(Chapters[shot]!=previous)
                    {
                        loading=true; SceneManager.LoadScene(CampaignGame.ScenePaths[Chapters[shot]]);
                        EditorApplication.QueuePlayerLoopUpdate(); return;
                    }
                    PrepareShot(); warmFrames=3;
                }
                Stage(); EditorApplication.QueuePlayerLoopUpdate();
            }
            catch(Exception exception) { Finish(false,exception.ToString()); }
        }

        static void InitializeEncoder()
        {
            realSavePath=CampaignSession.SavePath;
            realSaveBefore=File.Exists(realSavePath) ? File.ReadAllBytes(realSavePath) : null;
            CampaignSession.EditorTestSavePath="Temp/DoppelgangerChecks/trailer-isolated-save.json";
            Application.runInBackground=true; Time.timeScale=1; Time.captureFramerate=Fps;
            Application.targetFrameRate=-1; QualitySettings.vSyncCount=0;
            output=Path.GetFullPath(Folder+"/Doppelganger_Trailer.mp4");
            var video=new VideoTrackEncoderAttributes(new H264EncoderAttributes
            {gopSize=Fps, numConsecutiveBFrames=2, profile=VideoEncodingProfile.H264High})
            {frameRate=new MediaRational(Fps),width=Width,height=Height,targetBitRate=9000000,includeAlpha=false};
            var audio=new AudioTrackAttributes {sampleRate=new MediaRational(AudioRate),channelCount=2,language="ru"};
            encoder=new MediaEncoder(output,video,audio);
            sound=new NativeArray<float>(AudioRate/Fps*2,Allocator.Persistent);
            target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32); target.Create();
            image=new Texture2D(Width,Height,TextureFormat.RGBA32,false);
            File.AppendAllText(Report,"OUTPUT "+output+"\nAudio: original mathematically synthesized reactor hum and pulses; no external music.\n");
        }

        static void SetupScene()
        {
            CampaignSession.EditorTestSavePath="Temp/DoppelgangerChecks/trailer-isolated-save.json";
            game.StartCurrentEpisode(); game.enabled=false; game.hud.enabled=false; game.Devices.enabled=false;
            weapon=game.presentation; camera=game.eyes; camera.transform.SetParent(null,true);
            game.player.enabled=false;
            foreach(var source in UnityEngine.Object.FindObjectsByType<AudioSource>()) source.mute=true;
            foreach(var enemy in game.Enemies) if(enemy) enemy.gameObject.SetActive(false);
            robot=null; robotRoot=null;
            if(game.Enemies.Count>0)
            {
                var actor=game.Enemies[0]; actor.gameObject.SetActive(true); actor.body.enabled=false;
                robot=actor.GetComponentInChildren<RobotPresentation>(); robotRoot=actor.transform;
                if(!robot || !robot.IsAnimating) throw new InvalidOperationException("ADAM animation graph is not running in the actual scene.");
                foreach(var light in actor.GetComponentsInChildren<Light>(true))
                    if(light.name=="ADAM identification light") light.enabled=false;
                var keyLight=new GameObject("Temporary cinematic ADAM soft key",typeof(Light)).GetComponent<Light>(); cinematicKey=keyLight;
                keyLight.type=LightType.Point; keyLight.color=new Color(.7f,.85f,1); keyLight.intensity=4.5f;
                keyLight.range=9; keyLight.shadows=LightShadows.None; keyLight.transform.position=new Vector3(1,2.4f,-6);
                var fill=new GameObject("Temporary cinematic ADAM warm rim",typeof(Light)).GetComponent<Light>(); cinematicRim=fill;
                fill.type=LightType.Point; fill.color=new Color(1,.42f,.15f); fill.intensity=3; fill.range=7;
                fill.transform.position=new Vector3(-3,2.2f,-2); fill.shadows=LightShadows.None;
            }
            BuildOverlay(); PrepareShot();
        }

        static void PrepareShot()
        {
            bool guns=shot==2;
            weapon.enabled=guns; weapon.weaponPivot.gameObject.SetActive(guns);
            if(guns) { weapon.Equip(0); weapon.RefillAmmo(); }
            if(robotRoot) robotRoot.gameObject.SetActive(shot==4 || shot==6);
            File.AppendAllText(Report,"SHOT "+shot+" / "+Shots[shot]+" / chapter="+game.chapter+"\n");
            if(shot==4) ValidateAdamTrack();
        }

        static void Stage()
        {
            float t=frame/(float)Fps;
            if(shot==2)
            {
                weapon.SetInput(0,Vector2.zero,0,false,false,false,frame==116,true);
                if(frame==10 || frame==17 || frame==24)
                { if(weapon.TryConsumeRound()) weapon.OnShot(false); }
                if(frame==30) weapon.BeginReload();
                if(frame==89) weapon.RequestEquip(1);
            }
            if(robotRoot && (shot==4 || shot==6))
            {
                Vector3 position=shot==4 ? AdamPosition(t) : new Vector3(3,.02f,8.4f);
                Vector3 velocity=shot==4 && t<4.5f ? new Vector3(.28f,0,.1f) : Vector3.zero;
                robotRoot.position=position;
                Vector3 towardCamera=shot==4 ? Vector3.Lerp(new Vector3(5,0,5.8f),new Vector3(5.4f,0,6.1f),Ease(t/6))-position : new Vector3(2,0,-2.4f);
                Vector3 visibleFront=VisualFront();
                robotRoot.rotation=Quaternion.FromToRotation(visibleFront,towardCamera.normalized)*robotRoot.rotation;
                if(cinematicKey) cinematicKey.transform.position=position+towardCamera.normalized*2.5f+Vector3.up*2.2f;
                if(cinematicRim) cinematicRim.transform.position=position-towardCamera.normalized*2+Vector3.up*1.8f;
                bool crouch=shot==4 && t>3.2f && t<4.8f;
                bool fire=shot==4 && t>4.9f && frame%12==0;
                robot.SetAimTarget(position+towardCamera+Vector3.up*1.5f,t>4.6f || shot==6);
                robot.Tick(velocity,crouch,fire,false,false);
                if(fire) game.Beam(robot.MuzzlePosition,new Vector3(2.3f,1.1f,-1),new Color(.7f,.25f,1));
            }
        }

        static float Ease(float value) => Mathf.SmoothStep(0,1,Mathf.Clamp01(value));
        static void ComposeCamera()
        {
            float u=Ease(frame/(float)(Seconds[shot]*Fps-1));
            Vector3 from,to,look; float fov=58;
            switch(shot)
            {
                case 0: from=new Vector3(0,200,180); to=new Vector3(25,185,190); look=new Vector3(-28,-130,240); fov=58; break;
                case 1: from=new Vector3(-3,2.6f,-8); to=new Vector3(-1.5f,2.1f,7); look=new Vector3(0,2.2f,20); fov=66; break;
                case 2: from=new Vector3(0,1.65f,4); to=from; look=new Vector3(0,1.65f,20); fov=75; break;
                case 3: from=new Vector3(-1.3f,2.25f,-17); to=new Vector3(-1.3f,2.0f,-7.2f); look=new Vector3(-1.3f,1.7f,5); fov=66; break;
                case 4: from=new Vector3(5,1.6f,5.8f); to=new Vector3(5.4f,1.5f,6.1f); look=robotRoot.position+Vector3.up*.95f; fov=45; break;
                case 5: from=new Vector3(-6,2.5f,-10); to=new Vector3(5.8f,2.7f,-8); look=new Vector3(0,2.6f,2); fov=62; break;
                default: from=new Vector3(5,1.6f,5.8f); to=new Vector3(4,1.5f,6.5f); look=robotRoot.position+Vector3.up*1.2f; fov=43; break;
            }
            camera.transform.position=Vector3.Lerp(from,to,u); camera.transform.LookAt(look); camera.fieldOfView=fov;
            camera.aspect=Width/(float)Height;
        }

        static void BuildOverlay()
        {
            var root=new GameObject("Temporary cinematic caption canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(CanvasGroup));
            canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=.1f;
            canvas.sortingOrder=1000; overlay=root.GetComponent<CanvasGroup>();
            var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(Width,Height);
            var font=Font.CreateDynamicFontFromOSFont(new[]{"Segoe UI","Arial"},48);
            AddPanel(root.transform,"Top letterbox",new Vector2(0,1),new Vector2(1,1),new Vector2(0,-28),new Vector2(0,56),new Color(0,0,0,.95f));
            AddPanel(root.transform,"Bottom letterbox",Vector2.zero,new Vector2(1,0),new Vector2(0,28),new Vector2(0,56),new Color(0,0,0,.95f));
            kicker=AddText(root.transform,font,22,FontStyle.Bold,new Color(.23f,.88f,1),new Vector2(60,-96),new Vector2(1100,45));
            title=AddText(root.transform,font,48,FontStyle.Bold,Color.white,new Vector2(60,-142),new Vector2(1160,95));
            subtitle=AddText(root.transform,font,25,FontStyle.Normal,new Color(.85f,.9f,.95f),new Vector2(60,-234),new Vector2(1100,90));
            disclosure=AddText(root.transform,font,14,FontStyle.Normal,new Color(.6f,.7f,.78f),new Vector2(60,-681),new Vector2(1100,22));
            disclosure.text="Постановочные кадры на движке игры  /  Учебный прототип Unity 6";
            black=AddPanel(root.transform,"Cinematic fades",Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,Color.clear);
        }

        static Image AddPanel(Transform root,string name,Vector2 min,Vector2 max,Vector2 anchored,Vector2 size,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image)); go.transform.SetParent(root,false);
            var rect=go.GetComponent<RectTransform>(); rect.anchorMin=min; rect.anchorMax=max; rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=anchored; rect.sizeDelta=size;
            var panel=go.GetComponent<Image>(); panel.color=color; panel.raycastTarget=false; return panel;
        }
        static Text AddText(Transform root,Font font,int size,FontStyle style,Color color,Vector2 position,Vector2 box)
        {
            var go=new GameObject("Cinematic text",typeof(RectTransform),typeof(Text)); go.transform.SetParent(root,false);
            var rect=go.GetComponent<RectTransform>(); rect.anchorMin=rect.anchorMax=new Vector2(0,1); rect.pivot=new Vector2(0,1); rect.anchoredPosition=position; rect.sizeDelta=box;
            var text=go.GetComponent<Text>(); text.font=font; text.fontSize=size; text.fontStyle=style; text.color=color; text.raycastTarget=false;
            text.horizontalOverflow=HorizontalWrapMode.Overflow; text.verticalOverflow=VerticalWrapMode.Overflow;
            var shadow=go.AddComponent<Shadow>(); shadow.effectColor=new Color(0,0,0,.8f); shadow.effectDistance=new Vector2(1.25f,-1.25f);
            return text;
        }
        static void ComposeOverlay()
        {
            string[] small={"2187 / ОРБИТА ЗЕМЛИ","КОРАБЛЬ «РУБЕЖ»","ОСТАВАЙСЯ В ДВИЖЕНИИ","02 / ПАМЯТЬ КОРАБЛЯ","ЭКСПЕРИМЕНТ D-18","03 / НЕЙРОННОЕ ЯДРО","DOPPELGÄNGER"};
            string[] large={"Связь с Землёй потеряна.","Кто-то остался на борту.","Каждое действие оставляет след.","Восстанови питание.","Он запоминает твою тактику.","Измени свои привычки.","ДВОЙНИК"};
            string[] lines={"Аварийный сигнал привёл тебя сюда.","Верни управление. Узнай, кто говорит твоим голосом.","Два оружия. Риск. Решения под огнём.","Пройди охрану и открой путь к ядру.","Передвижение · огонь · присед · приоритет энергии","Пока противник не использовал их против тебя.","Сразись с отражением собственной тактики.\nДамир · AI и техническая часть    /    Айгерим · концепция и UI"};
            kicker.text=small[shot]; title.text=large[shot]; subtitle.text=lines[shot];
            float t=frame/(float)Fps,duration=Seconds[shot];
            float fade=shot==0 ? Mathf.Clamp01(1-t/.8f) : shot==6 ? Mathf.Clamp01((t-duration+1.0f)/1.0f) : 0;
            black.color=new Color(0,0,0,fade);
            float alpha=shot==2 ? .9f : 1;
            kicker.color=new Color(.23f,.88f,1,alpha); title.color=new Color(1,1,1,alpha);
        }

        static void Capture()
        {
            var previous=RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target; image.ReadPixels(new Rect(0,0,Width,Height),0,0); image.Apply(false);
            }
            finally { RenderTexture.active=previous; }
        }
        static void SaveStill(string name,bool withTitles)
        {
            if(!withTitles) { overlay.alpha=0; Canvas.ForceUpdateCanvases(); Capture(); }
            File.WriteAllBytes(AssetFolder+"/"+name,image.EncodeToPNG());
            if(!withTitles) { overlay.alpha=1; Canvas.ForceUpdateCanvases(); Capture(); }
        }
        static void Portrait()
        {
            Vector3 position=camera.transform.position; Quaternion rotation=camera.transform.rotation; float fov=camera.fieldOfView;
            try
            {
                camera.transform.position=robotRoot.position+new Vector3(1.9f,1.15f,-2.5f); camera.transform.LookAt(robotRoot.position+Vector3.up*.95f); camera.fieldOfView=36;
                ValidateVisibleBody(camera.transform.position,robotRoot.position,"core portrait");
                overlay.alpha=0; Canvas.ForceUpdateCanvases(); Capture();
                File.WriteAllBytes(AssetFolder+"/adam-portrait-core.png",image.EncodeToPNG());
                LogOrientation("WALK / portrait");
            }
            finally { camera.transform.SetPositionAndRotation(position,rotation); camera.fieldOfView=fov; overlay.alpha=1; Canvas.ForceUpdateCanvases(); Capture(); }
        }

        static Vector3 VisualFront()
        {
            if(!robot || !robot.head) return robotRoot ? robotRoot.forward : Vector3.forward;
            var sockets=robot.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Eye_L_Sclera" || t.name=="Eye_R_Sclera").ToArray();
            if(sockets.Length==0) return robotRoot.forward;
            Vector3 center=Vector3.zero; foreach(var socket in sockets) center+=socket.position; center/=sockets.Length;
            Vector3 forward=Vector3.ProjectOnPlane(center-robot.head.position,Vector3.up);
            return forward.sqrMagnitude>.00001f ? forward.normalized : robotRoot.forward;
        }
        static void LogOrientation(string label)
        {
            Vector3 visual=VisualFront(),bodyFront=robot.animator.bodyRotation*Vector3.forward;
            File.AppendAllText(Report,$"ADAM {label}: bodyRotationEuler={robot.animator.bodyRotation.eulerAngles}; bodyForwardDotActor={Vector3.Dot(bodyFront,robotRoot.forward):F4}; eyesVisualForward={visual}; eyesForwardDotActor={Vector3.Dot(visual,robotRoot.forward):F4}; visualFacesCameraDot={Vector3.Dot(visual,Vector3.ProjectOnPlane(camera.transform.position-robotRoot.position,Vector3.up).normalized):F4}\n");
        }
        static Vector3 AdamPosition(float t)
        { t=Mathf.Min(t,4.5f); return new Vector3(3+t*.28f,.02f,8.4f+t*.1f); }

        static void ValidateAdamTrack()
        {
            Physics.SyncTransforms();
            Vector3 oldPosition=camera.transform.position; Quaternion oldRotation=camera.transform.rotation; float oldFov=camera.fieldOfView;
            try
            {
                for(int sample=0;sample<=48;sample++)
                {
                    float u=sample/48f,t=u*6;
                    Vector3 actor=AdamPosition(t),lens=Vector3.Lerp(new Vector3(5,1.6f,5.8f),new Vector3(5.4f,1.5f,6.1f),Ease(u));
                    if(Physics.CheckCapsule(actor+Vector3.up*.45f,actor+Vector3.up*1.35f,.32f,~0,QueryTriggerInteraction.Ignore))
                        throw new InvalidOperationException("ADAM cinematic actor track enters level collision at "+actor);
                    camera.transform.position=lens; camera.transform.LookAt(actor+Vector3.up*.95f); camera.fieldOfView=45;
                    ValidateVisibleBody(lens,actor,"ADAM track sample "+sample);
                }
                File.AppendAllText(Report,"PASS: 49 ADAM track samples: actor capsule/camera sphere, camera near-frustum corners, and 9 unobstructed rays to head/torso/feet across body width.\n");
            }
            finally { camera.transform.SetPositionAndRotation(oldPosition,oldRotation); camera.fieldOfView=oldFov; }
        }
        static void ValidateVisibleBody(Vector3 lens,Vector3 actor,string label)
        {
            if(Physics.CheckSphere(lens,.12f,~0,QueryTriggerInteraction.Ignore)) throw new InvalidOperationException(label+": camera intersects geometry.");
            foreach(float height in new[]{.2f,.95f,1.6f}) foreach(float side in new[]{-.32f,0,.32f})
            {
                Vector3 point=actor+Vector3.up*height+camera.transform.right*side;
                if(Physics.Linecast(lens,point,out var hit,~0,QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException(label+": body view blocked by "+hit.collider.name+" at "+hit.point);
            }
            // Only the immediate near clipping volume must be empty. Distant walls are a valid background.
            var corners=new Vector3[4]; camera.CalculateFrustumCorners(new Rect(0,0,1,1),.15f,Camera.MonoOrStereoscopicEye.Mono,corners);
            foreach(var corner in corners)
                if(Physics.Linecast(lens,camera.transform.TransformPoint(corner),out var hit,~0,QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException(label+": near-frustum edge intersects "+hit.collider.name);
        }

        static void AddSound()
        {
            int samples=AudioRate/Fps;
            for(int i=0;i<samples;i++)
            {
                double t=(total*samples+i)/(double)AudioRate;
                float fade=Mathf.Clamp01((float)t/1.5f)*Mathf.Clamp01((36-(float)t)/1.2f);
                double pulse=Math.Exp(-((t%1.2)*7));
                double value=(Math.Sin(t*Math.PI*2*43)*.065+Math.Sin(t*Math.PI*2*67)*.022+Math.Sin(t*Math.PI*2*110)*.012);
                value+=Math.Sin(t*Math.PI*2*(shot>=4 ? 82 : 55))*.065*pulse;
                if(shot==6) value+=Math.Sin(t*Math.PI*2*220)*.025*Math.Exp(-frame/(double)Fps);
                float result=(float)value*fade;
                sound[i*2]=result; sound[i*2+1]=result*.96f;
            }
            if(!encoder.AddSamples(sound)) throw new InvalidOperationException("Unity native encoder rejected audio.");
        }

        static void Finish(bool success,string detail)
        {
            if(done) return; done=true;
            EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
            try { encoder?.Dispose(); encoder=null; } catch(Exception ex) { success=false; detail+="\nEncoder close: "+ex; }
            if(sound.IsCreated) sound.Dispose();
            if(target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if(image) UnityEngine.Object.DestroyImmediate(image);
            if(realSavePath!=null)
            {
                bool preserved=realSaveBefore==null ? !File.Exists(realSavePath) : File.Exists(realSavePath) && realSaveBefore.SequenceEqual(File.ReadAllBytes(realSavePath));
                if(!preserved) { success=false; detail+="\nFAIL: real save bytes changed."; }
                else detail+="\nPASS: real player save left byte-for-byte unchanged.";
            }
            if(success && (!File.Exists(output) || new FileInfo(output).Length<100000)) { success=false; detail+="\nMP4 missing or empty."; }
            File.AppendAllText(Report,"RESULT: "+(success ? "PASSED" : "FAILED")+"\nFrames: "+total+"\n"+detail+"\n");
            SessionState.SetBool(Key,false); SessionState.SetBool(Pending,true);
            RestoreGlobals();
            QueueRestore();
            if(EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        }
        static void Mode(PlayModeStateChange mode)
        {
            if(mode==PlayModeStateChange.ExitingPlayMode && IsRunning && !done) Finish(false,"Capture interrupted before completion.");
            if(mode==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending,false)) QueueRestore();
        }
        static void RestoreGlobals()
        {
            Application.runInBackground=SessionState.GetBool(Key+".Background",false);
            Time.timeScale=SessionState.GetFloat(Key+".Scale",1);
            Time.captureFramerate=SessionState.GetInt(Key+".Capture",0);
            Application.targetFrameRate=SessionState.GetInt(Key+".Rate",-1);
            QualitySettings.vSyncCount=SessionState.GetInt(Key+".Vsync",0);
            CampaignSession.EditorTestSavePath=SessionState.GetString(Key+".Profile","");
        }
        static void QueueRestore()
        { EditorApplication.update-=RestoreScene; EditorApplication.update+=RestoreScene; }
        static void RestoreScene()
        {
            if(!SessionState.GetBool(Pending,false)) { EditorApplication.update-=RestoreScene; return; }
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            RestoreGlobals(); string path=SessionState.GetString(Key+".Scene","");
            if(File.Exists(path)) EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            SessionState.SetBool(Pending,false); EditorApplication.update-=RestoreScene;
            File.AppendAllText(Report,"RESTORED: original scene and editor timing/background/profile settings.\n");
        }
    }
}
