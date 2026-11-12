using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // Edit-time preview scene, deterministic real component calls. No campaign
    // Start, profile, learned model, microphone or webcam is opened by this check.
    public static class CommanderShieldCheck
    {
        const string Folder="Diagnostics/CampaignPreviews";
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before checking the shield.");
            Directory.CreateDirectory(Folder);
            var log=new StringBuilder("Deployable commander shield / finite directional interception\n");
            var previousLock=Cursor.lockState; bool previousVisible=Cursor.visible;
            Scene scene=EditorSceneManager.NewPreviewScene();
            GameObject fixture=null,lens=null;
            try
            {
                fixture=new GameObject("Isolated shield owner"); fixture.SetActive(false); SceneManager.MoveGameObjectToScene(fixture,scene);
                var game=fixture.AddComponent<CampaignGame>(); Set(game,"MainMenu",false);
                lens=new GameObject("Shield check camera",typeof(Camera)); SceneManager.MoveGameObjectToScene(lens,scene);
                var camera=lens.GetComponent<Camera>(); camera.enabled=false; camera.scene=scene;
                camera.transform.position=new Vector3(0,1.65f,0); camera.fieldOfView=65; camera.aspect=16f/9;
                camera.nearClipPlane=.03f; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.025f,.045f,.075f);
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=false; game.eyes=camera;
                var shield=fixture.AddComponent<CommanderEnergyShield>(); shield.Initialize(game); Set(game,"ActiveShield",shield);
                Vector3 source=camera.transform.position+new Vector3(0,-.4f,8),target=camera.transform.position+Vector3.down*.7f;
                game.SetEnergy(1);
                game.DamagePlayer(8,source,target);
                Require(game.Health==92 && game.Shield==60 && shield.BlockedHits==0,"Deployment delay: an unopened field cannot block bullets");
                Set(game,"Health",100f); for(int i=0;i<7;i++) shield.Tick(.05f,true);
                Require(shield.Deployed && shield.DeployAmount==1,"Shield unfolds in 0.3 seconds");
                game.DamagePlayer(8,source,target);
                Require(game.Health==100 && game.Shield==52 && shield.BlockedHits==1 && shield.BlockedDamage==8,"Front bullet consumes exactly eight charge and no health");
                Vector3 impact=shield.Surface.InverseTransformPoint(game.LastIncomingImpact);
                Require(Mathf.Abs(impact.z)<.0001f && Vector3.Distance(game.LastIncomingImpact,target)>.1f,"Enemy beam endpoint lies on the actual shield surface before the player");
                game.DamagePlayer(8,camera.transform.position+new Vector3(0,-.4f,-8),target);
                Require(game.Health==92 && game.Shield==52 && shield.BlockedHits==1,"Rear bullet bypasses shield and damages health");
                game.DamagePlayer(8,camera.transform.position+new Vector3(10,-.4f,3),target);
                Require(game.Health==84 && game.Shield==52,"Side bullet outside 110-degree protection arc bypasses shield");
                game.DamagePlayer(8,camera.transform.position+new Vector3(4,0,8),target+Vector3.right*2.5f);
                Require(game.Health==76 && game.Shield==52,"A bullet outside the finite panel cannot be blocked");
                game.SetPaused(true); float previous=shield.DeployAmount; shield.Tick(.2f,false);
                Require(shield.DeployAmount==previous,"Pause freezes shield presentation"); game.SetPaused(false);
                // First-person capture is the actual mesh/shader, not a mockup.
                for(int frame=0;frame<7;frame++) shield.Tick(.05f,true); Capture(camera,Folder+"/commander-shield.png");
                game.DamagePlayer(60,source,target);
                Require(game.Shield==0 && game.Health==68 && shield.BlockedHits==2 && shield.BlockedDamage==60,"Depletion absorbs remaining charge; excess damage reaches health");
                game.DamagePlayer(8,source,target);
                Require(game.Health==60 && shield.BlockedHits==2 && shield.IsDepleted,"Empty shield cannot provide invisible invulnerability");
                Set(game,"Shield",60f); game.SetEnergy(0);
                game.DamagePlayer(8,source,target);
                Require(game.Health==52 && game.Shield==60,"Weapon mode retracts protection immediately");
                for(int i=0;i<7;i++) shield.Tick(.05f,false);
                Require(shield.DeployAmount==0 && !shield.Surface.gameObject.activeSelf,"Retraction hides the mesh completely");
                game.SetEnergy(1); for(int i=0;i<7;i++) shield.Tick(.05f,true);
                Vector3 missedEnd=target+Vector3.right*.40f+Vector3.up*.35f;
                Vector3 stoppedEnd=game.ResolveIncomingShot(4,source,missedEnd,false);
                Require(game.Health==52 && game.Shield==56 && stoppedEnd!=missedEnd && shield.BlockedHits==3,
                    "A bullet crossing the field is intercepted even when its ray misses the player capsule");
                game.DamagePlayer(5);
                Require(game.Health==47 && game.Shield==56,"Undirected environmental damage does not use the directional shield");
                log.AppendLine("RESULT: PASSED");
            }
            catch(Exception exception) { log.AppendLine("RESULT: FAILED\n"+exception); throw; }
            finally
            {
                if(fixture) UnityEngine.Object.DestroyImmediate(fixture);
                if(lens) UnityEngine.Object.DestroyImmediate(lens);
                EditorSceneManager.ClosePreviewScene(scene);
                Cursor.lockState=previousLock; Cursor.visible=previousVisible;
                File.WriteAllText(Folder+"/commander-shield-check.txt",log.ToString());
            }
            void Require(bool condition,string label) { log.AppendLine((condition ? "PASS " : "FAIL ")+label); if(!condition) throw new InvalidOperationException(label); }
        }

        static void Set<T>(CampaignGame game,string property,T value)
        {
            typeof(CampaignGame).GetField("<"+property+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(game,value);
        }
        static void Capture(Camera camera,string path)
        {
            var texture=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32); Texture2D image=null; var previous=RenderTexture.active;
            try
            {
                texture.Create(); RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=texture});
                RenderTexture.active=texture; image=new Texture2D(1280,720,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply(false);
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally { RenderTexture.active=previous; if(image) UnityEngine.Object.DestroyImmediate(image); texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
