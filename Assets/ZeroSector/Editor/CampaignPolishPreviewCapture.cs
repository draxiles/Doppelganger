using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // Run with a real graphics device, e.g. -batchmode -force-d3d11 (without -nographics).
    // Does not enter Play Mode, read a player profile, change assets, or capture the desktop.
    public static class CampaignPolishPreviewCapture
    {
        const string Folder="Diagnostics/CampaignPreviews";
        const string Report=Folder+"/operation-visual-check.txt";
        const int Width=1600,Height=900;

        public static void Run()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Report,"Operation Signal / actual URP scene renders, no UI, 1600 × 900\n");
            var original=EditorSceneManager.GetSceneManagerSetup();
            bool replacedScenes=false;
            int result=1;
            try
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before rendering visual checks.");
                if(Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(scene=>scene.isDirty))
                    throw new InvalidOperationException("Save open scenes before rendering visual checks.");
                if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Offscreen render requires a graphics device. Remove -nographics.");
                File.AppendAllText(Report,"Graphics device: "+SystemInfo.graphicsDeviceType+"\n");
                replacedScenes=true;

                File.AppendAllText(Report,"The first shot uses the authored post processing. The four subsequent offscreen shots omit post processing: the URP editor batch volume stack is invalid after reloading a scene. This does not alter the game cameras.\n");
                int captures=0;
                if(Attempt("menu-orbit",()=>
                {
                    var bridge=Load(0);
                    var menu=typeof(CampaignGame).GetMethod("OpenMenuView",BindingFlags.Instance|BindingFlags.NonPublic);
                    if(menu==null) throw new MissingMethodException("CampaignGame.OpenMenuView");
                    menu.Invoke(bridge,null);
                    Capture(bridge.eyes,"menu-orbit",true);
                })) captures++;
                if(Attempt("bridge",()=>CaptureRoom(0,"bridge",new Vector3(0,1.68f,-5.7f),Quaternion.identity))) captures++;
                if(Attempt("engineering",()=>CaptureRoom(1,"engineering",new Vector3(0,1.72f,-10.18f),Quaternion.Euler(2,0,0)))) captures++;
                if(Attempt("core",()=>CaptureRoom(2,"core",new Vector3(0,1.71f,-8.9f),Quaternion.identity))) captures++;
                if(Attempt("exterior",()=>
                {
                    var core=Load(2);
                    var prefab=Resources.Load<GameObject>("Doppelganger/Exterior");
                    if(!prefab) throw new FileNotFoundException("Missing prepared Resources/Doppelganger/Exterior.prefab.");
                    var exterior=(GameObject)PrefabUtility.InstantiatePrefab(prefab,core.gameObject.scene);
                    exterior.transform.position=new Vector3(140,0,0);
                    Pose(core.eyes,exterior.transform.TransformPoint(new Vector3(0,1.69f,-5)),Quaternion.identity);
                    Capture(core.eyes,"exterior",false);
                })) captures++;
                result=captures==5 ? 0 : 1;
                File.AppendAllText(Report,$"RESULT: {(result==0 ? "PASSED" : "FAILED")} ({captures}/5 images)\n");
            }
            catch(Exception error)
            {
                File.AppendAllText(Report,"RESULT: FAILED\n"+error+"\n");
                Debug.LogException(error);
            }
            finally
            {
                try
                {
                    if(replacedScenes && original.Length>0 && original.All(scene=>!string.IsNullOrEmpty(scene.path)))
                        EditorSceneManager.RestoreSceneManagerSetup(original);
                    else if(replacedScenes)
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                }
                catch(Exception error)
                {
                    File.AppendAllText(Report,"SCENE RESTORE: FAILED\n"+error+"\n");
                    Debug.LogException(error); result=1;
                }
                if(Application.isBatchMode) EditorApplication.Exit(result);
            }
        }

        static CampaignGame Load(int chapter)
        {
            var scene=EditorSceneManager.OpenScene(CampaignGame.ScenePaths[chapter],OpenSceneMode.Single);
            var game=scene.GetRootGameObjects().Select(go=>go.GetComponent<CampaignGame>()).FirstOrDefault(g=>g);
            if(!game || !game.eyes || !game.presentation) throw new InvalidDataException("Incomplete scene: "+scene.path);
            // These are art shots. The game camera retains its authored renderer and lighting.
            if(game.presentation.weaponPivot) game.presentation.weaponPivot.gameObject.SetActive(false);
            return game;
        }

        static void Dress(CampaignGame game)
        {
            var scene=game.gameObject.scene;
            var targets=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<CampaignInteractable>()).ToArray();
            game.gameObject.AddComponent<CampaignEnvironmentArt>().Initialize(game,targets,true);
            // Transform.Find interprets the slash inside the display name as a hierarchy separator.
            if(!game.transform.Cast<Transform>().Any(child=>child.name=="05 / Reactive ship displays and route lights"))
                throw new InvalidDataException("Reactive scene art did not initialize: "+scene.path);
        }

        static void Pose(Camera camera,Vector3 position,Quaternion rotation)
        { camera.transform.SetPositionAndRotation(position,rotation); }

        static bool Attempt(string name,Action render)
        {
            try { render(); return true; }
            catch(Exception error)
            {
                File.AppendAllText(Report,"FAILED: "+name+"\n"+error+"\n");
                Debug.LogException(error); return false;
            }
        }

        static void CaptureRoom(int chapter,string name,Vector3 position,Quaternion rotation)
        {
            var room=Load(chapter);
            Dress(room);
            Pose(room.eyes,position,rotation);
            Capture(room.eyes,name,false);
        }

        static void Capture(Camera camera,string name,bool keepPostProcessing)
        {
            string path=Folder+"/operation-"+name+".png";
            var old=RenderTexture.active;
            var cameraData=camera.GetUniversalAdditionalCameraData();
            bool oldPostProcessing=cameraData.renderPostProcessing;
            RenderTexture target=null;
            Texture2D frame=null;
            try
            {
                cameraData.renderPostProcessing=keepPostProcessing && oldPostProcessing;
                target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32);
                if(!target.Create()) throw new InvalidOperationException("Offscreen render texture could not be created.");
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;
                frame=new Texture2D(Width,Height,TextureFormat.RGB24,false);
                frame.ReadPixels(new Rect(0,0,Width,Height),0,0); frame.Apply(false);
                var png=frame.EncodeToPNG();
                if(png==null || png.Length<24000) throw new InvalidDataException("Render is blank or missing: "+name);
                File.WriteAllBytes(path,png);
                File.AppendAllText(Report,path+" | "+png.Length+" bytes | post processing "+(cameraData.renderPostProcessing ? "on" : "off")+"\n");
            }
            finally
            {
                cameraData.renderPostProcessing=oldPostProcessing;
                RenderTexture.active=old;
                if(frame) UnityEngine.Object.DestroyImmediate(frame);
                if(target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
        }
    }
}
