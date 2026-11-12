using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // Uses generated local speech files rather than opening the user's microphone.
    public static class RussianVoiceCheck
    {
        [Serializable] sealed class SpeechCase { public string file,phrase,action; }
        [Serializable] sealed class SpeechCases { public SpeechCase[] cases; }
        public static void Run()
        {
            const string report="Diagnostics/CampaignPreviews/russian-voice-check.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            var log=new StringBuilder("Native offline Russian ASR / no physical microphone\n");
            IntPtr model=IntPtr.Zero,recognizer=IntPtr.Zero;
            try
            {
                string path=RussianVoiceRecognizer.CacheModel(
                    Path.Combine(Application.streamingAssetsPath,"Voice",RussianVoiceRecognizer.ModelFolder),
                    Path.Combine(Application.persistentDataPath,"Voice",RussianVoiceRecognizer.ModelFolder));
                VoskNative.EnsureLibraries(Path.Combine(Application.dataPath,"Plugins","x86_64"));
                VoskNative.vosk_set_log_level(-1);
                model=VoskNative.vosk_model_new(VoskNative.Utf8(path));
                Require(model!=IntPtr.Zero,"Native model loaded from cache");
                recognizer=VoskNative.vosk_recognizer_new_grm(model,16000,VoskNative.Utf8(RussianVoiceRecognizer.Grammar));
                Require(recognizer!=IntPtr.Zero,"Russian grammar recognizer initialized");
                VoskNative.vosk_recognizer_set_words(recognizer,1);
                string manifest=File.ReadAllText("ThirdPartyStaging/VoskRussian/TestAudio/expanded-manifest.json").TrimStart('\uFEFF');
                var cases=JsonUtility.FromJson<SpeechCases>("{\"cases\":"+manifest+"}").cases;
                Require(cases!=null && cases.Length>=40,"Expanded real Russian speech fixtures exist");
                var covered=new HashSet<string>();
                foreach(var speech in cases)
                {
                    short[] pcm=ReadWave("ThirdPartyStaging/VoskRussian/TestAudio/"+speech.file);
                    var padded=new short[pcm.Length+40000]; Array.Copy(pcm,0,padded,8000,pcm.Length);
                    var commands=Decode(recognizer,padded,log);
                    bool matched=commands.Count==1 && SpokenCommandCatalog.TryResolve(commands[0],out string action) && action==speech.action;
                    Require(matched,$"Russian WAV «{speech.phrase}»: expected {speech.action}, got {string.Join(", ",commands)}");
                    covered.Add(speech.action);
                }
                foreach(var entry in SpokenCommandCatalog.Entries)
                {
                    Require(covered.Contains(entry.Action),"Real Russian speech covers "+entry.Action);
                    foreach(string alias in entry.Phrases)
                        Require(SpokenCommandCatalog.TryResolve(alias.ToUpperInvariant(),out string action) && action==entry.Action,"Catalog alias: "+alias);
                }
                for(int index=8;index<=9;index++)
                {
                    short[] pcm=ReadWave($"ThirdPartyStaging/VoskRussian/TestAudio/phrase-{index:00}.wav");
                    var padded=new short[pcm.Length+40000]; Array.Copy(pcm,0,padded,8000,pcm.Length);
                    Require(Decode(recognizer,padded,log).Count==0,"Unrelated actual Russian speech rejected: "+index);
                }
                Require(Decode(recognizer,new short[48000],log).Count==0,"Silence rejected");
                var noise=new short[48000]; var random=new System.Random(42);
                for(int i=0;i<noise.Length;i++) noise[i]=(short)random.Next(-90,91);
                Require(Decode(recognizer,noise,log).Count==0,"Quiet noise rejected");
                Require(!RussianVoiceRecognizer.TryCommand("{\"text\":\"щит\",\"result\":[{\"word\":\"щит\",\"conf\":0.84}]}",out _),"Low-confidence forced grammar match rejected");
                Require(!RussianVoiceRecognizer.TryCommand("{\"text\":\"[unk]\",\"result\":[{\"word\":\"[unk]\",\"conf\":1}]}",out _),"Unknown speech rejected");
                Require(!RussianVoiceRecognizer.TryCommand("not json",out _),"Malformed output rejected");
                short[] mixed=RussianVoiceRecognizer.Downmix(new[]{1f,-1f,.5f,.5f},2,out _);
                Require(mixed.Length==2 && mixed[0]==0 && Math.Abs(mixed[1]-16384)<=1,"Microphone stereo conversion is mono PCM16");
                log.AppendLine("RESULT: PASSED"); File.WriteAllText(report,log.ToString());
                Debug.Log("Russian voice offline native check passed. No microphone was activated.");
            }
            catch(Exception exception)
            {
                log.AppendLine("RESULT: FAILED\n"+exception); File.WriteAllText(report,log.ToString()); throw;
            }
            finally
            {
                if(recognizer!=IntPtr.Zero) VoskNative.vosk_recognizer_free(recognizer);
                if(model!=IntPtr.Zero) VoskNative.vosk_model_free(model);
            }
            void Require(bool condition,string label)
            { log.AppendLine((condition ? "PASS " : "FAIL ")+label); if(!condition) throw new InvalidOperationException(label); }
        }

        static List<string> Decode(IntPtr recognizer,short[] pcm,StringBuilder log)
        {
            VoskNative.vosk_recognizer_reset(recognizer); var commands=new List<string>();
            for(int offset=0;offset<pcm.Length;offset+=1600)
            {
                int length=Math.Min(1600,pcm.Length-offset); var block=new short[length]; Array.Copy(pcm,offset,block,0,length);
                int result=VoskNative.vosk_recognizer_accept_waveform_s(recognizer,block,length);
                if(result<0) throw new InvalidOperationException("Native waveform decoding failed");
                if(result>0) Add(VoskNative.vosk_recognizer_result(recognizer));
            }
            Add(VoskNative.vosk_recognizer_final_result(recognizer)); return commands;
            void Add(IntPtr pointer)
            {
                string json=VoskNative.ReadUtf8(pointer);
                log.AppendLine(json.Replace('\n',' '));
                if(RussianVoiceRecognizer.TryCommand(json,out string command)) commands.Add(command);
            }
        }

        static short[] ReadWave(string path)
        {
            using var reader=new BinaryReader(File.OpenRead(path));
            if(new string(reader.ReadChars(4))!="RIFF") throw new InvalidDataException("Not a WAV");
            reader.ReadInt32(); reader.ReadChars(4);
            while(reader.BaseStream.Position+8<=reader.BaseStream.Length)
            {
                string chunk=new(reader.ReadChars(4)); int bytes=reader.ReadInt32(); long next=reader.BaseStream.Position+bytes+(bytes&1);
                if(chunk=="fmt ")
                {
                    short format=reader.ReadInt16(),channels=reader.ReadInt16(); int rate=reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt16(); short bits=reader.ReadInt16();
                    if(format!=1 || channels!=1 || rate!=16000 || bits!=16) throw new InvalidDataException("Expected PCM16 mono 16000 Hz");
                }
                else if(chunk=="data")
                {
                    var pcm=new short[bytes/2]; for(int i=0;i<pcm.Length;i++) pcm[i]=reader.ReadInt16(); return pcm;
                }
                reader.BaseStream.Position=next;
            }
            throw new InvalidDataException("WAV data missing");
        }
    }
}
