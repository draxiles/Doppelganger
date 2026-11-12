using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ZeroSector.Learning;

namespace ZeroSector.Campaign
{
    public static class CampaignSession
    {
        [Serializable] public sealed class Salvage
        {
            public int number;
            public Vector3 position;
        }
        [Serializable] public sealed class SaveData
        {
            public int version=1,chapter,kills,shots,hits,crouches,samples,underFire,credits,wins;
            public float seconds;
            public string[] flags=Array.Empty<string>();
            public Vector3 checkpoint;
            public bool hasCheckpoint,finished,alternateColor;
            public bool snapshot,cargoHeld,outside;
            public float yaw,pitch,health,shield,oxygen;
            public int energy,equipped;
            public int[] magazines,reserves;
            public Salvage[] salvage=Array.Empty<Salvage>();
            public TacticalPolicy.PolicyData policy;
        }
        public static SaveData Data { get; private set; }=new();
        public static TacticalPolicy Policy { get; private set; }=new();
        public static readonly List<TacticalPolicy.Example> Replay=new();
        public static readonly HashSet<string> Flags=new();
        static readonly System.Random random=new(2187);
        public static string Error { get; private set; }
#if UNITY_EDITOR
        public static string EditorTestSavePath { get; set; }
#endif
        public static string SavePath
        {
            get
            {
#if UNITY_EDITOR
                if(!string.IsNullOrEmpty(EditorTestSavePath)) return EditorTestSavePath;
#endif
                return Path.Combine(Application.persistentDataPath,"DoppelgangerCampaign","campaign.json");
            }
        }
        public static bool HasSave => File.Exists(SavePath) || File.Exists(SavePath+".bak");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { NewGame(); }
        public static void NewGame()
        {
            Data=new SaveData(); Policy=new TacticalPolicy(); Replay.Clear(); Flags.Clear(); Error=null;
        }
        public static bool Has(string flag) => Flags.Contains(Data.chapter+"/"+flag);
        public static bool Mark(string flag) => Flags.Add(Data.chapter+"/"+flag);
        public static void Learn(float[] state,int move,bool fire,int power,bool crouch)
        {
            var example=new TacticalPolicy.Example(state,move,fire,power,crouch);
            if(Replay.Count>=1600) Replay.RemoveAt(0);
            Replay.Add(example); Data.samples++;
            if(state[10]>0) Data.underFire++;
            Policy.Train(example);
            for(int i=0;i<9;i++)
            {
                int first=random.NextDouble()<.75 ? Math.Max(0,Replay.Count-100) : 0;
                Policy.Train(Replay[random.Next(first,Replay.Count)]);
            }
        }
        public static void Save()
        {
            try
            {
                Data.flags=new List<string>(Flags).ToArray(); Data.policy=Policy.Export();
                string directory=Path.GetDirectoryName(SavePath); Directory.CreateDirectory(directory);
                string temporary=SavePath+".tmp";
                File.WriteAllText(temporary,JsonUtility.ToJson(Data,true));
                if(File.Exists(SavePath)) File.Replace(temporary,SavePath,SavePath+".bak");
                else File.Move(temporary,SavePath);
                Error=null;
            }
            catch(Exception exception) { Error="Не удалось сохранить: "+exception.Message; Debug.LogWarning(Error); }
        }
        public static bool Load(int expectedChapter=-1)
        {
            try
            {
                SaveData loaded; TacticalPolicy model;
                try { (loaded,model)=ReadSnapshot(SavePath); }
                catch(Exception) when(File.Exists(SavePath+".bak")) { (loaded,model)=ReadSnapshot(SavePath+".bak"); }
                if(expectedChapter>=0 && loaded.chapter!=expectedChapter) throw new InvalidDataException("Контрольная точка относится к другому эпизоду.");
                Data=loaded; Policy=model; Flags.Clear();
                foreach(string flag in loaded.flags ?? Array.Empty<string>()) Flags.Add(flag);
                Replay.Clear(); Error=null; return true;
            }
            catch(Exception exception) { Error="Сохранение не загружено: "+exception.Message; return false; }
        }
        static (SaveData,TacticalPolicy) ReadSnapshot(string path)
        {
            var loaded=JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            if(loaded==null || loaded.version!=1 || loaded.chapter<0 || loaded.chapter>2 ||
                (loaded.hasCheckpoint && !Finite(loaded.checkpoint)) ||
                (loaded.snapshot && (!float.IsFinite(loaded.yaw) || !float.IsFinite(loaded.pitch) || !float.IsFinite(loaded.health)
                    || !float.IsFinite(loaded.shield) || !float.IsFinite(loaded.oxygen))))
                throw new InvalidDataException("Неверный формат контрольной точки.");
            var model=new TacticalPolicy(); model.Import(loaded.policy);
            return (loaded,model);
        }
        public static bool Finite(Vector3 position) => float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z);
    }
}
