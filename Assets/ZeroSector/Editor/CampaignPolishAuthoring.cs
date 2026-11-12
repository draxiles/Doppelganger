using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    public static class CampaignPolishAuthoring
    {
        const string Output="Assets/DoppelgangerGenerated/OperationSignal";
        const string ResourcesPath="Assets/ZeroSector/Resources/Doppelganger/";
        const string Battery="Assets/Sci-Fi Styled Modular Pack/Prefabs/Machines/Battery.prefab";
        const string Crate="Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Prefabs/Props/Crate_01.prefab";

        public static void Prepare()
        {
            Directory.CreateDirectory(ResourcesPath);
            File.Copy("../Deliverables/Lab08/assets/logo.png",ResourcesPath+"Logo.png",true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var original=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var battery=Prop("EmergencyBattery",Battery,new Vector3(.46f,.32f,.27f),Vector3.zero,out var bounds);
                var body=battery.AddComponent<Rigidbody>(); body.isKinematic=true; body.mass=2.6f;
                body.interpolation=RigidbodyInterpolation.Interpolate; body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                var collider=battery.AddComponent<BoxCollider>(); collider.size=bounds.size; collider.center=bounds.center;
                var interaction=battery.AddComponent<CampaignInteractable>(); interaction.id=CampaignCargo.Id; interaction.title="Взять аварийный аккумулятор";
                battery.AddComponent<CampaignCargo>(); Save(battery);

                var dock=Prop("BatteryDock",Crate,new Vector3(.86f,.52f,.68f),new Vector3(0,.26f,0),out var dockBounds);
                var solid=dock.AddComponent<BoxCollider>(); solid.size=dockBounds.size; solid.center=dockBounds.center;
                var tag=new GameObject("Battery rack label",typeof(TextMesh)).GetComponent<TextMesh>();
                tag.transform.SetParent(dock.transform,false); tag.transform.localPosition=new Vector3(0,.45f,-dockBounds.extents.z-.01f);
                tag.text="AUX  /  01"; tag.fontSize=42; tag.characterSize=.026f; tag.anchor=TextAnchor.MiddleCenter; tag.color=new Color(1,.7f,.26f);
                Save(dock);

                var salvage=Prop("Salvage",Crate,new Vector3(.54f,.30f,.42f),new Vector3(0,.15f,0),out _);
                salvage.AddComponent<CampaignInteractable>(); Save(salvage);
            }
            finally
            {
                if(original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(scene,true);
            }
            CampaignExteriorAuthoring.Prepare();
            AssetDatabase.SaveAssets();
        }
        static GameObject Prop(string name,string path,Vector3 size,Vector3 center,out Bounds fitted)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!source) throw new FileNotFoundException(path);
            var root=new GameObject(name);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(source,root.transform);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach(var light in model.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
            foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true)) if(component) UnityEngine.Object.DestroyImmediate(component);
            foreach(var child in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
            foreach(var lod in model.GetComponentsInChildren<LODGroup>(true)) BridgeVisualQuality.UseHighestDetail(lod);
            var bounds=Measure(model);
            float scale=Mathf.Min(size.x/Mathf.Max(.01f,bounds.size.x),size.y/Mathf.Max(.01f,bounds.size.y),size.z/Mathf.Max(.01f,bounds.size.z));
            model.transform.localScale*=scale;
            model.transform.position+=center-Measure(model).center;
            fitted=Measure(model);
            FpsArenaGraphics.ConvertMaterials(root,Output);
            return root;
        }
        static Bounds Measure(GameObject model)
        {
            var renderers=model.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0) throw new InvalidDataException(model.name+" has no renderable geometry.");
            var box=renderers[0].bounds;
            foreach(var renderer in renderers) box.Encapsulate(renderer.bounds);
            return box;
        }
        static void Save(GameObject root)
        {
            if(!PrefabUtility.SaveAsPrefabAsset(root,ResourcesPath+root.name+".prefab")) throw new IOException(root.name+" prefab could not be saved.");
        }
    }
}
