using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;
using Object=UnityEngine.Object;

namespace ZeroSector.Editor
{
    public static class ImpactProloguePreviewCapture
    {
        const string Output="Diagnostics/CampaignPreviews/Prologue";
        const string ExistingOutput="Diagnostics/CampaignPreviews/Ramming";

        public static void Run() => Run(true,false);

        public static void RunWithWindowsBuild() => Run(true,true);

        public static void RenderExisting() => Run(false,false);

        public static void CaptureExisting() => Capture(false,ExistingOutput);

        static void Run(bool author,bool buildPlayer)
        {
            int result=0;
            try
            {
                Capture(author,author ? Output : ExistingOutput);
                if(buildPlayer)
                {
                    EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0],OpenSceneMode.Single);
                    CampaignDemoBuild.Build();
                }
            }
            catch(Exception error)
            {
                result=1;
                Debug.LogException(error);
            }
            if(Application.isBatchMode) EditorApplication.Exit(result);
        }

        static void Capture(bool author,string output)
        {
            Directory.CreateDirectory(output);
            var setup=EditorSceneManager.GetSceneManagerSetup().Where(scene=>!string.IsNullOrEmpty(scene.path)).ToArray();
            Scene scratch=default;
            Scene lightingScene=default;
            UniversalRenderPipelineAsset pipeline=null;
            float shadowDistance=0;
            Light sun=null;
            try
            {
                for(int i=0;i<SceneManager.sceneCount;i++)
                    if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scenes before capturing previews.");
                EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0],OpenSceneMode.Single);
                if(author)
                {
                    ImpactPrologueAuthoring.Prepare();
                    CommanderAvatarAuthoring.PrepareAndPreview();
                }
                var game=Object.FindAnyObjectByType<CampaignGame>();
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPrologueAuthoring.PrefabPath);
                if(!game || !game.eyes || !prefab) throw new InvalidOperationException("Author the prologue prefab and bridge first.");
                scratch=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                var clone=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scratch);
                clone.transform.position=new Vector3(0,0,-10000);
                var stage=clone.GetComponent<CampaignPrologue>();
                if(!stage || !stage.Lens) throw new InvalidOperationException("Prologue lens is missing.");
                lightingScene=SceneManager.GetActiveScene();
                pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                shadowDistance=pipeline ? pipeline.shadowDistance : 0;
                sun=RenderSettings.sun;
                foreach(var shot in new[]{("01-orbit",3.75f),("02-hull",6.1f),("03-inbound",10.42f),
                    ("04-impact",11.23f),("05-latched",11.83f)})
                {
                    stage.SampleOrbit(shot.Item2);
                    Render(stage.Lens,output,shot.Item1);
                }
                stage.SampleBoarding(CampaignPrologue.BoardingAt+1.64f);
                if(!stage.BoardersVisible) throw new InvalidOperationException("The boarding shot is missing the assault squad.");
                if(RenderSettings.sun!=sun || (pipeline && !Mathf.Approximately(shadowDistance,pipeline.shadowDistance)))
                    throw new InvalidOperationException("The orbital lighting overrode the boarding scene.");
                Render(stage.Lens,output,"06-boarders");
                stage.PreviewBridge(game.eyes,CampaignPrologue.BridgeAt+1.9f);
                Render(stage.Lens,output,"07-bridge");
                File.WriteAllText(output+"/preview-check.txt","RESULT: PASSED\nSeven URP frames captured after key-art dissolve and across the cutter approach, impact, latch and boarding.\n");
            }
            catch(Exception error)
            {
                File.WriteAllText(output+"/preview-check.txt","RESULT: FAILED\n"+error+"\n");
                throw;
            }
            finally
            {
                if(pipeline) pipeline.shadowDistance=shadowDistance;
                if(lightingScene.IsValid() && SceneManager.GetActiveScene()==lightingScene) RenderSettings.sun=sun;
                if(scratch.IsValid() && scratch.isLoaded) EditorSceneManager.CloseScene(scratch,true);
                if(setup.Length>0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        static void Render(Camera camera,string output,string label)
        {
            const int width=1600,height=900;
            var previous=RenderTexture.active;
            bool asyncShaders=ShaderUtil.allowAsyncCompilation;
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,width,height),0,0);
                texture.Apply();
                var bytes=texture.EncodeToPNG();
                if(bytes.Length<15000) throw new InvalidDataException("Rendered frame appears empty: "+label);
                File.WriteAllBytes(output+"/prologue-"+label+".png",bytes);
                if(label.Contains("boarders") && texture.GetPixels32().Count(pixel=>pixel.r>224 && pixel.b>224 &&
                    pixel.g<50)>width*height*.012f)
                    throw new InvalidDataException("The boarding frame contains a magenta material failure.");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=asyncShaders;
                RenderTexture.active=previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
