using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ZeroSector.Campaign
{
    [Serializable] public sealed class HandGestureHand
    {
        public string Side;
        // MediaPipe handedness confidence, not a gesture-class probability.
        public float Confidence;
        public Vector3[] Landmarks;
        public Vector3[] WorldLandmarks;
    }

    [Serializable] public sealed class HandGestureResult
    {
        public string Kind, Status, Action, Pose;
        public float Confidence, DurationMs;
        public long Sequence;
        public bool HandSample;
        // Upright image coordinates: x rightwards, y downwards, range 0..1.
        public HandGestureHand[] Hands;
        public WebcamHeadPose Head;
        public float FrameAgeSeconds { get; internal set; }
    }

    [Serializable] public sealed class WebcamHeadPose
    {
        public bool Tracked;
        public float Yaw, Pitch;
    }

    // Actual pre-trained ML hand landmarks and optional head pose.
    // The child never opens devices or sockets. Keep just the latest frames and results.
    public sealed class HandGestureRecognizer : IDisposable
    {
        readonly object sync=new object();
        AutoResetEvent wake;
        Thread worker;
        Process child;
        byte[] pendingFrame,reusableFrame;
        int pendingWidth,pendingHeight,pendingRotation;
        bool pendingMirror;
        long sequence,pendingSequence,pendingCapture;
        readonly Queue<(string json,long capture)> results=new();
        string status="Распознавание жестов выключено";
        volatile bool running,ready;
        string executable,bundle,cacheRoot;
        bool trackHead;
        public bool IsReady => ready && running;
        public string Status { get { lock(sync) return status; } }

        public void Start(bool enableHeadTracking=false)
        {
            if(running || (worker!=null && worker.IsAlive)) return;
            bundle=Path.Combine(Application.streamingAssetsPath,"HandGestures","hand-worker.zip");
            cacheRoot=Path.Combine(Application.persistentDataPath,"HandGestures");
            if(!File.Exists(bundle)) { SetStatus("Не найден автономный модуль жестов MediaPipe"); return; }
            trackHead=enableHeadTracking;
            running=true; ready=false;
            SetStatus(trackHead ? "Загружаю локальные модели кисти и лица…" : "Загружаю локальную модель кистей MediaPipe…");
            wake=new AutoResetEvent(false);
            worker=new Thread(Run) { IsBackground=true, Name="Doppelganger hand landmarks" };
            worker.Start();
        }

        public void Submit(Color32[] pixels,int width,int height,int rotation,bool verticallyMirrored)
        {
            if(!IsReady || pixels==null || pixels.Length!=width*height || width<16 || height<16 || width>1920 || height>1080) return;
            long capturedAt=Stopwatch.GetTimestamp();
            lock(sync)
            {
                if(!running) return;
                var rgb=pendingFrame;
                if(rgb==null && reusableFrame!=null) { rgb=reusableFrame; reusableFrame=null; }
                if(rgb==null || rgb.Length!=pixels.Length*3) rgb=new byte[pixels.Length*3];
                for(int i=0,j=0;i<pixels.Length;i++)
                {
                    rgb[j++]=pixels[i].r;
                    rgb[j++]=pixels[i].g;
                    rgb[j++]=pixels[i].b;
                }
                pendingFrame=rgb; pendingWidth=width; pendingHeight=height;
                pendingRotation=((rotation%360)+360)%360; pendingMirror=verticallyMirrored;
                pendingSequence=++sequence;
                pendingCapture=capturedAt;
                wake?.Set();
            }
        }

        public bool TryGetResult(out HandGestureResult result,bool bridgeGestures=false)
        {
            result=null;
            (string json,long capture) response;
            lock(sync)
            {
                if(results.Count==0) return false;
                response=results.Dequeue();
            }
            try { result=JsonUtility.FromJson<HandGestureResult>(response.json); }
            catch(Exception) { SetStatus("Неверный ответ модуля жестов"); return false; }
            if(result==null || result.Kind!="result") return false;
            result.FrameAgeSeconds=(Stopwatch.GetTimestamp()-response.capture)/(float)Stopwatch.Frequency;
            if(result.HandSample) Classify(result,bridgeGestures);
            return true;
        }

        void SetStatus(string text) { lock(sync) status=text; }

        void Run()
        {
            Process process=null;
            string lastError="";
            try
            {
                executable=PrepareRuntime();
                if(!running) return;
                var info=new ProcessStartInfo(executable) {
                    WorkingDirectory=Path.GetDirectoryName(executable), UseShellExecute=false,
                    Arguments=trackHead ? "--head" : "",
                    CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden,
                    RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true,
                    StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8
                };
                info.EnvironmentVariables["TF_CPP_MIN_LOG_LEVEL"]="3";
                process=new Process { StartInfo=info, EnableRaisingEvents=true };
                process.ErrorDataReceived+=(sender,args)=> { if(!string.IsNullOrWhiteSpace(args.Data)) lastError=args.Data; };
                lock(sync) { if(!running) return; child=process; }
                process.Start(); process.BeginErrorReadLine();
                string greeting=ReadLine(process,45000);
                if(greeting==null || greeting.IndexOf("\"ready\"",StringComparison.Ordinal)<0)
                    throw new IOException("Модель не загрузилась: "+(greeting??lastError));
                if(trackHead && greeting.IndexOf("\"HeadTracking\":true",StringComparison.Ordinal)<0)
                    throw new IOException("В архиве нет модуля поворота головы");
                ready=true; SetStatus("MediaPipe готов: покажи кисть целиком перед камерой");
                var writer=new BinaryWriter(process.StandardInput.BaseStream,Encoding.UTF8,true);
                while(running)
                {
                    byte[] frame; int w,h,r; bool mirror; long id,capture;
                    lock(sync)
                    {
                        frame=pendingFrame; pendingFrame=null;
                        w=pendingWidth; h=pendingHeight; r=pendingRotation;
                        mirror=pendingMirror; id=pendingSequence; capture=pendingCapture;
                    }
                    if(frame==null) { wake.WaitOne(200); continue; }
                    writer.Write(new byte[]{(byte)'H',(byte)'N',(byte)'D',(byte)'1'});
                    writer.Write(w); writer.Write(h); writer.Write(r); writer.Write(mirror?1:0);
                    writer.Write(frame.Length); writer.Write(id); writer.Write(frame); writer.Flush();
                    lock(sync) if(running && reusableFrame==null) reusableFrame=frame;
                    string response=ReadLine(process,12000);
                    if(response==null) throw new IOException("Модуль жестов завершился: "+lastError);
                    if(response.IndexOf("\"error\"",StringComparison.Ordinal)>=0) throw new IOException(response);
                    lock(sync)
                    {
                        if(!running) continue;
                        // In head mode adjacent replies alternate with and without a hand sample.
                        if(results.Count==2) results.Dequeue();
                        results.Enqueue((response,capture));
                    }
                }
            }
            catch(Exception error)
            {
                if(running) SetStatus("Жесты недоступны: "+error.Message);
            }
            finally
            {
                ready=false;
                try { if(process!=null && !process.HasExited) process.Kill(); } catch(Exception) { }
                process?.Dispose();
                lock(sync) { child=null; pendingFrame=reusableFrame=null; running=false; wake?.Dispose(); wake=null; }
            }
        }

        string PrepareRuntime()
        {
            SetStatus("Подготавливаю локальный модуль жестов…");
            string fingerprint;
            using(var sha=SHA256.Create()) using(var input=File.OpenRead(bundle))
                fingerprint=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").Substring(0,20);
            string target=Path.GetFullPath(Path.Combine(cacheRoot,fingerprint));
            string exe=Path.Combine(target,"HandGestureWorker.exe");
            if(File.Exists(Path.Combine(target,"ready.txt")) && RuntimeComplete(target)) return exe;
            Directory.CreateDirectory(cacheRoot);
            string staging=Path.GetFullPath(Path.Combine(cacheRoot,"unpacking-"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(staging);
            try
            {
                SetStatus("Первый запуск: распаковываю модель жестов, подожди…");
                using(var archive=ZipFile.OpenRead(bundle))
                {
                    long total=0;
                    if(archive.Entries.Count>5000) throw new IOException("Слишком большой архив распознавания");
                    foreach(var entry in archive.Entries)
                    {
                        if(!running) throw new OperationCanceledException();
                        total+=entry.Length;
                        if(total>700L*1024*1024) throw new IOException("Превышен размер модуля распознавания");
                        string destination=Path.GetFullPath(Path.Combine(staging,entry.FullName));
                        if(!destination.StartsWith(staging+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Небезопасное имя файла в архиве");
                        if(string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using(var source=entry.Open()) using(var output=File.Create(destination))
                        {
                            var block=new byte[65536]; int count;
                            while((count=source.Read(block,0,block.Length))>0)
                            { if(!running) throw new OperationCanceledException(); output.Write(block,0,count); }
                        }
                    }
                }
                if(!RuntimeComplete(staging)) throw new IOException("Архив распознавания неполный");
                File.WriteAllText(Path.Combine(staging,"ready.txt"),fingerprint);
                if(!Directory.Exists(target)) Directory.Move(staging,target);
                else if(!File.Exists(Path.Combine(target,"ready.txt")) || !RuntimeComplete(target)) throw new IOException("Неполный кэш жестов: удали папку "+target);
                return exe;
            }
            finally
            {
                // Only a freshly created child of this cache is eligible for cleanup.
                string prefix=Path.GetFullPath(cacheRoot)+Path.DirectorySeparatorChar;
                if(staging.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) && Directory.Exists(staging))
                { try { Directory.Delete(staging,true); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
            }
        }

        bool RuntimeComplete(string folder) => File.Exists(Path.Combine(folder,"HandGestureWorker.exe")) &&
            File.Exists(Path.Combine(folder,"hand_landmarker.task")) &&
            (!trackHead || File.Exists(Path.Combine(folder,"face_landmarker.task")));

        static string ReadLine(Process process,int timeout)
        {
            var read=process.StandardOutput.ReadLineAsync();
            if(!read.Wait(timeout)) { try { process.Kill(); } catch(Exception) { } throw new IOException("Истекло время ожидания модели жестов"); }
            return read.Result;
        }

        public void Stop()
        {
            running=false; ready=false;
            lock(sync) { status="Распознавание жестов выключено"; pendingFrame=null; results.Clear(); wake?.Set(); try { if(child!=null && !child.HasExited) child.Kill(); } catch(Exception) { } }
            // No main-thread wait for inference. Closing the process unblocks its reader.
        }

        public void Dispose() { Stop(); }

        public static void Classify(HandGestureResult result,bool bridgeGestures=false)
        {
            result.Action=""; result.Pose="Рука не видна"; result.Confidence=0;
            if(result.Hands==null || result.Hands.Length==0) return;
            var poses=new string[result.Hands.Length]; int valid=0;
            float confidence=1;
            for(int i=0;i<result.Hands.Length;i++)
            {
                poses[i]=SinglePose(result.Hands[i],bridgeGestures);
                if(poses[i]!="") { valid++; confidence=Mathf.Min(confidence,result.Hands[i].Confidence); }
            }
            if(valid==0) { result.Pose="Покажи кисть целиком, ладонью к камере"; return; }
            if(poses.Length>=2)
            {
                if(poses[0]=="" || poses[1]=="") { result.Pose="Покажи обе кисти целиком или оставь одну руку"; return; }
                if(poses[0]=="palm" && poses[1]=="palm") { Set(result,"pause","Две открытые ладони — пауза",confidence); return; }
                if(poses[0]=="fist" && poses[1]=="fist") { Set(result,"aim","Два кулака — прицеливание",confidence); return; }
                if((poses[0]=="palm" && poses[1]=="fist") || (poses[1]=="palm" && poses[0]=="fist"))
                { Set(result,"fire","Ладонь и кулак — огонь",confidence); return; }
                result.Pose="Для одиночной команды оставь в кадре одну руку"; return;
            }
            string pose="";
            for(int i=0;i<poses.Length;i++) if(poses[i]!="") { pose=poses[i]; break; }
            switch(pose)
            {
                case "palm": Set(result,"stop","Открытая ладонь — стоп",confidence); break;
                case "fist": Set(result,"shield","Кулак — достать щит",confidence); break;
                case "forward": Set(result,"forward","Указательный вверх — вперёд",confidence); break;
                case "backward": Set(result,"backward","Указательный вниз — назад",confidence); break;
                case "left": Set(result,"left","Указательный влево — влево",confidence); break;
                case "right": Set(result,"right","Указательный вправо — вправо",confidence); break;
                case "victory": Set(result,"run","Указательный и средний — бег",confidence); break;
                case "walk": Set(result,"walk","Большой, указательный и средний — идти шагом",confidence); break;
                case "thumb_up": Set(result,"jump","Большой палец вверх — прыжок",confidence); break;
                case "thumb_down": Set(result,"crouch","Большой палец вниз — присесть",confidence); break;
                case "four": Set(result,"stand","Четыре пальца без большого — встать",confidence); break;
                case "three": Set(result,"reload","Указательный, средний и безымянный — перезарядка",confidence); break;
                case "pinky": Set(result,"inspect","Мизинец — осмотр оружия",confidence); break;
                case "horns": Set(result,"switch","Указательный и мизинец — сменить оружие",confidence); break;
                case "weapon": Set(result,"weapon","Большой, указательный и мизинец — оружие",confidence); break;
                case "rifle": Set(result,"rifle","Один указательный — автомат",confidence); break;
                case "pistol": Set(result,"pistol","Указательный и средний — пистолет",confidence); break;
                case "ok": Set(result,"interact","Кольцо OK — взаимодействие",confidence); break;
                default: result.Pose="Жест не распознан"; break;
            }
        }

        static void Set(HandGestureResult result,string action,string pose,float confidence)
        { result.Action=action; result.Pose=pose; result.Confidence=confidence; }

        static string SinglePose(HandGestureHand hand,bool bridgeGestures)
        {
            if(hand==null || float.IsNaN(hand.Confidence) || hand.Confidence<.72f || hand.Landmarks==null || hand.Landmarks.Length!=21) return "";
            var p=hand.Landmarks;
            for(int i=0;i<p.Length;i++) if(!Finite(p[i]) || p[i].x<-.03f || p[i].x>1.03f || p[i].y<-.03f || p[i].y>1.03f) return "";
            float imagePalm=Vector2.Distance(p[0],p[9]);
            if(imagePalm<.035f) return ""; // Tiny hands cannot give reliable finger poses.
            var w=hand.WorldLandmarks!=null && hand.WorldLandmarks.Length==21 ? hand.WorldLandmarks : p;
            for(int i=0;i<w.Length;i++) if(!Finite(w[i])) return "";
            float palm=Vector3.Distance(w[0],w[9]);
            if(palm<.0001f) return "";
            bool index=Extended(w,5),middle=Extended(w,9),ring=Extended(w,13),pinky=Extended(w,17);
            bool thumb=Angle(w[1],w[2],w[3])>140 && Angle(w[2],w[3],w[4])>150 && Vector3.Distance(w[4],w[5])>.47f*palm;
            if(middle && ring && pinky && Vector3.Distance(w[4],w[8])<.35f*palm) return "ok";
            int mask=(index?1:0)|(middle?2:0)|(ring?4:0)|(pinky?8:0);
            if(bridgeGestures && (mask==1 || mask==3))
            {
                // A separated thumb can make the pose look like a different command.
                // On the bridge these two masks never fall through to movement.
                bool thumbTucked=!thumb && Vector3.Distance(w[4],w[5])<.85f*palm;
                if(!thumbTucked) return "";
                return mask==1 ? "rifle" : "pistol";
            }
            if(mask==15) return thumb?"palm":"four";
            if(mask==0)
            {
                if(!thumb) return "fist";
                Vector3 direction=p[4]-p[2];
                if(Mathf.Abs(direction.y)>Mathf.Abs(direction.x)*.85f) return direction.y<0?"thumb_up":"thumb_down";
                return "";
            }
            if(mask==1)
            {
                Vector3 direction=p[8]-p[5];
                if(Mathf.Abs(direction.y)>Mathf.Abs(direction.x)*1.15f) return direction.y<0?"forward":"backward";
                if(Mathf.Abs(direction.x)>Mathf.Abs(direction.y)*1.15f) return direction.x<0?"left":"right";
                return "";
            }
            if(mask==3) return thumb?"walk":"victory";
            if(mask==7 && !thumb) return "three";
            if(mask==8 && !thumb) return "pinky";
            if(mask==9) return thumb?"weapon":"horns";
            return "";
        }

        static bool Extended(Vector3[] p,int mcp)
        {
            return Angle(p[mcp],p[mcp+1],p[mcp+2])>150 && Angle(p[mcp+1],p[mcp+2],p[mcp+3])>140
                && Vector3.Distance(p[0],p[mcp+3])>Vector3.Distance(p[0],p[mcp+1])*1.10f;
        }
        static float Angle(Vector3 a,Vector3 b,Vector3 c) => Vector3.Angle(a-b,c-b);
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
