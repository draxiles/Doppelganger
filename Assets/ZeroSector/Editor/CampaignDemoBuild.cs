using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    public static class CampaignDemoBuild
    {
        public const string CurrentOutput="Builds/SignalAlex";
        public static void Build() => BuildTo(CurrentOutput);

        public static void BuildAnimations() => BuildTo(CurrentOutput);

        static void BuildTo(string output)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before building the demo.");
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before building the demo.");
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Install Windows Build Support in Unity Hub.");
            if(!CampaignGame.ScenePaths.All(File.Exists)) throw new InvalidOperationException("Build the three campaign scenes first.");
            foreach(string name in new[]{"EmergencyBattery","BatteryDock","Salvage","Exterior"})
                if(!Resources.Load<GameObject>("Doppelganger/"+name))
                    throw new InvalidOperationException("Missing authored prefab "+name+"; run CampaignPolishAuthoring.Prepare first.");
            if(!Resources.Load<Texture2D>("Doppelganger/Logo") || !Resources.Load<AudioClip>("Doppelganger/Radio/EarthReply"))
                throw new InvalidOperationException("Operation Signal requires the original logo and Russian Earth transmission.");
            int radioClips=0;
            foreach(string name in CampaignRadio.RequiredClipNames())
            {
                var clip=Resources.Load<AudioClip>("Doppelganger/Radio/"+name);
                if(!clip || clip.samples<=0 || clip.length<.3f)
                    throw new InvalidOperationException("Missing or empty campaign voice recording: "+name);
                radioClips++;
            }
            if(!Resources.Load<GameObject>(CommanderAvatar.ResourcePath))
                throw new InvalidOperationException("The shared full-body commander outfit is missing.");
            var bulletMark=Resources.Load<Material>(CampaignBulletMarks.MaterialPath);
            if(!bulletMark || !bulletMark.shader || bulletMark.shader.name!="Doppelganger/Bullet Mark")
                throw new InvalidOperationException("The bullet-mark projection material is missing.");
            if(!Resources.Load<GameObject>(CampaignPrologue.ResourcePath) ||
                !Resources.Load<Texture2D>("Doppelganger/Interface/SignalKeyArt") ||
                !Resources.Load<Font>("Doppelganger/Interface/GolosText-Regular"))
                throw new InvalidOperationException("The impact prologue or the new interface assets are missing.");
            foreach(string motion in new[]{"Idle","Run","StrafeRight","StrafeLeft","Backpedal","Sprint","Aim","Fire"})
                if(!Resources.Load<AnimationClip>("Doppelganger/CombatMotion/"+motion))
                    throw new InvalidOperationException("Missing authored humanoid combat animation: "+motion);
            PlayerSettings.productName="Doppelganger";
            PlayerSettings.companyName="Doppelganger Student Project";
            PlayerSettings.defaultScreenWidth=1600; PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.resizableWindow=true; PlayerSettings.runInBackground=false;
            Directory.CreateDirectory(output);
            File.WriteAllText("Diagnostics/CampaignPreviews/windows-build.txt","Building Windows x64 demo...\n");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes=CampaignGame.ScenePaths,locationPathName=output+"/Doppelganger.exe",
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.None
            });
            File.WriteAllText("Diagnostics/CampaignPreviews/windows-build.txt",
                "Result: "+report.summary.result+"\nOutput: "+Path.GetFullPath(output+"/Doppelganger.exe")+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+
                "\nBytes: "+report.summary.totalSize+"\nTime: "+report.summary.totalTime+"\n");
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Windows demo build failed. See windows-build.txt and Editor.log.");
            File.AppendAllText("Diagnostics/CampaignPreviews/windows-build.txt",$"PASS: {radioClips} recorded campaign radio lines and command replies.\n");
            File.WriteAllText(output+"/READ_ME.txt",
                "ДВОЙНИК / ОПЕРАЦИЯ «СИГНАЛ»\n\nЗапуск: Doppelganger.exe\nМостик → пробитый шлюз → грузовая палуба → инженерный отсек → ядро → внешняя антенна.\n"+
                "WASD/мышь; ЛКМ огонь; ПКМ прицел; R перезарядка; Q/колесо смена оружия; X пистолет; F осмотр; C/Ctrl присед; Shift бег; E действие.\n"+
                "G — положить аккумулятор, Tab — пропустить текущую реплику. У пульта W/S — тангаж, A/D — рыскание, Q/E — крен, Пробел — импульс после захвата.\n"+
                "F7 — русский голос: движение, бег, щит, оружие, перезарядка, осмотр и другие команды.\n"+
                "На мостике скажи «Гера, проверь связь» для голосовой диагностики.\n"+
                "Новая операция открывается кат-сценой, затем появляется брифинг. Enter — начать. F1 — снова открыть порядок прохождения.\n"+
                "Прицел показывает текущий разброс. Короткие очереди и прицеливание на ПКМ помогают попадать точнее.\n"+
                "F8 — встроенная камера. F9 — калибровка по положению прямо. WASD следует текущему взгляду.\n"+
                "На мостике один указательный с согнутым большим — автомат, два пальца с согнутым большим — пистолет, кулак — щит.\n"+
                "ADAM сам ищет позицию и атакует. В фазе отражения он повторяет движение, присед и выстрел через 0,5 с.\n"+
                "Esc -> Управление и устройства -> вкладки Клавиатура / Голос / Жесты. Там выбор устройств и живая камера.\n"+
                "Голосовое движение — до3с или команды стоп. Движение жестом — пока рука в кадре; WASD имеет приоритет.\n"+
                "Подробные команды и порядок прохождения: УПРАВЛЕНИЕ.txt.\n"+
                "Сохраняй всю папку игры: exe, Doppelganger_Data и остальные файлы должны лежать вместе.\n\n"+
                "ADAM Character Pack — Restricted Asset: только личное/некоммерческое использование.\n"+
                "Unity FPS Sample art — Unity Companion License. Earth textures — Solar System Scope, CC BY 4.0.\n"+
                "https://www.solarsystemscope.com/textures/\n");
            if(File.Exists("../НАЧАТЬ ЗДЕСЬ.txt")) File.Copy("../НАЧАТЬ ЗДЕСЬ.txt",output+"/УПРАВЛЕНИЕ.txt",true);
            const string handWorker="Assets/StreamingAssets/HandGestures/hand-worker.zip";
            string builtWorker=output+"/Doppelganger_Data/StreamingAssets/HandGestures/hand-worker.zip";
            if(!File.Exists(handWorker) || !File.Exists(builtWorker) || new FileInfo(handWorker).Length!=new FileInfo(builtWorker).Length)
                throw new InvalidOperationException("Offline hand gesture worker missing from built player.");
            using(var workerArchive=System.IO.Compression.ZipFile.OpenRead(builtWorker))
                if(!workerArchive.Entries.Any(entry=>entry.Name=="face_landmarker.task"))
                    throw new InvalidOperationException("The bridge face model was not included in the Windows player.");
            File.AppendAllText("Diagnostics/CampaignPreviews/windows-build.txt","PASS: offline MediaPipe hand and face models and self-contained worker included in player.\n");
            if(File.Exists("THIRD_PARTY_NOTICES.md")) File.Copy("THIRD_PARTY_NOTICES.md",output+"/THIRD_PARTY_NOTICES.md",true);
            File.Copy("Assets/Resources/Doppelganger/Interface/Golos-OFL.txt",output+"/Golos-OFL.txt",true);
            string voicePath=output+"/Doppelganger_Data/StreamingAssets/Voice/"+RussianVoiceRecognizer.ModelFolder;
            foreach(string file in new[]{"am/final.mdl","conf/model.conf","graph/HCLr.fst","graph/Gr.fst"})
                if(!File.Exists(voicePath+"/"+file)) throw new InvalidOperationException("Russian voice model missing from built player: "+file);
            foreach(string file in new[]{"libgcc_s_seh-1.dll","libwinpthread-1.dll","libstdc++-6.dll","libvosk.dll"})
                if(!File.Exists(output+"/Doppelganger_Data/Plugins/x86_64/"+file)) throw new InvalidOperationException("Russian voice native dependency missing from built player: "+file);
            File.AppendAllText("Diagnostics/CampaignPreviews/windows-build.txt","PASS: Russian offline model and all four native DLLs included in player.\n");
            Debug.Log("Windows demo built: "+Path.GetFullPath(output+"/Doppelganger.exe"));
        }
    }
}
