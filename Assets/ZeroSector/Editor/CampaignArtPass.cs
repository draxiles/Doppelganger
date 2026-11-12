using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroSector.Learning;

namespace ZeroSector.Editor
{
    public static class CampaignArtPass
    {
        const string Request="Temp/DoppelgangerChecks/campaign-art.request";
        [InitializeOnLoadMethod]
        static void Pending() { if(File.Exists(Request)) EditorApplication.delayCall+=Run; }
        static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.playModeStateChanged+=AfterPlay; return; }
            if(EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall+=Run; return; }
            if(!File.Exists(Request)) return;
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            File.Delete(Request);
            try { Apply(); }
            catch(Exception exception) { File.WriteAllText("Temp/DoppelgangerChecks/campaign-art-error.txt",exception.ToString()); Debug.LogException(exception); }
        }
        static void AfterPlay(PlayModeStateChange state)
        { if(state==PlayModeStateChange.EnteredEditMode) { EditorApplication.playModeStateChanged-=AfterPlay; EditorApplication.delayCall+=Run; } }

        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Сначала останови Play."); return; }
            EarthAuthoring.Prepare();
            FpsSampleArt.Prepare();
            string original=SceneManager.GetActiveScene().path;
            string[] scenes={"01_CommandBridge","02_Engineering","03_DoppelgangerCore"};
            string[] titles={"КОМАНДНЫЙ МОСТИК","ИНЖЕНЕРНЫЙ ОТСЕК","ЯДРО ДВОЙНИКА"};
            for(int i=0;i<scenes.Length;i++)
            {
                string path="Assets/DoppelgangerGenerated/Campaign/"+scenes[i]+".unity";
                var scene=SceneManager.GetSceneByPath(path);
                if(!scene.isLoaded) scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                else SceneManager.SetActiveScene(scene);
                CreepyDeckBuilder.RepairRoom(scene);
                var arena=UnityEngine.Object.FindAnyObjectByType<ImitationArena>();
                arena.locationName=titles[i];
                FpsSampleArt.Install(arena); EditorUtility.SetDirty(arena);
                if(i==0)
                {
                    CreepyDeckBuilder.RefineBridge(scene);
                    var earth=scene.GetRootGameObjects().FirstOrDefault(root=>root.name.StartsWith("Earth / satellite"));
                    if(!earth) earth=EarthAuthoring.Create(new Vector3(-28,-130,240),135);
                    earth.transform.position=new Vector3(-28,-130,240);
                }
                arena.eyes.farClipPlane=1500;
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            if(File.Exists(original)) EditorSceneManager.OpenScene(original,OpenSceneMode.Single);
            File.WriteAllText("Diagnostics/CampaignPreviews/art-result.txt","Three locations patched; source meshes and real PBR textures imported; Earth created.\n");
            Debug.Log("Двойник: помещения закрыты переборками, два новых FPS-оружия установлены, Земля обновлена. Q/колесо — смена оружия, F — осмотр.");
        }

        static void Bridge() { Open("01_CommandBridge"); }
        static void Engineering() { Open("02_Engineering"); }
        static void Core() { Open("03_DoppelgangerCore"); }
        static void Open(string name)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Сначала останови Play."); return; }
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene("Assets/DoppelgangerGenerated/Campaign/"+name+".unity",OpenSceneMode.Single);
        }
    }
}
