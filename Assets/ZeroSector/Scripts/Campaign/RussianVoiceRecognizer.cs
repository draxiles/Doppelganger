using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ZeroSector.Campaign
{
    // Explicitly opt-in, offline Russian ASR. Unity microphone calls stay on the main
    // thread; model loading and decoding use a bounded background worker.
    public sealed class RussianVoiceRecognizer : MonoBehaviour
    {
        public const string ModelFolder="vosk-model-small-ru-0.22";
        public static string Grammar => SpokenCommandCatalog.Grammar;
        public bool Requested { get; private set; }
        public bool Listening { get; private set; }
        public string DeviceName => microphone ?? "";
        public string Status { get; private set; }="Русский голос выключен. F7 — включить локальное распознавание.";
        public float MicrophoneLevel { get; private set; }
        public event Action<string> Phrase;
        public Func<bool> SuppressInput { get; set; }

        VoiceSession session;
        AudioClip recording;
        string microphone;
        float[] microphoneBuffer;
        int position;
        float microphoneStarted;
        const int Rate=16000, Chunk=1600;
        const string ListeningStatus="Слушаю по-русски. «Вперёд», «стоп», «щит», «перезарядка»… Справка: Esc → управление.";
        bool inputSuppressed;

        public void StartListening(string preferredDevice=null)
        {
            if(Requested) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var devices=Microphone.devices;
            if(devices.Length==0) { Status="Микрофон не найден. Подключи его и проверь разрешения Windows."; return; }
            string source=Path.Combine(Application.streamingAssetsPath,"Voice",ModelFolder);
            if(!Directory.Exists(source)) { Status="Русская модель не найдена: переустанови полную папку игры со StreamingAssets."; return; }
            Requested=true; Listening=false;
            Status="Загружаю русскую модель Vosk. Интернет и язык Windows не нужны…";
            microphone=Array.IndexOf(devices,preferredDevice)>=0 ? preferredDevice : devices[0];
            string cache=Path.Combine(Application.persistentDataPath,"Voice",ModelFolder);
            session=new VoiceSession(source,cache,Path.Combine(Application.dataPath,"Plugins","x86_64"));
            session.Start();
#else
            Status="Русский голос в этой версии поддерживается в Windows x64.";
#endif
        }

        public void StopListening()
        {
            Requested=false; Listening=false; MicrophoneLevel=0;
            if(recording)
            {
                if(!string.IsNullOrEmpty(microphone)) Microphone.End(microphone);
                Destroy(recording); recording=null;
            }
            microphoneBuffer=null; microphone=null; position=0;
            inputSuppressed=false;
            if(session!=null) { session.Dispose(); session=null; }
            Status="Русский голос выключен.";
        }

        void Update()
        {
            if(!Requested || session==null) return;
            if(session.Error!=null) { Fail("Голос: "+session.Error); return; }
            if(!session.Ready) return;
            if(!recording)
            {
                try
                {
                    recording=Microphone.Start(microphone,true,10,Rate);
                    if(!recording) throw new InvalidOperationException("Windows не разрешила открыть микрофон.");
                    if(recording.frequency!=Rate) throw new InvalidOperationException("Нужен микрофон с частотой записи 16000 Гц.");
                    microphoneBuffer=new float[Chunk*recording.channels]; position=0;
                    microphoneStarted=Time.unscaledTime;
                    Status=ListeningStatus;
                }
                catch(Exception exception) { Fail("Не удалось открыть микрофон: "+exception.Message); return; }
            }
            int written=Microphone.GetPosition(microphone);
            if(written<0 || (!Microphone.IsRecording(microphone) && Time.unscaledTime-microphoneStarted>2))
            { Fail("Микрофон перестал записывать. Проверь подключение и доступ Windows."); return; }
            if(written>0) Listening=true;
            else if(!Listening && Time.unscaledTime-microphoneStarted>5)
            { Fail("Микрофон не отдаёт звук. Выбери другой микрофон в управлении и включи F7 снова."); return; }
            bool suppressed=SuppressInput?.Invoke()==true;
            if(suppressed!=inputSuppressed)
            {
                inputSuppressed=suppressed;
                position=written;
                session.SetSuppressed(suppressed);
                Status=suppressed ? "Идёт реплика. Слушаю после неё." : ListeningStatus;
            }
            if(suppressed)
            {
                // Keep the microphone running, but skip playback-contaminated samples.
                position=written;
                MicrophoneLevel=0;
                return;
            }
            int available=(written-position+recording.samples)%recording.samples;
            int iterations=0;
            while(available>=Chunk && iterations++<6)
            {
                if(!recording.GetData(microphoneBuffer,position)) { Fail("Не удалось прочитать запись микрофона."); return; }
                short[] pcm=Downmix(microphoneBuffer,recording.channels,out float peak);
                MicrophoneLevel=Mathf.Lerp(MicrophoneLevel,peak,.45f);
                if(!session.Enqueue(pcm)) { Fail("Распознавание не успевает за звуком. Закрой тяжёлые приложения и включи F7 снова."); return; }
                position=(position+Chunk)%recording.samples; available-=Chunk;
            }
            while(session.TryReadResult(out string json))
            {
                if(!TryCommand(json,out string phrase)) continue;
                Status="Услышано: «"+phrase+"». Русская модель работает локально.";
                Phrase?.Invoke(phrase);
            }
        }

        public static short[] Downmix(float[] input,int channels,out float peak)
        {
            channels=Math.Max(1,channels); var pcm=new short[input.Length/channels]; peak=0;
            for(int frame=0;frame<pcm.Length;frame++)
            {
                float sample=0;
                for(int channel=0;channel<channels;channel++) sample+=input[frame*channels+channel];
                sample=Math.Max(-1,Math.Min(1,sample/channels)); peak=Math.Max(peak,Math.Abs(sample));
                pcm[frame]=(short)Math.Round(sample*32767);
            }
            return pcm;
        }

        [Serializable] sealed class Word { public float conf; public string word; }
        [Serializable] sealed class Result { public string text; public Word[] result; }
        public static bool TryCommand(string json,out string phrase)
        {
            phrase=null;
            try
            {
                var result=JsonUtility.FromJson<Result>(json);
                if(result==null || string.IsNullOrWhiteSpace(result.text) || result.result==null || result.result.Length==0) return false;
                string text=SpokenCommandCatalog.Normalize(result.text);
                if(text.Contains("[unk]")) return false;
                float confidence=0;
                foreach(var word in result.result) confidence+=word.conf;
                if(confidence/result.result.Length<.9f) return false;
                if(!SpokenCommandCatalog.TryResolve(text,out _)) return false;
                phrase=text; return true;
            }
            catch(Exception) { return false; }
        }

        void Fail(string reason) { StopListening(); Status=reason; }
        void OnDisable() { StopListening(); }
        void OnDestroy() { StopListening(); }

        sealed class VoiceSession : IDisposable
        {
            readonly string source,cache,native;
            readonly ConcurrentQueue<(int generation,short[] samples)> input=new();
            readonly ConcurrentQueue<(int generation,string json)> results=new();
            readonly AutoResetEvent wake=new(false);
            Thread thread;
            volatile bool cancelled;
            volatile bool suppressed;
            public volatile bool Ready;
            public volatile string Error;
            int queued,generation;
            public VoiceSession(string source,string cache,string native) { this.source=source; this.cache=cache; this.native=native; }
            public void Start() { thread=new Thread(Run) { IsBackground=true,Name="Doppelganger Russian Voice" }; thread.Start(); }
            public bool Enqueue(short[] pcm)
            {
                if(cancelled) return false;
                if(Interlocked.Increment(ref queued)>20) { Interlocked.Decrement(ref queued); return false; }
                input.Enqueue((Volatile.Read(ref generation),pcm)); wake.Set(); return true;
            }
            public void SetSuppressed(bool value)
            {
                suppressed=value;
                Interlocked.Increment(ref generation);
                while(input.TryDequeue(out _)) Interlocked.Decrement(ref queued);
                while(results.TryDequeue(out _)) { }
            }
            public bool TryReadResult(out string json)
            {
                while(results.TryDequeue(out var result))
                {
                    if(suppressed || result.generation!=Volatile.Read(ref generation)) continue;
                    json=result.json;
                    return true;
                }
                json=null;
                return false;
            }
            void Run()
            {
                IntPtr model=IntPtr.Zero,recognizer=IntPtr.Zero;
                try
                {
                    string modelPath=CacheModel(source,cache);
                    if(cancelled) return;
                    VoskNative.EnsureLibraries(native);
                    VoskNative.vosk_set_log_level(-1);
                    model=VoskNative.vosk_model_new(VoskNative.Utf8(modelPath));
                    if(model==IntPtr.Zero) throw new InvalidOperationException("Не удалось загрузить русскую модель Vosk.");
                    if(cancelled) return;
                    recognizer=VoskNative.vosk_recognizer_new_grm(model,Rate,VoskNative.Utf8(Grammar));
                    if(recognizer==IntPtr.Zero) throw new InvalidOperationException("Не удалось создать русский распознаватель.");
                    VoskNative.vosk_recognizer_set_words(recognizer,1); Ready=true;
                    int decoderGeneration=0;
                    while(!cancelled)
                    {
                        if(!input.TryDequeue(out var chunk)) { wake.WaitOne(100); continue; }
                        Interlocked.Decrement(ref queued);
                        if(suppressed || chunk.generation!=Volatile.Read(ref generation)) continue;
                        if(decoderGeneration!=chunk.generation)
                        {
                            VoskNative.vosk_recognizer_reset(recognizer);
                            decoderGeneration=chunk.generation;
                        }
                        int result=VoskNative.vosk_recognizer_accept_waveform_s(recognizer,chunk.samples,chunk.samples.Length);
                        if(result<0) throw new InvalidOperationException("Ошибка декодирования звука.");
                        if(result>0 && !suppressed && chunk.generation==Volatile.Read(ref generation))
                            results.Enqueue((chunk.generation,VoskNative.ReadUtf8(VoskNative.vosk_recognizer_result(recognizer))));
                    }
                }
                catch(Exception exception) { Error=exception.GetType().Name+": "+exception.Message; }
                finally
                {
                    Ready=false;
                    if(recognizer!=IntPtr.Zero) VoskNative.vosk_recognizer_free(recognizer);
                    if(model!=IntPtr.Zero) VoskNative.vosk_model_free(model);
                    wake.Dispose();
                }
            }
            public void Dispose()
            {
                cancelled=true;
                try { wake.Set(); } catch(ObjectDisposedException) { }
                // Native loading cannot be interrupted. The background thread owns and
                // releases its handles even when the scene was unloaded during loading.
                if(thread!=null && thread.IsAlive) thread.Join(100);
            }
        }

        public static string CacheModel(string source,string destination)
        {
            // Kaldi's Windows file reader needs a simple local path. Managed copying
            // also supports this project's Cyrillic OneDrive installation path.
            foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
            {
                if(file.EndsWith(".meta",StringComparison.OrdinalIgnoreCase)) continue;
                string target=Path.Combine(destination,file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
                var info=new FileInfo(file);
                if(File.Exists(target) && new FileInfo(target).Length==info.Length) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file,target,true);
            }
            return VoskNative.NativeModelPath(destination);
        }
    }

    // Minimal binding to the official Apache-2.0 Vosk C API, Windows x64 build 0.3.45.
    public static class VoskNative
    {
        static readonly object LibraryLock=new();
        static bool loaded;
        public static void EnsureLibraries(string directory)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            lock(LibraryLock)
            {
                if(loaded) return;
                foreach(string name in new[]{"libgcc_s_seh-1.dll","libwinpthread-1.dll","libstdc++-6.dll","libvosk.dll"})
                {
                    string path=Path.Combine(directory,name);
                    if(!File.Exists(path)) throw new FileNotFoundException("Не найдена библиотека голоса "+name+". Скопируй всю папку игры.",path);
                    if(LoadLibraryEx(path,IntPtr.Zero,8)==IntPtr.Zero)
                        throw new InvalidOperationException("Не удалось открыть "+name+" (Windows "+Marshal.GetLastWin32Error()+").");
                }
                loaded=true;
            }
#else
            throw new PlatformNotSupportedException("Vosk включён для Windows x64.");
#endif
        }
        public static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value+"\0");
        public static string NativeModelPath(string path)
        {
            bool simple=true; foreach(char character in path) if(character>127) { simple=false; break; }
            if(simple) return path;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var shortened=new StringBuilder(4096);
            if(GetShortPathName(path,shortened,shortened.Capacity)>0)
            {
                string candidate=shortened.ToString(); simple=true;
                foreach(char character in candidate) if(character>127) { simple=false; break; }
                if(simple) return candidate;
            }
#endif
            throw new InvalidOperationException("Путь к локальному кэшу голосовой модели должен содержать латиницу. На этом Windows-профиле короткие имена путей недоступны.");
        }
        public static string ReadUtf8(IntPtr pointer)
        {
            if(pointer==IntPtr.Zero) return "";
            int length=0; while(Marshal.ReadByte(pointer,length)!=0) length++;
            var bytes=new byte[length]; Marshal.Copy(pointer,bytes,0,length); return Encoding.UTF8.GetString(bytes);
        }
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string file,IntPtr reserved,uint flags);
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetShortPathName(string path,StringBuilder shortened,int length);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern void vosk_set_log_level(int level);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr vosk_model_new(byte[] path);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern void vosk_model_free(IntPtr model);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr vosk_recognizer_new_grm(IntPtr model,float rate,byte[] grammar);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern void vosk_recognizer_set_words(IntPtr recognizer,int enabled);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern int vosk_recognizer_accept_waveform_s(IntPtr recognizer,short[] data,int length);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr vosk_recognizer_result(IntPtr recognizer);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr vosk_recognizer_final_result(IntPtr recognizer);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern void vosk_recognizer_reset(IntPtr recognizer);
        [DllImport("libvosk",CallingConvention=CallingConvention.Cdecl)] public static extern void vosk_recognizer_free(IntPtr recognizer);
    }
}
