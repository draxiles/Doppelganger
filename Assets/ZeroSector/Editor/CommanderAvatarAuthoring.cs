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

namespace ZeroSector.Editor
{
    /// <summary>Build the third-person counterpart to the currently equipped first-person Terraformer.</summary>
    public static class CommanderAvatarAuthoring
    {
        const string Import = "Assets/ThirdParty/UnityFpsSample";
        const string ModelPath = Import + "/Models/Characters/Terraformer/Terraformer_A.fbx";
        const string RiflePath = Import + "/Models/Weapons/Terraformer_Weapon_A/Terraformer_Weapon_A_3P.fbx";
        const string TexturePath = Import + "/Textures/Characters/Terraformer_A/Terraformer_A_";
        const string Appearance = "Assets/ThirdParty/MakeHumanVetrov/";
        const string Output = "Assets/DoppelgangerGenerated/Characters";
        const string Materials = Output + "/CommanderMaterials";
        public const string PrefabPath = "Assets/Resources/Doppelganger/CommanderAvatar.prefab";
        const float StandingHeight = 1.84f;
        static readonly Color UniformTint = new(.19f,.24f,.29f);

        public static void PrepareAndPreview()
        {
            Prepare();
            var previous=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(previous.path))
                previous=EditorSceneManager.OpenScene(CampaignGame.ScenePaths[0],OpenSceneMode.Single);
            var portrait=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(portrait);
            var target=new RenderTexture(880,1000,24,RenderTextureFormat.ARGB32);
            Texture2D pixels=null;
            var previousTarget=RenderTexture.active;
            try
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var actor=(GameObject)PrefabUtility.InstantiatePrefab(prefab,portrait);
                foreach(var part in actor.GetComponentsInChildren<Transform>(true)) part.gameObject.layer=29;
                var face=Find(actor,"Алекс / Face").GetComponent<Renderer>();
                Vector3 center=face.bounds.center;
                var camera=new GameObject("Vetrov portrait camera",typeof(Camera)).GetComponent<Camera>();
                camera.transform.position=center+new Vector3(.37f,.085f,1.8f);
                camera.transform.LookAt(center+Vector3.down*.11f);
                camera.cullingMask=1<<29;
                camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.022f,.033f,.05f);
                camera.orthographic=true;
                camera.orthographicSize=.37f;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                RenderSettings.ambientMode=AmbientMode.Flat;
                RenderSettings.ambientLight=new Color(.31f,.36f,.41f);
                PreviewLight("Key",new Vector3(10,-174,0),new Color(1,.9f,.82f),2.6f);
                PreviewLight("Fill",new Vector3(16,145,0),new Color(.62f,.78f,1),.85f);
                PreviewLight("Rim",new Vector3(16,-28,0),new Color(.47f,.78f,1),2.0f);
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;
                pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
                pixels.Apply();
                string folder="Diagnostics/CampaignPreviews";
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder,"vetrov-human.png"),pixels.EncodeToPNG());
                int helmets=actor.GetComponentsInChildren<Renderer>().Count(part=>part.name.Contains("Helmet"));
                File.WriteAllText(Path.Combine(folder,"vetrov-human.txt"),
                    $"Face center {face.bounds.center:F3}; head bounds {face.bounds.size:F3}; skin lower edge {face.bounds.min.y:F3}.\n"+
                    $"Visible helmet renderers {helmets}.\n");
            }
            finally
            {
                RenderTexture.active=previousTarget;
                if(pixels) UnityEngine.Object.DestroyImmediate(pixels);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                EditorSceneManager.CloseScene(portrait,true);
                if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        static void PreviewLight(string name,Vector3 rotation,Color color,float intensity)
        {
            var light=new GameObject(name,typeof(Light)).GetComponent<Light>();
            light.type=LightType.Directional;
            light.transform.eulerAngles=rotation;
            light.cullingMask=1<<29;
            light.color=color;
            light.intensity=intensity;
            light.shadows=LightShadows.None;
        }

        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Prepare the commander avatar outside Play mode.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var rifleSource = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
            if (!source || !rifleSource) throw new FileNotFoundException("The original full-body Terraformer and third-person rifle must be imported.");
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var suitTexture=Texture("Body_Albedo");
            if(!suitTexture) throw new FileNotFoundException("Terraformer suit albedo is missing.");
            var suit=MatteMaterial("Suit",suitTexture,UniformTint);
            RetintFirstPersonSuit();

            var root = new GameObject("Алекс / штурмовая форма экипажа");
            root.SetActive(false);
            try
            {
                var model = InstantiateArt(source, root.transform);
                model.name = "Алекс / анимированный экипажный костюм";
                var animator = model.GetComponent<Animator>();
                if (!animator || !animator.avatar || !animator.avatar.isHuman || !animator.avatar.isValid)
                    throw new InvalidOperationException("The full-body commander needs its original valid Humanoid avatar.");
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.runtimeAnimatorController = null;
                animator.fireEvents = false;

                var bounds = BoundsOf(model);
                model.transform.localScale *= StandingHeight / bounds.size.y;
                bounds = BoundsOf(model);
                model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    string name = renderer.gameObject.name;
                    if(name.Contains("Backpack") || name.Contains("Helmet") || name.Contains("Hair") ||
                        name.Contains("Glass") || name.Contains("Eye") || name.Contains("Face") || name.Contains("Neck"))
                    {
                        UnityEngine.Object.DestroyImmediate(renderer);
                        continue;
                    }
                    Assign(renderer,new[]{suit});
                    if(renderer is SkinnedMeshRenderer skinned && name.Contains("Body")) FlattenChest(skinned);
                }
                AttachOpenFace(model,animator);
                AttachVest(model,animator);

                var grip = Find(model, "Right_Hand_Attach");
                if (!grip) throw new InvalidOperationException("The commander rifle attachment bone is missing.");
                var rifle = InstantiateArt(rifleSource, grip);
                rifle.name = "СКАТ / third-person rifle";
                var rifleAnimator = rifle.GetComponent<Animator>();
                if (rifleAnimator) UnityEngine.Object.DestroyImmediate(rifleAnimator);
                AssignRifle(rifle);
                var barrel = Find(rifle, "Bullit_Spawn");
                if (!barrel) throw new InvalidOperationException("The third-person rifle has no barrel marker.");
                var muzzle = new GameObject("Barrel / muzzle flash").transform;
                muzzle.SetParent(barrel,false);
                muzzle.rotation = Quaternion.LookRotation(rifle.transform.forward,rifle.transform.up);
                var muzzleFlash = muzzle.gameObject.AddComponent<Light>();
                muzzleFlash.type = LightType.Point;
                muzzleFlash.color = new Color(1,.69f,.31f);
                muzzleFlash.range = 1.9f;
                muzzleFlash.intensity = 3;
                muzzleFlash.shadows = LightShadows.None;
                muzzleFlash.enabled = false;
                var foregrip = new GameObject("Rifle / offhand grip").transform;
                foregrip.SetParent(rifle.transform,false);
                Vector3 barrelLocal = rifle.transform.InverseTransformPoint(barrel.position);
                foregrip.localPosition = new Vector3(barrelLocal.x,barrelLocal.y*.20f,barrelLocal.z*.54f);

                var motion = root.AddComponent<RobotPresentation>();
                motion.animator = animator;
                motion.model = model.transform;
                motion.standingHeight = StandingHeight;
                motion.walkClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Output+"/Animations/Adam_Walk_InPlace.anim");
                if (!motion.walkClip || !motion.walkClip.isHumanMotion)
                    throw new InvalidOperationException("The commander needs a humanoid walk cycle.");
                motion.walkMetersPerSecond = .76f;
                motion.soleHeightCorrection = 0;
                motion.twoHandedWeapon = true;
                motion.muzzle = muzzle;
                motion.foregrip = foregrip;
                root.AddComponent<CommanderAvatar>();
                root.SetActive(true);
                if (!PrefabUtility.SaveAsPrefabAsset(root, PrefabPath))
                    throw new InvalidOperationException("Unity did not save the shared commander prefab.");
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static GameObject InstantiateArt(GameObject asset, Transform parent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach (var component in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in instance.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in instance.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }
            return instance;
        }

        static Transform Find(GameObject instance,string name) =>
            instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(item=>item.name==name);

        static Bounds BoundsOf(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length==0) throw new InvalidOperationException("No visible commander geometry.");
            var bounds = renderers[0].bounds;
            for (int i=1;i<renderers.Length;i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.y<.01f || !float.IsFinite(bounds.size.y))
                throw new InvalidOperationException("Invalid bounds on the full-body commander.");
            return bounds;
        }

        static void Assign(Renderer renderer,UnityEngine.Material[] palette)
        {
            var slots = renderer.sharedMaterials;
            for (int i=0;i<slots.Length;i++) slots[i]=palette[Mathf.Min(i,palette.Length-1)];
            renderer.sharedMaterials=slots;
        }

        static void RetintFirstPersonSuit()
        {
            var material=AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/DoppelgangerGenerated/Weapons/Materials/Terraformer_Arms.mat");
            if(!material) return;
            ApplyMatteMaterial(material,material.GetTexture("_BaseMap"),UniformTint,0);
        }

        static void FlattenChest(SkinnedMeshRenderer renderer)
        {
            var mesh=UnityEngine.Object.Instantiate(renderer.sharedMesh);
            var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                Vector3 point=renderer.transform.TransformPoint(vertices[i]);
                float height=1-Mathf.Clamp01(Mathf.Abs(point.y-1.30f)/.24f);
                float width=1-Mathf.InverseLerp(.13f,.23f,Mathf.Abs(point.x));
                float blend=Mathf.SmoothStep(0,1,height)*width;
                point.z-=Mathf.Max(0,point.z-.037f)*blend*.85f;
                vertices[i]=renderer.transform.InverseTransformPoint(point);
            }
            mesh.vertices=vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            renderer.sharedMesh=SaveMesh(mesh,Output+"/VetrovUniformMesh.asset");
        }

        static Mesh SaveMesh(Mesh mesh,string path)
        {
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing)
            {
                EditorUtility.CopySerialized(mesh,existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh=existing;
            }
            else AssetDatabase.CreateAsset(mesh,path);
            return mesh;
        }

        static void AttachVest(GameObject model,Animator animator)
        {
            var chest=animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if(!chest) chest=animator.GetBoneTransform(HumanBodyBones.Chest);
            if(!chest) chest=Find(model,"Spine2");
            if(!chest) throw new InvalidOperationException("The commander needs a chest bone for the vest.");

            Vector3 center=new(0,1.235f,.070f);
            Vector3[] perimeter={
                new(-.122f,1.427f,.014f),new(-.216f,1.359f,-.012f),new(-.188f,1.152f,-.024f),
                new(-.124f,1.078f,.007f),new(.124f,1.078f,.007f),new(.188f,1.152f,-.024f),
                new(.216f,1.359f,-.012f),new(.122f,1.427f,.014f)
            };
            int count=perimeter.Length;
            var vertices=new Vector3[count*2+1];
            var triangles=new int[count*9];
            vertices[count*2]=chest.InverseTransformPoint(center);
            for(int i=0;i<count;i++)
            {
                int next=(i+1)%count;
                vertices[i]=chest.InverseTransformPoint(perimeter[i]);
                Vector3 bevel=Vector3.Lerp(perimeter[i],center,.14f);
                bevel.z=Mathf.Lerp(.047f,.071f,1-Mathf.Abs(bevel.x)/.22f);
                vertices[i+count]=chest.InverseTransformPoint(bevel);
                int index=i*9;
                triangles[index]=i;
                triangles[index+1]=next;
                triangles[index+2]=next+count;
                triangles[index+3]=i;
                triangles[index+4]=next+count;
                triangles[index+5]=i+count;
                triangles[index+6]=i+count;
                triangles[index+7]=next+count;
                triangles[index+8]=count*2;
            }
            var shell=new Mesh {name="Vetrov / single-piece plate",vertices=vertices,triangles=triangles};
            shell.RecalculateNormals();
            shell=SaveMesh(shell,Output+"/VetrovVestMesh.asset");
            AttachMesh(chest,"Vest",shell,MatteMaterial("Vetrov_Vest",null,new Color(.16f,.20f,.23f)));
        }

        static void AttachOpenFace(GameObject model,Animator animator)
        {
            var bone=animator.GetBoneTransform(HumanBodyBones.Head);
            if(!bone) bone=Find(model,"Head");
            if(!bone) throw new InvalidOperationException("The commander needs a bound head bone.");
            var faceMesh=AppearanceMesh("Face");
            var root=new GameObject("Алекс / открытое мужское лицо").transform;
            bool reverse=faceMesh.bounds.max.z < -faceMesh.bounds.min.z;
            Quaternion rotation=model.transform.rotation*Quaternion.Euler(0,reverse ? 180 : 0,0);
            root.SetPositionAndRotation(bone.position+model.transform.forward*.005f,rotation);
            root.localScale=Vector3.one*(.254f/faceMesh.bounds.size.y);
            root.SetParent(bone,true);

            var skinTexture=AppearanceTexture("Skin");
            var skin=MatteMaterial("Vetrov_Skin",skinTexture,new Color(.98f,.94f,.89f));
            var hair=MatteMaterial("Vetrov_HairStrands",AppearanceTexture("HairAlbedo"),new Color(.66f,.56f,.47f),.38f);
            var sclera=MatteMaterial("Vetrov_Sclera",null,new Color(.46f,.43f,.39f));
            var iris=MatteMaterial("Vetrov_Iris",null,new Color(.11f,.15f,.13f));
            var pupil=MatteMaterial("Vetrov_Pupil",null,new Color(.021f,.026f,.024f));
            AttachMesh(root,"Face",faceMesh,skin);
            AttachMesh(root,"HairCards",AppearanceMesh("HairCards"),hair);
            AttachMesh(root,"Sclera",AppearanceMesh("Sclera"),sclera);
            AttachMesh(root,"Iris",AppearanceMesh("Iris"),iris);
            AttachMesh(root,"Pupil",AppearanceMesh("Pupil"),pupil);
        }

        static UnityEngine.Material MatteMaterial(string name,Texture texture,Color tint,float cutoff=0)
        {
            string path=Materials+"/"+name+".mat";
            var shader=Shader.Find("Doppelganger/Character Matte");
            if(!shader) throw new InvalidOperationException("The matte character shader is missing.");
            var material=AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
            if(!material)
            {
                material=new UnityEngine.Material(shader) {name=name};
                AssetDatabase.CreateAsset(material,path);
            }
            ApplyMatteMaterial(material,texture,tint,cutoff);
            return material;
        }

        static void ApplyMatteMaterial(UnityEngine.Material material,Texture texture,Color tint,float cutoff)
        {
            var shader=Shader.Find("Doppelganger/Character Matte");
            if(!shader) throw new InvalidOperationException("The matte character shader is missing.");
            material.shader=shader;
            material.SetTexture("_BaseMap",texture ? texture : Texture2D.whiteTexture);
            material.SetColor("_BaseColor",tint);
            material.SetFloat("_Cutoff",cutoff);
            material.renderQueue=cutoff>0 ? (int)RenderQueue.AlphaTest : -1;
            EditorUtility.SetDirty(material);
        }

        static Mesh AppearanceMesh(string name)
        {
            string path=Appearance+"Vetrov"+name+".obj";
            var mesh=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();
            if(!mesh || mesh.bounds.size.y<=.001f) throw new FileNotFoundException("Missing commander mesh: "+path);
            return mesh;
        }

        static Texture2D AppearanceTexture(string name)
        {
            string path=Appearance+"Vetrov"+name+".png";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(!texture) throw new FileNotFoundException("Missing commander texture: "+path);
            return texture;
        }

        static void AttachMesh(Transform parent,string name,Mesh mesh,UnityEngine.Material material)
        {
            var part=new GameObject("Алекс / "+name,typeof(MeshFilter),typeof(MeshRenderer));
            part.transform.SetParent(parent,false);
            part.GetComponent<MeshFilter>().sharedMesh=mesh;
            part.GetComponent<MeshRenderer>().sharedMaterial=material;
        }

        static void AssignRifle(GameObject rifle)
        {
            var main=AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/DoppelgangerGenerated/Weapons/Materials/Terraformer_Gun_.mat");
            if (!main) throw new FileNotFoundException("The first-person rifle's shared PBR material is missing.");
            var palette=new[]{
                main,
                RifleMaterial("Glass","Glass_A_",.08f,.84f),
                RifleMaterial("Liquid","Liquids_A_",.05f,.61f),
                RifleMaterial("Container","Container_A_",.38f,.42f)
            };
            foreach (var renderer in rifle.GetComponentsInChildren<Renderer>(true)) Assign(renderer,palette);
        }

        static UnityEngine.Material RifleMaterial(string name,string prefix,float metallic,float smoothness)
        {
            var material=Material("Rifle_"+name,null,Color.white,metallic,smoothness);
            string source=Import+"/Textures/Weapons/Terraformer_Weapon_A/Terraformer_Weapon_A_"+prefix;
            var color=AssetDatabase.LoadAssetAtPath<Texture2D>(source+"BaseColor_A.tif");
            var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(source+"Normals.tif");
            if (!color) throw new FileNotFoundException("No rifle texture: "+source+"BaseColor_A.tif");
            material.SetTexture("_BaseMap",color);
            material.SetTexture("_BumpMap",normal);
            if (normal) material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        static UnityEngine.Material Material(string name,string texture,Color fallback,float metallic,float smoothness)
        {
            string path = Materials+"/"+name+".mat";
            var material = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
            if (!material)
            {
                material = new UnityEngine.Material(Shader.Find("Universal Render Pipeline/Lit")) { name="Terraformer / "+name };
                AssetDatabase.CreateAsset(material,path);
            }
            var albedo=texture==null ? null : Texture(texture+"_Albedo");
            var normal=texture==null ? null : Texture(texture+"_Normal");
            var emission=texture==null ? null : Texture(texture+"_Emissive");
            material.SetColor("_BaseColor",albedo ? Color.white : fallback);
            material.SetTexture("_BaseMap",albedo);
            material.SetFloat("_Metallic",metallic);
            material.SetFloat("_Smoothness",smoothness);
            material.SetTexture("_BumpMap",normal);
            if (normal) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
            material.SetTexture("_EmissionMap",emission);
            material.SetColor("_EmissionColor",emission ? new Color(.3f,.64f,.75f) : Color.black);
            if (emission) material.EnableKeyword("_EMISSION"); else material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D Texture(string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath+name+".png") ??
            AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath+name+".tif");

    }
}
