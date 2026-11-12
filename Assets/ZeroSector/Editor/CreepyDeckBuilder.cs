using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZeroSector.Learning;

namespace ZeroSector.Editor
{
    // One-shot authoring request. No Play Mode changes and no changes to the source kit.
    public static class CreepyDeckBuilder
    {
        const string Pack="Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Prefabs/";
        const string Output="Assets/DoppelgangerGenerated/Campaign";
        const string Request="Temp/DoppelgangerChecks/create-three-decks.request";
        static Transform geometry;
        static readonly List<string> results=new();

        [InitializeOnLoadMethod]
        static void Pending()
        {
            if(!File.Exists(Request)) return;
            EditorApplication.delayCall+=RunPending;
        }
        static void RunPending()
        {
            if(!File.Exists(Request)) return;
            if(EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.Log("Двойник: создание трёх локаций ожидает Stop Play. Текущий запуск не прерывается.");
                EditorApplication.playModeStateChanged+=AfterPlay;
                return;
            }
            if(EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall+=RunPending; return; }
            File.Delete(Request);
            try { BuildAll(); }
            catch(Exception exception)
            { File.WriteAllText("Temp/DoppelgangerChecks/deck-build-error.txt",exception.ToString()); Debug.LogException(exception); }
        }
        static void AfterPlay(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged-=AfterPlay; EditorApplication.delayCall+=RunPending;
        }

        public static void BuildAll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Сначала останови Play."); return; }
            if(!AssetDatabase.IsValidFolder(Pack.TrimEnd('/'))) throw new InvalidOperationException("Creepy Cat Starter Kit не импортирован.");
            Directory.CreateDirectory(Output); Directory.CreateDirectory("Diagnostics/CampaignPreviews");
            AssetDatabase.Refresh(); results.Clear();
            Scene previous=SceneManager.GetActiveScene();
            string sourcePath="Assets/DoppelgangerGenerated/LearningArena/LearningArena.unity";
            Scene source=SceneManager.GetSceneByPath(sourcePath);
            bool opened=!source.isLoaded;
            if(opened) source=EditorSceneManager.OpenScene(sourcePath,OpenSceneMode.Additive);
            var sourceArena=source.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<ImitationArena>()).FirstOrDefault();
            if(!sourceArena) throw new InvalidOperationException("В тестовой арене нет ImitationArena.");
            string[] names={"01_CommandBridge","02_Engineering","03_DoppelgangerCore"};
            try
            {
                for(int i=0;i<3;i++)
                {
                    string path=Output+"/"+names[i]+".unity";
                    if(File.Exists(path)) { results.Add("Preserved existing scene: "+path); continue; }
                    var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                    SceneManager.SetActiveScene(scene);
                    try
                    {
                        BuildRoom(i);
                        var arena=Actors(sourceArena,i);
                        Lighting(i,arena.eyes);
                        FpsArenaGraphics.ConvertMaterials(geometry.gameObject,Output);
                        Validate(scene,arena);
                        EditorSceneManager.SaveScene(scene,path);
                        Capture(scene,arena.eyes,"Diagnostics/CampaignPreviews/"+names[i]+".png");
                        results.Add("Created: "+path+"; renderers="+scene.GetRootGameObjects().Sum(root=>root.GetComponentsInChildren<Renderer>().Length));
                    }
                    finally { EditorSceneManager.CloseScene(scene,true); }
                }
            }
            finally
            {
                if(opened && source.isLoaded) EditorSceneManager.CloseScene(source,true);
                if(previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            var gameScenes=names.Select(name=>new EditorBuildSettingsScene(Output+"/"+name+".unity",true)).ToList();
            foreach(var entry in EditorBuildSettings.scenes)
                if(!entry.path.EndsWith("SampleScene.unity") && !entry.path.EndsWith("FirstFlight.unity") && !gameScenes.Any(item=>item.path==entry.path)) gameScenes.Add(entry);
            EditorBuildSettings.scenes=gameScenes.ToArray();
            AssetDatabase.SaveAssets();
            // Ask Unity to preserve any unrelated unsaved scene before changing the open scene.
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(Output+"/01_CommandBridge.unity",OpenSceneMode.Single);
                ArchivePrototypes();
            }
            else results.Add("Prototype cleanup deferred: user kept an unsaved scene open.");
            File.WriteAllLines("Diagnostics/CampaignPreviews/build-result.txt",results);
            Debug.Log("Двойник: три локации созданы.\n"+string.Join("\n",results));
        }

        static void BuildRoom(int chapter)
        {
            geometry=new GameObject("01 / Creepy Cat authored modules").transform;
            int columns=chapter==0 ? 4 : 3, rows=chapter==2 ? 3 : 4;
            float halfWidth=columns*3,halfDepth=rows*3;
            float[] xs=Enumerable.Range(0,columns).Select(i=>i*6-halfWidth+3).ToArray();
            float[] zs=Enumerable.Range(0,rows).Select(i=>i*6-halfDepth+3).ToArray();
            foreach(float x in xs) foreach(float z in zs)
                Module(chapter==1 ? "Floors/Floor_Squared_02_6x6" : "Floors/Floor_Squared_01_6x6",new Vector3(x,0,z),6,0,true,true);
            ColliderBox("Continuous walkable deck",new Vector3(0,-.2f,0),new Vector3(halfWidth*2,.4f,halfDepth*2));
            var probe=Module("Walls/Wall_Simple_01_Long",new Vector3(100,0,100),6,0,false,false);
            float wallHeight=BoundsOf(probe).size.y;
            UnityEngine.Object.DestroyImmediate(probe);
            int tiers=wallHeight<4.1f ? 2 : 1;
            float ceiling=wallHeight*tiers;
            for(int tier=0;tier<tiers;tier++)
            {
                foreach(float x in xs)
                {
                    string front=chapter==0 && tier==0 ? "Walls/Porthole_01_Long" : chapter==1 ? "Walls/Wall_Pipes_01_Large" : "Walls/Wall_Simple_03_Long";
                    Module(front,new Vector3(x,tier*wallHeight,halfDepth),6,180,false,true);
                    Module("Walls/Wall_Simple_01_Long",new Vector3(x,tier*wallHeight,-halfDepth),6,0,false,true);
                }
                foreach(float z in zs)
                {
                    Module(chapter==1 ? "Walls/Wall_Pipes_02_Large" : "Walls/Wall_Console_01_Large",new Vector3(-halfWidth,tier*wallHeight,z),6,90,false,true);
                    Module(chapter==2 ? "Walls/Wall_Gear_01_Half" : "Walls/Wall_Simple_02_Long",new Vector3(halfWidth,tier*wallHeight,z),6,-90,false,true);
                }
            }
            foreach(float x in xs) foreach(float z in zs)
                Module("Roofs/Roof_Squared_01_6x6",new Vector3(x,ceiling,z),6,0,true,true);
            for(int side=-1;side<=1;side+=2)
            {
                foreach(float z in zs)
                {
                    Module("Lights/Light_SideLight_01",new Vector3(side*(halfWidth-.35f),2.5f,z),1.2f,side>0 ? -90 : 90,false,false);
                    if(chapter==1) Module("Stuff/Pipes_01",new Vector3(side*(halfWidth-1),0,z),1.6f,0,false,true);
                }
                for(int i=0;i<2;i++)
                {
                    float z=i==0 ? -3.3f : 3.3f;
                    var crate=Module("Props/Crate_01",new Vector3(side*(halfWidth-3),0,z),chapter==0 ? 1.5f : 1.8f,0,false,true);
                    results.Add("Cover "+chapter+": "+BoundsOf(crate).size);
                }
            }
            if(chapter==0)
                foreach(float x in new[]{-6f,0,6f}) Module("Walls/Wall_Console_01_Half",new Vector3(x,0,halfDepth-2),2.5f,180,false,true);
            if(chapter==2)
            {
                Module("Walls/Column_01_Big",new Vector3(0,0,5.5f),2.6f,0,false,true);
                Module("Stuff/Beam_01",new Vector3(-5,0,5),1.1f,0,false,true);
                Module("Stuff/Beam_01",new Vector3(5,0,5),1.1f,0,false,true);
            }
            // Explicit lateral routes remain clear around the central duel lane.
            ColliderBox("West boundary",new Vector3(-halfWidth,ceiling/2,0),new Vector3(.3f,ceiling,halfDepth*2));
            ColliderBox("East boundary",new Vector3(halfWidth,ceiling/2,0),new Vector3(.3f,ceiling,halfDepth*2));
            ColliderBox("South boundary",new Vector3(0,ceiling/2,-halfDepth),new Vector3(halfWidth*2,ceiling,.3f));
            ColliderBox("North boundary",new Vector3(0,ceiling/2,halfDepth),new Vector3(halfWidth*2,ceiling,.3f));
            results.Add("Room "+chapter+": "+(halfWidth*2)+" x "+(halfDepth*2)+" m; ceiling "+ceiling.ToString("F2"));
        }

        static GameObject Module(string relative,Vector3 position,float span,float yaw,bool horizontal,bool solid)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Pack+relative+".prefab");
            if(!prefab) throw new InvalidOperationException("Missing Creepy Cat model: "+relative);
            var root=new GameObject(Path.GetFileName(relative)); root.transform.SetParent(geometry,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            model.transform.localPosition=Vector3.zero; model.transform.localRotation=Quaternion.identity;
            foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true)) if(component) UnityEngine.Object.DestroyImmediate(component);
            foreach(var light in model.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
            foreach(var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach(var group in model.GetComponentsInChildren<LODGroup>(true)) BridgeVisualQuality.UseHighestDetail(group);
            var bounds=BoundsOf(root);
            if(!horizontal && bounds.size.z>bounds.size.x*1.2f) model.transform.localRotation=Quaternion.Euler(0,90,0);
            bounds=BoundsOf(root);
            float scale=span/Mathf.Max(.01f,horizontal ? Mathf.Max(bounds.size.x,bounds.size.z) : bounds.size.x);
            root.transform.localScale=Vector3.one*scale;
            bounds=BoundsOf(root);
            // Preserve proportions. Floors are anchored by their walking surface, other modules by their feet.
            bool floor=relative.StartsWith("Floors/");
            model.transform.position-=new Vector3(bounds.center.x,floor ? bounds.max.y : bounds.min.y,bounds.center.z);
            root.transform.rotation=Quaternion.Euler(0,yaw,0); root.transform.position=position;
            if(solid && !floor)
                foreach(var filter in model.GetComponentsInChildren<MeshFilter>())
                    if(filter.sharedMesh && filter.GetComponent<Renderer>() && filter.GetComponent<Renderer>().enabled)
                    { var collider=filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh=filter.sharedMesh; }
            return root;
        }
        static Bounds BoundsOf(GameObject root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>().Where(renderer=>renderer.enabled).ToArray();
            if(renderers.Length==0) throw new InvalidOperationException("No visible mesh: "+root.name);
            Bounds bounds=renderers[0].bounds;
            foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        public static void RepairRoom(Scene scene)
        {
            geometry=scene.GetRootGameObjects().First(root=>root.name=="01 / Creepy Cat authored modules").transform;
            if(geometry.Find("Art pass complete")) return;
            foreach(var previous in geometry.Cast<Transform>().Where(node=>node.name=="Structural backing" || node.name=="Stacked cover").ToArray())
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var originals=geometry.Cast<Transform>().ToArray();
            foreach(var node in originals)
            {
                if(node.name.StartsWith("Wall_Console_01_Large") || node.name.StartsWith("Wall_Pipes_") || node.name.StartsWith("Wall_Gear_"))
                {
                    Vector3 offset=node.rotation*Vector3.back*.18f;
                    Module("Walls/Wall_Simple_01_Long",node.position+offset,6,node.eulerAngles.y,false,true).name="Structural backing";
                }
                if(node.name=="Crate_01")
                {
                    float height=BoundsOf(node.gameObject).size.y;
                    Module("Props/Crate_01",node.position+Vector3.up*height,BoundsOf(node.gameObject).size.x,node.eulerAngles.y,false,true).name="Stacked cover";
                }
                if(node.name.StartsWith("Wall_Gear_")) node.localScale*=.5f;
                if(scene.name=="01_CommandBridge" && node.name=="Wall_Console_01_Half")
                { UnityEngine.Object.DestroyImmediate(node.gameObject); continue; }
                if(node.name=="Beam_01") UnityEngine.Object.DestroyImmediate(node.gameObject);
            }
            if(scene.name=="01_CommandBridge")
                foreach(float x in new[]{-6f,0,6f})
                {
                    Furniture("Decorative elements/computer_station",new Vector3(x,0,9.7f),2.3f);
                    Furniture("Decorative elements/pilot_seat",new Vector3(x,0,7.9f),.9f);
                }
            FpsArenaGraphics.ConvertMaterials(geometry.gameObject,Output);
            foreach(var renderer in geometry.GetComponentsInChildren<Renderer>())
                foreach(var material in renderer.sharedMaterials)
                    if(material && AssetDatabase.GetAssetPath(material).StartsWith(Output+"/Materials/") && material.HasProperty("_Cull"))
                    { material.SetFloat("_Cull",0); EditorUtility.SetDirty(material); }
            var lightRoot=scene.GetRootGameObjects().First(root=>root.name=="02 / lighting").transform;
            var fill=Lamp(lightRoot,"Soft interior bounce",new Vector3(0,4,0),new Color(.6f,.74f,1),.5f,50,LightType.Directional);
            fill.transform.rotation=Quaternion.Euler(35,-30,0); fill.shadows=LightShadows.None;
            RenderSettings.ambientSkyColor=new Color(.22f,.27f,.34f);
            RenderSettings.ambientEquatorColor=new Color(.15f,.18f,.24f);
            RenderSettings.ambientGroundColor=new Color(.08f,.1f,.14f);
            var marker=new GameObject("Art pass complete"); marker.transform.SetParent(geometry,false);
        }

        public static void RefineBridge(Scene scene)
        {
            geometry=scene.GetRootGameObjects().First(root=>root.name=="01 / Creepy Cat authored modules").transform;
            foreach(var node in geometry.Cast<Transform>().Where(item=>item.name=="computer_station" || item.name=="pilot_seat" || item.name=="Furniture collision").ToArray())
                UnityEngine.Object.DestroyImmediate(node.gameObject);
            foreach(float x in new[]{-6f,0,6f})
            {
                Furniture("Decorative elements/computer_station",new Vector3(x,0,9.7f),2.3f,1.35f);
                Furniture("Decorative elements/pilot_seat",new Vector3(x,0,7.9f),.9f,1.35f);
            }
            FpsArenaGraphics.ConvertMaterials(geometry.gameObject,Output);
            var glass=AssetDatabase.LoadAssetAtPath<Material>("Assets/DoppelgangerGenerated/Earth/BridgeGlass.mat");
            foreach(var renderer in geometry.GetComponentsInChildren<Renderer>())
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                    if(materials[i] && materials[i].name.IndexOf("Glass",StringComparison.OrdinalIgnoreCase)>=0) materials[i]=glass;
                renderer.sharedMaterials=materials;
            }
        }

        static void Furniture(string path,Vector3 position,float width,float maxHeight=0)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sci-Fi Styled Modular Pack/Prefabs/"+path+".prefab");
            if(!asset) return;
            var root=new GameObject(Path.GetFileName(path)).transform; root.SetParent(geometry,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(asset,root);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            var bounds=BoundsOf(model);
            float scale=width/bounds.size.x;
            if(maxHeight>0) scale=Mathf.Min(scale,maxHeight/bounds.size.y);
            root.localScale=Vector3.one*scale;
            bounds=BoundsOf(model); model.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            root.rotation=Quaternion.Euler(0,180,0); root.position=position; bounds=BoundsOf(model);
            var colliderBox=new GameObject("Furniture collision",typeof(BoxCollider)); colliderBox.transform.SetParent(geometry,false);
            colliderBox.transform.position=bounds.center; colliderBox.GetComponent<BoxCollider>().size=bounds.size;
        }
        static void ColliderBox(string name,Vector3 center,Vector3 size)
        { var go=new GameObject(name,typeof(BoxCollider)); go.transform.SetParent(geometry,false); go.transform.position=center; go.GetComponent<BoxCollider>().size=size; }

        static ImitationArena Actors(ImitationArena source,int chapter)
        {
            var human=new GameObject("Commander",typeof(CharacterController));
            var body=human.GetComponent<CharacterController>(); body.height=1.8f; body.radius=.4f; body.center=Vector3.up*.9f; body.stepOffset=.25f;
            human.transform.position=new Vector3(0,.05f,-7);
            var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
            camera.tag="MainCamera"; camera.transform.SetParent(human.transform,false); camera.transform.localPosition=Vector3.up*1.65f;
            camera.fieldOfView=75; camera.nearClipPlane=.035f; camera.farClipPlane=250;
            var enemy=UnityEngine.Object.Instantiate(source.doubleBody.gameObject);
            SceneManager.MoveGameObjectToScene(enemy,SceneManager.GetActiveScene());
            enemy.name="Doppelganger / diagnostic body"; enemy.transform.position=new Vector3(0,.05f,0);
            var arena=new GameObject("Doppelganger / learning and combat",typeof(ImitationArena),typeof(ArenaHud),typeof(FirstPersonPresentation)).GetComponent<ImitationArena>();
            arena.player=body; arena.eyes=camera; arena.doubleBody=enemy.GetComponent<CharacterController>();
            arena.doubleVisual=enemy.transform.Find(source.doubleVisual.name); arena.laserMaterial=source.laserMaterial; arena.hud=arena.GetComponent<ArenaHud>();
            arena.presentation=arena.GetComponent<FirstPersonPresentation>(); arena.presentation.body=body; arena.presentation.eyes=camera;
            FpsArenaGraphics.AddEasyPreviewTo(arena);
            return arena;
        }

        static void Lighting(int chapter,Camera camera)
        {
            Color accent=chapter==0 ? new Color(.15f,.65f,1) : chapter==1 ? new Color(1,.32f,.09f) : new Color(.65f,.18f,1);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.13f,.18f,.25f); RenderSettings.ambientEquatorColor=new Color(.09f,.12f,.17f); RenderSettings.ambientGroundColor=new Color(.045f,.055f,.08f);
            RenderSettings.fog=false;
            var sky=AssetDatabase.LoadAssetAtPath<Material>("Assets/DoppelgangerGenerated/LearningArena/Materials/Sky.mat");
            if(sky) RenderSettings.skybox=sky;
            var root=new GameObject("02 / lighting").transform;
            for(int x=-6;x<=6;x+=6) for(int z=-6;z<=6;z+=6)
            {
                var lamp=Lamp(root,"Overhead soft pool",new Vector3(x,3.5f,z),new Color(.65f,.8f,1),11,13,LightType.Spot);
                lamp.transform.rotation=Quaternion.Euler(90,0,0); lamp.spotAngle=105; lamp.innerSpotAngle=70;
                lamp.shadows=x==0 && z!=0 ? LightShadows.Soft : LightShadows.None;
            }
            Lamp(root,"Accent left",new Vector3(-7,2,1),accent,6,13,LightType.Point);
            Lamp(root,"Accent right",new Vector3(7,2,-1),chapter==1 ? new Color(.15f,.65f,1) : new Color(1,.4f,.15f),5,13,LightType.Point);
            Lamp(root,"Focal light",new Vector3(0,3,6),accent,5,9,LightType.Point);
            camera.allowHDR=true; camera.allowDynamicResolution=false;
            var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=true;
            data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality=AntialiasingQuality.High;
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Output+"/Atmosphere.asset");
            if(!profile)
            {
                profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,Output+"/Atmosphere.asset");
                profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
                var bloom=profile.Add<Bloom>(true); bloom.intensity.Override(.18f); bloom.threshold.Override(1.25f);
                var grade=profile.Add<ColorAdjustments>(true); grade.postExposure.Override(.6f); grade.contrast.Override(5);
                profile.Add<Vignette>(true).intensity.Override(.12f);
                foreach(var component in profile.components) AssetDatabase.AddObjectToAsset(component,profile);
            }
            var volume=new GameObject("ACES / light bloom",typeof(Volume)).GetComponent<Volume>(); volume.isGlobal=true; volume.priority=10; volume.sharedProfile=profile;
            if(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
            { pipeline.renderScale=1; pipeline.shadowDistance=35; EditorUtility.SetDirty(pipeline); }
        }
        static Light Lamp(Transform parent,string name,Vector3 position,Color color,float intensity,float range,LightType type)
        { var light=new GameObject(name,typeof(Light)).GetComponent<Light>(); light.transform.SetParent(parent); light.transform.position=position; light.color=color; light.intensity=intensity; light.range=range; light.type=type; return light; }

        static void Validate(Scene scene,ImitationArena arena)
        {
            Physics.SyncTransforms();
            foreach(var actor in new[]{arena.player,arena.doubleBody})
            {
                bool floor=Physics.RaycastAll(actor.transform.position+Vector3.up,Vector3.down,2).Any(hit=>hit.collider.gameObject.scene==scene && hit.collider!=actor);
                if(!floor) throw new InvalidOperationException("No floor under "+actor.name);
                var overlaps=Physics.OverlapCapsule(actor.transform.position+Vector3.up*.5f,actor.transform.position+Vector3.up*1.3f,.35f);
                if(overlaps.Any(item=>item.gameObject.scene==scene && item!=actor && !item.transform.IsChildOf(actor.transform)))
                    throw new InvalidOperationException("Blocked spawn for "+actor.name);
            }
            foreach(var renderer in geometry.GetComponentsInChildren<Renderer>())
                foreach(var material in renderer.sharedMaterials)
                    if(!material || !material.shader || material.shader.name.Contains("InternalErrorShader")) throw new InvalidOperationException("Missing material/shader on "+renderer.name);
        }

        public static void Capture(Scene scene,Camera camera,string path)
        {
            try
            {
                var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
                var previous=RenderTexture.active; RenderTexture.active=target;
                var image=new Texture2D(1280,720,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG()); RenderTexture.active=previous;
                UnityEngine.Object.DestroyImmediate(image); target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
            catch(Exception exception) { results.Add("Preview unavailable: "+exception.Message); }
        }

        static void ArchivePrototypes()
        {
            string archive="Diagnostics/ArchivedPrototypes/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            foreach(string asset in new[]{"Assets/Scenes/FirstFlight.unity","Assets/Scenes/SampleScene.unity","Assets/ZeroSector/Scripts/FirstFlight.cs","Assets/ZeroSector/Editor/FirstFlightSetup.cs"})
            {
                string absolute=Path.GetFullPath(asset), assetsRoot=Path.GetFullPath("Assets")+Path.DirectorySeparatorChar;
                if(!absolute.StartsWith(assetsRoot,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Cleanup path outside Assets.");
                if(!File.Exists(absolute)) continue;
                Directory.CreateDirectory(archive); File.Copy(absolute,Path.Combine(archive,Path.GetFileName(asset)),false);
                if(File.Exists(absolute+".meta")) File.Copy(absolute+".meta",Path.Combine(archive,Path.GetFileName(asset)+".meta"),false);
                if(!AssetDatabase.DeleteAsset(asset)) throw new IOException("Could not remove "+asset);
                results.Add("Archived and removed prototype: "+asset);
            }
        }
    }
}
