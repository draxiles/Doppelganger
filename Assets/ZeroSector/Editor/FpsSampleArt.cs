using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using ZeroSector.Learning;

namespace ZeroSector.Editor
{
    public static class FpsSampleArt
    {
        const string Import="Assets/ThirdParty/UnityFpsSample";
        const string Output="Assets/DoppelgangerGenerated/Weapons";
        const string Stage="ThirdPartyStaging/UnityFpsSample/Assets";

        public static void Prepare()
        {
            if(!Directory.Exists(Stage)) throw new DirectoryNotFoundException("Unity FPS Sample art has not been downloaded.");
            Directory.CreateDirectory(Import); Directory.CreateDirectory(Output+"/Materials");
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(string file in Directory.GetFiles(Stage,"*",SearchOption.AllDirectories))
                {
                    string relative=file.Substring(Stage.Length+1).Replace('\\','/');
                    // Upstream prefabs contain dependencies on the sample's own game. Build our own art prefabs.
                    if(relative.StartsWith("Prefabs/") || relative.StartsWith("Materials/")) continue;
                    string destination=Import+"/"+relative;
                    if(File.Exists(destination)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    if(relative.EndsWith(".tif.meta"))
                    {
                        string metadata=File.ReadAllText(file);
                        metadata=Regex.Replace(metadata,@"maxTextureSize: \d+","maxTextureSize: 2048");
                        File.WriteAllText(destination,metadata);
                    }
                    else File.Copy(file,destination);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Build("Terraformer","Terraformer/Terraformer_A--FirstPerson.fbx","Terraformer_Weapon_A/Terraformer_Weapon_A_1P.fbx","СКАТ / импульсная винтовка",12,.28f);
            Build("Robot","Robot/Robot_A--FirstPerson.fbx","Robot_Weapon_A/Robot_Weapon_A.fbx","ЭХО / тяжёлый излучатель",24,.6f);
            AssetDatabase.SaveAssets();
        }

        static void Build(string kind,string character,string weapon,string title,float damage,float interval)
        {
            string path=Output+"/"+kind+".prefab";
            var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(existing && existing.GetComponent<WeaponArtBinding>().formatVersion>=3) return;
            var root=new GameObject(kind+" / FPS Sample art"); root.SetActive(false);
            try
            {
                var frame=new GameObject("Camera-relative authored rig").transform; frame.SetParent(root.transform,false);
                var arms=Model(Import+"/Models/Characters/"+character,frame);
                var gun=Model(Import+"/Models/Weapons/"+weapon,frame);
                arms.transform.localScale=gun.transform.localScale=Vector3.one*.3f;
                var armAnimator=arms.GetComponent<Animator>(); if(!armAnimator) armAnimator=arms.AddComponent<Animator>();
                var gunAnimator=gun.GetComponent<Animator>(); if(!gunAnimator) gunAnimator=gun.AddComponent<Animator>();
                armAnimator.applyRootMotion=gunAnimator.applyRootMotion=false;
                var slot=new FirstPersonPresentation.WeaponSlot
                {
                    id="UnityFpsSample."+kind,title=title,root=root.transform,animator=armAnimator,secondaryAnimator=gunAnimator,
                    idle=Clip(kind,"Idle"),walk=Clip(kind,"Run"),run=Clip(kind,"Sprint"),fire=Clip(kind,"Shoot"),reload=Clip(kind,"Reload"),
                    secondaryIdle=Clip(kind,"Idle_Weapon"),secondaryWalk=Clip(kind,"Run_Weapon"),
                    secondaryRun=Clip(kind,"Sprint_Weapon"),secondaryFire=Clip(kind,"Shoot_Weapon"),
                    secondaryReload=Clip(kind,"Reload_Weapon"),
                    magazineSize=kind=="Robot" ? 8 : 30,reserveAmmo=kind=="Robot" ? 40 : 150,
                    damage=damage,interval=interval,heat=kind=="Robot" ? 22 : 11,automatic=true,
                    hipOffset=new Vector3(.06f,-.035f,.035f),aimOffset=new Vector3(-.06f,.025f,.04f),
                    shotSound=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Easy FPS/Sounds/shotSound.wav")
                };
                slot.idle.SampleAnimation(arms,0); slot.secondaryIdle.SampleAnimation(gun,0);
                // The source sample attaches the item to this animated hand/forearm bone.
                // A shared world origin leaves the gun upright and disconnected from the grip.
                string attachName=kind=="Robot" ? "Right_LowerArm_Attach" : "Right_Hand_Attach";
                var attachment=arms.GetComponentsInChildren<Transform>(true).FirstOrDefault(item=>item.name==attachName);
                if(!attachment) throw new InvalidOperationException("No weapon attachment bone: "+attachName);
                gun.transform.SetParent(attachment,false); gun.transform.localPosition=Vector3.zero;
                gun.transform.localRotation=Quaternion.identity; gun.transform.localScale=Vector3.one;
                var camera=arms.GetComponentsInChildren<Transform>(true).FirstOrDefault(item=>item.name=="Camera");
                if(camera)
                {
                    Quaternion inverse=Quaternion.Inverse(camera.rotation);
                    Vector3 cameraPosition=camera.position;
                    frame.localRotation=inverse; frame.localPosition=-(inverse*cameraPosition);
                }
                var armMat=Skin(kind);
                foreach(var renderer in arms.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(_=>armMat).ToArray();
                foreach(var renderer in gun.GetComponentsInChildren<Renderer>(true))
                {
                    var materials=renderer.sharedMaterials;
                    for(int i=0;i<materials.Length;i++) materials[i]=GunMaterial(kind,materials[i] ? materials[i].name : renderer.name);
                    renderer.sharedMaterials=materials;
                }
                foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                    if(renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen=true;
                }
                var binding=root.AddComponent<WeaponArtBinding>(); binding.specification=slot; binding.formatVersion=3;
                root.SetActive(true); PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static GameObject Model(string path,Transform parent)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!asset) throw new FileNotFoundException("Model was not imported: "+path);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            model.transform.localPosition=Vector3.zero; model.transform.localRotation=Quaternion.identity;
            foreach(var script in model.GetComponentsInChildren<MonoBehaviour>(true)) if(script) UnityEngine.Object.DestroyImmediate(script);
            foreach(var transform in model.GetComponentsInChildren<Transform>(true))
            { transform.gameObject.layer=0; GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject); }
            foreach(var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach(var camera in model.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(camera);
            return model;
        }

        static AnimationClip Clip(string kind,string action)
        {
            string path=Import+"/Animation/Characters/"+kind+"_1P/Clips/Firstperson--"+action+".anim.fbx";
            var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(item=>!item.name.StartsWith("__preview"));
            if(!clip) throw new FileNotFoundException("No imported animation: "+path);
            return clip;
        }

        static Material Skin(string kind)
        {
            string prefix=kind=="Robot" ? "Characters/Robot_A/Robot_A_Firstperson_" : "Characters/Terraformer_A/Terraformer_A_Firstperson_";
            return Pbr(kind+"_Arms",prefix+(kind=="Robot" ? "Diffuse" : "Albedo"),prefix+"Normal",prefix+(kind=="Robot" ? "Mask_Map" : "Mask_map"));
        }
        static Material GunMaterial(string kind,string name)
        {
            if(kind=="Robot") return Pbr("Robot_Gun","Weapons/Robot_Weapon_A/Robot_Weapon_A_Diffuse","Weapons/Robot_Weapon_A/Robot_Weapon_A_Normal","Weapons/Robot_Weapon_A/Robot_Weapon_A_Mask_Map");
            string part=name.IndexOf("glass",StringComparison.OrdinalIgnoreCase)>=0 ? "Glass_A_" :
                name.IndexOf("liquid",StringComparison.OrdinalIgnoreCase)>=0 ? "Liquids_A_" : name.IndexOf("container",StringComparison.OrdinalIgnoreCase)>=0 ? "Container_A_" : "";
            string prefix="Weapons/Terraformer_Weapon_A/Terraformer_Weapon_A_"+part;
            return Pbr("Terraformer_Gun_"+part,prefix+"BaseColor_A",prefix+"Normals",prefix+"MaskMap");
        }
        static Material Pbr(string name,string color,string normal,string mask)
        {
            string path=Output+"/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material) return material;
            material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.name=name;
            Texture texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Import+"/Textures/"+color+".tif");
            if(!texture) throw new FileNotFoundException("No PBR base texture: "+color);
            material.SetTexture("_BaseMap",texture); material.SetColor("_BaseColor",Color.white);
            texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Import+"/Textures/"+normal+".tif");
            if(texture) { material.SetTexture("_BumpMap",texture); material.EnableKeyword("_NORMALMAP"); }
            texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Import+"/Textures/"+mask+".tif");
            if(texture)
            {
                material.SetTexture("_MetallicGlossMap",texture); material.SetTexture("_OcclusionMap",texture);
                material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.EnableKeyword("_OCCLUSIONMAP");
            }
            material.SetFloat("_Metallic",.65f); material.SetFloat("_Smoothness",.55f);
            AssetDatabase.CreateAsset(material,path); return material;
        }

        public static void Install(ImitationArena arena)
        {
            var view=arena.GetComponent<FirstPersonPresentation>();
            if(!view.weaponPivot) { view.weaponPivot=new GameObject("First person weapon / procedural motion").transform; view.weaponPivot.SetParent(arena.eyes.transform,false); }
            foreach(var slot in view.weapons.ToArray())
                if(slot.id.StartsWith("UnityFpsSample.") || (slot.root && slot.root.name.Contains("Imported FPS arms")))
                { if(slot.root) UnityEngine.Object.DestroyImmediate(slot.root.gameObject); view.weapons.Remove(slot); }
            int first=view.weapons.Count;
            foreach(string kind in new[]{"Terraformer","Robot"})
            {
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Output+"/"+kind+".prefab");
                if(!asset) throw new FileNotFoundException("Missing authored weapon prefab: "+kind);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(asset,view.weaponPivot);
                var binding=instance.GetComponent<WeaponArtBinding>();
                var slot=binding.specification; slot.root=instance.transform; view.weapons.Add(slot);
            }
            view.Equip(first); EditorUtility.SetDirty(view);
        }
    }
}
