using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    /// <summary>Headless Windows verification; never starts microphone/camera or uses the player's save.</summary>
    [InitializeOnLoad]
    public static class CampaignPolishPipeline
    {
        const string Key="Doppelganger.OperationSignal.Batch";
        const string WeaponCheckPending=Key+".WeaponCheck";
        const string Report="Diagnostics/CampaignPreviews/operation-signal-pipeline.txt";
        const string CampaignReport="Diagnostics/CampaignPreviews/campaign-check.txt";
        static CampaignPolishPipeline() { EditorApplication.update+=Tick; }

        public static void RunBoardingVisualChecks()
        {
            try
            {
                ImpactProloguePreviewCapture.CaptureExisting();
                BoardingRoutePreviewCapture.CaptureExisting();
                CampaignRobotCheck.RunBatch();
            }
            catch(Exception error)
            {
                UnityEngine.Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Report));
                File.WriteAllText(Report,"Authoring Signal: Impact, presentation, commander and combat motion.\n");
                EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0],OpenSceneMode.Single);
                CampaignPolishAuthoring.Prepare();
                CommanderAvatarAuthoring.Prepare();
                CombatMotionAuthoring.Prepare();
                BoardingRouteAuthoring.Prepare();
                ImpactPrologueAuthoring.Prepare();
                SignalPresentationAuthoring.Prepare();
                if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null)
                {
                    BoardingRoutePreviewCapture.CaptureExisting();
                    ImpactProloguePreviewCapture.CaptureExisting();
                }
                RussianVoiceCheck.Run();
                File.AppendAllText(Report,"Authoring completed. Checking campaign.\n");
                StartCampaignCheck();
            }
            catch(Exception error) { Finish(1,error.ToString()); }
        }

        public static void CheckAndBuild()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Report));
                File.WriteAllText(Report,"Checking the weapon rigs and the saved campaign scenes.\n");
                EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0],OpenSceneMode.Single);
                SessionState.SetBool(WeaponCheckPending,true);
                CampaignWeaponCheck.Start();
            }
            catch(Exception error) { Finish(1,error.ToString()); }
        }

        static void StartCampaignCheck()
        {
            EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0],OpenSceneMode.Single);
            SessionState.SetBool(Key,true);
            CampaignIntegrationCheck.Start();
        }

        static void Tick()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if(SessionState.GetBool(WeaponCheckPending,false))
            {
                if(CampaignWeaponCheck.IsRunning) return;
                SessionState.EraseBool(WeaponCheckPending);
                EditorApplication.delayCall+=()=>{
                    try
                    {
                        if(!CampaignWeaponCheck.Passed) throw new InvalidOperationException("Weapon integration failed. See weapon-check.txt.");
                        File.AppendAllText(Report,"Weapon check passed. Checking campaign.\n");
                        StartCampaignCheck();
                    }
                    catch(Exception error) { Finish(1,error.ToString()); }
                };
                return;
            }
            if(!SessionState.GetBool(Key,false) || SessionState.GetBool("Doppelganger.CampaignCheck",false)) return;
            SessionState.EraseBool(Key);
            EditorApplication.delayCall+=()=>{
                try
                {
                    if(!File.Exists(CampaignReport) || !File.ReadAllText(CampaignReport).Contains("RESULT: PASSED"))
                        throw new InvalidOperationException("Campaign integration failed. See "+CampaignReport);
                    EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0],OpenSceneMode.Single);
                    File.AppendAllText(Report,"Campaign passed. Building Windows player.\n");
                    CampaignDemoBuild.Build();
                    Finish(0,"RESULT: PASSED");
                }
                catch(Exception error) { Finish(1,error.ToString()); }
            };
        }
        static void Finish(int code,string detail)
        {
            SessionState.EraseBool(Key);
            SessionState.EraseBool(WeaponCheckPending);
            File.AppendAllText(Report,detail+"\n");
            EditorApplication.Exit(code);
        }
    }
}
