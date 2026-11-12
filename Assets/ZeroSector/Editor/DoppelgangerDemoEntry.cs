using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // One public entry point; builders and retired prototypes are not launch choices.
    public static class DoppelgangerDemoEntry
    {
        [MenuItem("Двойник/1. Запустить игру", false, 0)]
        public static void Play()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0]);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Двойник/2. Папка готовой игры", false, 1)]
        static void BuildFolder() => EditorUtility.RevealInFinder(Path.GetFullPath(CampaignDemoBuild.CurrentOutput+"/Doppelganger.exe"));

        [MenuItem("Двойник/3. Материалы для защиты", false, 2)]
        static void Documents() => EditorUtility.RevealInFinder(Path.GetFullPath("../Deliverables/Lab08/Двойник Лабораторная 8.docx"));

        [MenuItem("Двойник/Разработка/Открыть 01 — Мостик", false, 30)]
        static void Bridge() => Open(0);
        [MenuItem("Двойник/Разработка/Открыть 02 — Инженерный отсек", false, 31)]
        static void Engineering() => Open(1);
        [MenuItem("Двойник/Разработка/Открыть 03 — Ядро", false, 32)]
        static void Core() => Open(2);
        [MenuItem("Двойник/Разработка/Пересобрать Windows", false, 40)]
        static void Build() => CampaignDemoBuild.BuildAnimations();

        static void Open(int chapter)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(CampaignGame.ScenePaths[chapter]);
        }

        public static void PrepareForPresentation()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before cleaning up scenes.");
            string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            string archive=Path.Combine(project,"Diagnostics","ArchivedDemoCleanup",DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string current=SceneManager.GetActiveScene().path;
            if(SceneManager.GetActiveScene().isDirty)
            {
                if(string.IsNullOrEmpty(current)) throw new InvalidOperationException("Save the unnamed scene first.");
                if(!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not preserve the current scene.");
            }
            EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0]);
            var needed=AssetDatabase.GetDependencies(CampaignGame.ScenePaths,true).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var retired=AssetDatabase.FindAssets("t:Scene",new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path=>!CampaignGame.ScenePaths.Contains(path) && !needed.Contains(path)).ToArray();
            foreach(string asset in retired)
            {
                string source=Path.GetFullPath(Path.Combine(project,asset));
                string target=Path.GetFullPath(Path.Combine(archive,asset));
                if(!source.StartsWith(project+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ||
                   !target.StartsWith(archive+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Archive target escaped the project.");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(source,target);
                if(File.Exists(source+".meta")) File.Move(source+".meta",target+".meta");
            }
            EditorBuildSettings.scenes=CampaignGame.ScenePaths.Select(path=>new EditorBuildSettingsScene(path,true)).ToArray();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string path in CampaignGame.ScenePaths)
                if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) throw new InvalidOperationException("Campaign scene missing: "+path);
            Directory.CreateDirectory("Diagnostics/CampaignPreviews");
            File.WriteAllText("Diagnostics/CampaignPreviews/presentation-cleanup.txt",
                "RESULT: PASSED\nOpened: "+CampaignGame.ScenePaths[0]+"\nOnly three campaign scenes remain in Assets and build settings.\nRetired scenes preserved outside Assets: "+archive+"\n"+string.Join("\n",retired));
            Debug.Log("Двойник готов к показу. Меню: Двойник → 1. Запустить игру. Старые сцены убраны в архив.");
        }
    }
}
