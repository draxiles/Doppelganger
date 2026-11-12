using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZeroSector.Learning;

namespace ZeroSector.Editor
{
    public static class ImitationArenaBuilder
    {
        const string Pack="Assets/Sci-Fi Styled Modular Pack/Prefabs/";
        const string Output="Assets/DoppelgangerGenerated/LearningArena";
        static Transform room;
        static readonly Dictionary<Material,Material> materials=new();

        public static void Build()
        {
            if(EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Learning arena","Сначала остановите Play.","OK"); return; }
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Output+"/Materials"); AssetDatabase.Refresh(); materials.Clear();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            room=new GameObject("Training deck / imported sci-fi meshes").transform;
            for(int x=-10;x<=10;x+=4)
                for(int z=-10;z<=10;z+=4)
                    Mesh("Floors/floor_1",new Vector3(x,-.12f,z),new Vector3(4,.24f,4),0,room);
            Collision("Floor",new Vector3(0,-.25f,0),new Vector3(24,.5f,24));
            for(int i=-10;i<=10;i+=4)
            {
                Mesh("Walls/decorative_wall_1",new Vector3(i,2,12),new Vector3(4,4,.3f),0,room);
                Mesh("Walls/decorative_wall_1",new Vector3(i,2,-12),new Vector3(4,4,.3f),180,room);
                Mesh("Walls/decorative_wall_6",new Vector3(-12,2,i),new Vector3(.3f,4,4),90,room);
                Mesh("Walls/decorative_wall_6",new Vector3(12,2,i),new Vector3(.3f,4,4),-90,room);
            }
            Collision("North",new Vector3(0,2,12),new Vector3(24,4,.3f));
            Collision("South",new Vector3(0,2,-12),new Vector3(24,4,.3f));
            Collision("West",new Vector3(-12,2,0),new Vector3(.3f,4,24));
            Collision("East",new Vector3(12,2,0),new Vector3(.3f,4,24));
            foreach(int x in new[]{-9,9})
                foreach(int z in new[]{-8,8})
                {
                    Mesh("Machines/storage_container_big",new Vector3(x,.8f,z),new Vector3(1.8f,1.6f,2.8f),0,room);
                    Collision("Container",new Vector3(x,.8f,z),new Vector3(1.8f,1.6f,2.8f));
                }
            var beam=MakeMaterial("Laser",new Color(.15f,.75f,.95f),true);
            var guide=new GameObject("Navigation ring",typeof(LineRenderer)).GetComponent<LineRenderer>();
            guide.sharedMaterial=beam; guide.loop=true; guide.positionCount=96; guide.startWidth=guide.endWidth=.045f;
            for(int i=0;i<96;i++) { float angle=i*Mathf.PI*2/96; guide.SetPosition(i,new Vector3(Mathf.Sin(angle)*6,.04f,Mathf.Cos(angle)*6)); }
            for(int x=-8;x<=8;x+=8)
            {
                var light=new GameObject("Overhead soft light",typeof(Light)).GetComponent<Light>();
                light.type=LightType.Point; light.transform.position=new Vector3(x,5,0); light.range=22; light.intensity=5;
                light.color=new Color(.7f,.83f,1); light.shadows=LightShadows.None;
            }
            var sun=new GameObject("Fill",typeof(Light)).GetComponent<Light>();
            sun.type=LightType.Directional; sun.transform.rotation=Quaternion.Euler(55,-30,0); sun.intensity=1;
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.35f,.4f,.5f);
            RenderSettings.fog=false;
            var sky=Shader.Find("Doppelganger/DeepSpace");
            if(sky) RenderSettings.skybox=Persist(new Material(sky),"Sky");

            var human=new GameObject("Commander",typeof(CharacterController));
            var player=human.GetComponent<CharacterController>(); ConfigureController(player);
            var cameraObject=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));
            cameraObject.tag="MainCamera"; cameraObject.transform.SetParent(human.transform,false); cameraObject.transform.localPosition=new Vector3(0,1.65f,0);
            var camera=cameraObject.GetComponent<Camera>(); camera.nearClipPlane=.08f; camera.farClipPlane=250; camera.fieldOfView=75;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var enemy=new GameObject("Doppelganger / learned policy",typeof(CharacterController));
            var body=enemy.GetComponent<CharacterController>(); ConfigureController(body);
            var visual=new GameObject("Mechanical drone").transform; visual.SetParent(enemy.transform,false);
            Mesh("Machines/shield_generator",new Vector3(0,1.1f,0),new Vector3(1.1f,1.6f,1.1f),0,visual);
            Mesh("Machines/rotor",new Vector3(0,1.95f,0),new Vector3(1.4f,.22f,1.4f),0,visual);
            var marker=new GameObject("Enemy locator",typeof(Light)).GetComponent<Light>();
            marker.transform.SetParent(visual,false); marker.transform.localPosition=new Vector3(0,2,0);
            marker.type=LightType.Point; marker.color=new Color(1,.2f,.06f); marker.range=4; marker.intensity=2;
            enemy.transform.position=new Vector3(0,.05f,0);
            human.transform.position=new Vector3(0,.05f,-7);
            var arena=new GameObject("Learning arena / training and evaluation").AddComponent<ImitationArena>();
            arena.player=player; arena.doubleBody=body; arena.doubleVisual=visual; arena.eyes=camera; arena.laserMaterial=beam;
            arena.hud=arena.gameObject.AddComponent<ArenaHud>();
            Physics.SyncTransforms();
            if(!Physics.Raycast(human.transform.position+Vector3.up,Vector3.down,2)) throw new InvalidOperationException("No floor under player.");
            BridgeBuilder.FixQuality();
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene,Output+"/LearningArena.unity");
            Selection.activeGameObject=human;
            Debug.Log("Learning arena ready. Game -> Play. F1 teaching, F2 frozen duel, F3 reset network, F4 reset round, F5 save, F9 load.");
        }

        static void ConfigureController(CharacterController controller)
        { controller.height=1.8f; controller.radius=.4f; controller.center=new Vector3(0,.9f,0); controller.stepOffset=.25f; }

        static void Collision(string name,Vector3 position,Vector3 size)
        {
            var go=new GameObject(name+" collision"); go.transform.SetParent(room); go.transform.position=position; go.AddComponent<BoxCollider>().size=size;
        }

        static Bounds BoundsOf(GameObject model)
        {
            bool any=false; Bounds b=default;
            foreach(var r in model.GetComponentsInChildren<Renderer>())
                if(r.enabled) { if(!any) {b=r.bounds; any=true;} else b.Encapsulate(r.bounds); }
            if(!any) throw new InvalidOperationException("No visible mesh in "+model.name);
            return b;
        }

        static GameObject Mesh(string path,Vector3 position,Vector3 size,float yaw,Transform parent)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Pack+path+".prefab");
            if(!prefab) throw new InvalidOperationException("Missing training model: "+path);
            var root=new GameObject(Path.GetFileName(path)); root.transform.SetParent(parent,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab); model.transform.SetParent(root.transform,false);
            model.transform.localRotation=Quaternion.Euler(0,yaw,0);
            foreach(var c in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            foreach(var lod in model.GetComponentsInChildren<LODGroup>()) BridgeVisualQuality.UseHighestDetail(lod);
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var mats=renderer.sharedMaterials;
                for(int i=0;i<mats.Length;i++) if(mats[i]) mats[i]=Convert(mats[i]);
                renderer.sharedMaterials=mats;
            }
            var bounds=BoundsOf(model);
            root.transform.localScale=new Vector3(size.x/Mathf.Max(.001f,bounds.size.x),size.y/Mathf.Max(.001f,bounds.size.y),size.z/Mathf.Max(.001f,bounds.size.z));
            bounds=BoundsOf(model); model.transform.position-=bounds.center;
            root.transform.localPosition=position;
            return root;
        }

        static Material Convert(Material source)
        {
            if(materials.TryGetValue(source,out var cached)) return cached;
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor",source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.gray);
            string property=source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") ? "_BaseMap" : "_MainTex";
            if(source.HasProperty(property) && source.GetTexture(property)) material.SetTexture("_BaseMap",source.GetTexture(property));
            material.SetFloat("_Smoothness",.15f); material.SetFloat("_Metallic",.08f);
            if(source.name.ToLowerInvariant().Contains("emis"))
            { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor",new Color(.08f,.65f,.85f)*1.5f); }
            string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            cached=Persist(material,source.name+"_"+id.Substring(0,Math.Min(8,id.Length))); materials[source]=cached; return cached;
        }

        static Material MakeMaterial(string name,Color color,bool glow)
        {
            var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit")); mat.SetColor("_BaseColor",color);
            return Persist(mat,name);
        }

        static Material Persist(Material material,string name)
        {
            material.name=name;
            string path=Output+"/Materials/"+name+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(existing) { EditorUtility.CopySerialized(material,existing); UnityEngine.Object.DestroyImmediate(material); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(material,path); return material;
        }
    }
}
