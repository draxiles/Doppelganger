using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroSector.Learning;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // Local project authoring jobs only; no shell commands, arbitrary methods or runtime builds.
    [InitializeOnLoad]
    public static class DoppelgangerAuthoringQueue
    {
        const string Folder="Temp/DoppelgangerChecks";
        const string Command=Folder+"/authoring-command.txt";
        static double next;
        static DoppelgangerAuthoringQueue() { EditorApplication.update+=Tick; }
        [Serializable] sealed class State
        {
            public bool playing,compiling,updating,hasWeapon,inspecting,campaign;
            public string scene,weapon;
            public int weaponCount;
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next) return;
            next=EditorApplication.timeSinceStartup+1;
            Directory.CreateDirectory(Folder);
            var arena=UnityEngine.Object.FindAnyObjectByType<ImitationArena>();
            var mission=UnityEngine.Object.FindAnyObjectByType<CampaignGame>();
            var view=mission ? mission.presentation : arena ? arena.GetComponent<FirstPersonPresentation>() : UnityEngine.Object.FindAnyObjectByType<FirstPersonPresentation>();
            File.WriteAllText(Folder+"/editor-state.json",JsonUtility.ToJson(new State
            {
                playing=EditorApplication.isPlaying,compiling=EditorApplication.isCompiling,updating=EditorApplication.isUpdating,
                scene=SceneManager.GetActiveScene().path,hasWeapon=view && view.HasWeapon,campaign=mission,
                inspecting=view && view.IsInspecting,weapon=view ? view.WeaponTitle : "",weaponCount=view ? view.weapons.Count : 0
            }));
            if(!File.Exists(Command) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || CampaignReviewPass.IsRunning || CampaignTrailerCapture.IsRunning) return;
            string action=File.ReadAllText(Command).Trim(); File.Delete(Command);
            try
            {
                switch(action)
                {
                    case "refresh": AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); break;
                    case "prepare-demo": DoppelgangerDemoEntry.PrepareForPresentation(); break;
                    case "art":
                        if(mission) throw new InvalidOperationException("This is a playable campaign scene. Use the campaign builder instead of the old arena art pass.");
                        if(SceneManager.GetActiveScene().isDirty && !SceneManager.GetActiveScene().path.StartsWith("Assets/DoppelgangerGenerated/Campaign/"))
                            throw new InvalidOperationException("Save the current scene before the art job.");
                        CampaignArtPass.Apply(); break;
                    case "previews": CampaignReviewPass.Start(); break;
                    case "presentation-check": CampaignPresentationCheck.Start(); break;
                    case "missions": AdamRobotAuthoring.Prepare(); CampaignMissionBuilder.Build(); break;
                    case "adam-art": AdamRobotAuthoring.Prepare(); break;
                    case "open-bridge": UnityEditor.SceneManagement.EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0]); break;
                    case "weapons-check": CampaignWeaponCheck.Start(); break;
                    case "campaign-check": CampaignIntegrationCheck.Start(); break;
                    case "robot-check": CampaignRobotCheck.Start(); break;
                    case "voice-check": RussianVoiceCheck.Run(); break;
                    case "devices-check": MultimodalControlsCheck.Run(); break;
                    case "commands-check": GameCommandsCheck.Run(); break;
                    case "hands-check": HandGestureCheck.Run(); break;
                    case "shield-check": CommanderShieldCheck.Run(); break;
                    case "trailer": CampaignTrailerCapture.Start(); break;
                    case "windows-build": CampaignDemoBuild.Build(); break;
                    case "windows-build-animations": CampaignDemoBuild.BuildAnimations(); break;
                    default: throw new InvalidOperationException("Unknown project authoring job: "+action);
                }
                File.WriteAllText(Folder+"/authoring-result.txt",action+": completed");
            }
            catch(Exception exception) { File.WriteAllText(Folder+"/authoring-result.txt",action+": "+exception); Debug.LogException(exception); }
        }
    }
}
