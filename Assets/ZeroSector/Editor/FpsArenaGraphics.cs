using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ZeroSector.Learning;

namespace ZeroSector.Editor
{
    public sealed class FpsArenaGraphics : EditorWindow
    {
        const string Output="Assets/DoppelgangerGenerated/FPSArena";
        GameObject source;
        Vector2 scroll;
        AnimationClip idle,walk,run,fire,inspect;

        public static void Upgrade()
        {
            if(EditorApplication.isPlaying) { Debug.LogWarning("Сначала останови Play."); return; }
            var arena=UnityEngine.Object.FindAnyObjectByType<ImitationArena>();
            if(!arena) { Debug.LogWarning("Открой LearningArena, затем повтори Graphics > Create FPS Arena."); return; }
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Output+"/Materials"); AssetDatabase.Refresh();
            var scene=arena.gameObject.scene;
            // Save to a new scene before changing art. The original test arena stays available.
            EditorSceneManager.SaveScene(scene,Output+"/FPSArena.unity");
            var old=GameObject.Find("FPS lighting and set dressing");
            if(old) DestroyImmediate(old);
            var set=new GameObject("FPS lighting and set dressing").transform;
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>())
                if(!light.transform.IsChildOf(arena.doubleBody.transform) && !light.transform.IsChildOf(arena.player.transform)) DestroyImmediate(light.gameObject);
            var view=arena.GetComponent<FirstPersonPresentation>();
            if(!view) view=arena.gameObject.AddComponent<FirstPersonPresentation>();
            view.body=arena.player; view.eyes=arena.eyes; arena.presentation=view;
            arena.eyes.allowHDR=true; arena.eyes.allowDynamicResolution=false;
            var data=arena.eyes.GetUniversalAdditionalCameraData();
            data.renderPostProcessing=true; data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality=AntialiasingQuality.High;
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.12f,.18f,.26f);
            RenderSettings.ambientEquatorColor=new Color(.075f,.1f,.14f);
            RenderSettings.ambientGroundColor=new Color(.035f,.045f,.065f);
            RenderSettings.fog=false;
            var main=Light(set,"Cold key",LightType.Directional,new Vector3(0,3,0),new Color(.68f,.8f,1),.65f,40);
            main.transform.rotation=Quaternion.Euler(52,-28,0); main.shadows=LightShadows.Soft;
            for(int x=-6;x<=6;x+=12)
                for(int z=-6;z<=6;z+=12)
                {
                    var lamp=Light(set,"Ceiling pool",LightType.Spot,new Vector3(x,3.7f,z),new Color(.65f,.82f,1),14,15);
                    lamp.transform.rotation=Quaternion.Euler(90,0,0); lamp.spotAngle=110; lamp.innerSpotAngle=75;
                    // Two shadowed spotlights keep cost bounded on the laptop.
                    lamp.shadows=x==z ? LightShadows.Soft : LightShadows.None;
                }
            Light(set,"Warm west rim",LightType.Point,new Vector3(-10,2,1),new Color(1,.35f,.1f),5,13);
            Light(set,"Cyan east rim",LightType.Point,new Vector3(10,2,-1),new Color(.1f,.65f,1),5,13);
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Output+"/Atmosphere.asset");
            if(!profile)
            {
                profile=CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,Output+"/Atmosphere.asset");
                var tone=profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
                var bloom=profile.Add<Bloom>(true); bloom.threshold.Override(1.2f); bloom.intensity.Override(.2f); bloom.scatter.Override(.55f);
                var grade=profile.Add<ColorAdjustments>(true); grade.contrast.Override(7); grade.saturation.Override(4); grade.postExposure.Override(.25f);
                var vignette=profile.Add<Vignette>(true); vignette.intensity.Override(.16f); vignette.smoothness.Override(.45f);
                foreach(var component in profile.components) AssetDatabase.AddObjectToAsset(component,profile);
            }
            var volume=new GameObject("Restrained bloom / ACES",typeof(Volume)).GetComponent<Volume>();
            volume.transform.SetParent(set); volume.isGlobal=true; volume.priority=5; volume.sharedProfile=profile;
            // Imported fixtures provide visible light sources without substituting primitive meshes.
            for(int x=-6;x<=6;x+=12)
                for(int z=-1;z<=1;z+=2)
                    Fixture(set,new Vector3(x,2.6f,z*11.7f),z>0 ? 180 : 0);
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline) { pipeline.renderScale=1; pipeline.shadowDistance=40; EditorUtility.SetDirty(pipeline); }
            EditorUtility.SetDirty(arena); EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject=arena.gameObject;
            Debug.Log("FPSArena сохранена. Shift — бег, ПКМ — приближение, C — присед, F — осмотр подключённой модели, H — диагностика. Для рук: Graphics > Connect FPS Model.");
        }

        static Light Light(Transform parent,string name,LightType type,Vector3 position,Color color,float intensity,float range)
        {
            var light=new GameObject(name,typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(parent); light.transform.position=position;
            light.type=type; light.color=color; light.intensity=intensity; light.range=range;
            light.shadowBias=.035f; light.shadowNormalBias=.25f; return light;
        }

        static void Fixture(Transform parent,Vector3 position,float yaw)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sci-Fi Styled Modular Pack/Prefabs/Lights/light_wall_1_blue.prefab");
            if(!prefab) return;
            var root=new GameObject("Imported wall luminaire").transform; root.SetParent(parent);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var light in model.GetComponentsInChildren<Light>(true)) DestroyImmediate(light);
            foreach(var collider in model.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            var renderers=model.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0) { DestroyImmediate(root.gameObject); return; }
            Bounds bounds=renderers[0].bounds;
            foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float scale=.85f/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
            root.localScale=Vector3.one*scale;
            model.transform.position-=bounds.center*scale;
            root.rotation=Quaternion.Euler(0,yaw,0); root.position=position;
            ConvertMaterials(model);
        }

        static void Open() { var window=GetWindow<FpsArenaGraphics>("FPS model"); window.minSize=new Vector2(540,470); }

        public static void ConnectEasyPreview()
        {
            if(EditorApplication.isPlaying) { Debug.LogWarning("Останови Play: подключение сохраняется в сцене, а не в игровом запуске."); return; }
            var window=GetWindow<FpsArenaGraphics>("FPS model");
            window.source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Easy FPS/Resources/NewGun_auto.prefab");
            window.idle=window.walk=window.run=window.fire=window.inspect=null;
            if(!window.source) { Debug.LogWarning("Сначала импортируй Easy FPS."); return; }
            window.Connect();
        }

        public static void AddEasyPreviewTo(ImitationArena target)
        {
            var tool=CreateInstance<FpsArenaGraphics>();
            tool.source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Easy FPS/Resources/NewGun_auto.prefab");
            if(tool.source) tool.Connect(target);
            DestroyImmediate(tool);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Подключение настоящих рук и оружия к нашему контроллеру. Выбери prefab с руками, оружием и их скелетом. Готовый FPS-контроллер из набора заменяется нашим. Результат нужно проверить в Game и подогнать положение в Inspector.",MessageType.Info);
            if(EditorApplication.isPlaying) EditorGUILayout.HelpBox("Сначала останови Play. Во время игры подключение отключено, чтобы модель сохранилась в сцене.",MessageType.Warning);
            if(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Easy FPS/Resources/NewGun_auto.prefab"))
                using(new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                    if(GUILayout.Button("Подключить готовые руки Easy FPS одной кнопкой")) ConnectEasyPreview();
            source=(GameObject)EditorGUILayout.ObjectField("Arms + weapon prefab",source,typeof(GameObject),false);
            idle=Clip("Idle",idle); walk=Clip("Walk",walk); run=Clip("Run",run); fire=Clip("Fire",fire); inspect=Clip("Inspect (optional)",inspect);
            EditorGUILayout.LabelField("Если клипы не указаны, ищем их в Animator/Animation выбранного prefab.",EditorStyles.wordWrappedMiniLabel);
            using(new EditorGUI.DisabledScope(!source || EditorApplication.isPlaying))
                if(GUILayout.Button("Добавить выбранное оружие в арсенал",GUILayout.Height(36))) Connect();
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Найденные FPS prefab в проекте",EditorStyles.boldLabel);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            int shown=0;
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid), lower=path.ToLowerInvariant();
                if(lower.Contains("doppelgangergenerated") || lower.Contains("sci-fi styled")) continue;
                if(!new[]{"fps","weapon","arms","rifle","handgun"}.Any(lower.Contains)) continue;
                var candidate=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(!candidate || !candidate.GetComponentInChildren<SkinnedMeshRenderer>(true) || !candidate.GetComponentInChildren<Animator>(true)) continue;
                if(GUILayout.Button(path,EditorStyles.miniButton)) source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(++shown>=40) break;
            }
            if(shown==0) EditorGUILayout.HelpBox("Набор рук/оружия ещё не импортирован. Импортируй Easy FPS или другой выбранный FPS-набор.",MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        static AnimationClip Clip(string label,AnimationClip value) => (AnimationClip)EditorGUILayout.ObjectField(label,value,typeof(AnimationClip),false);

        void Connect(ImitationArena target=null)
        {
            if(EditorApplication.isPlaying || !source) return;
            var arena=target ? target : UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root=>root.GetComponentsInChildren<ImitationArena>()).FirstOrDefault();
            if(!arena) { Debug.LogWarning("Открой арену."); return; }
            var view=arena.GetComponent<FirstPersonPresentation>();
            if(!view) view=Undo.AddComponent<FirstPersonPresentation>(arena.gameObject);
            Undo.RecordObject(arena,"Connect FPS model"); Undo.RecordObject(view,"Connect FPS model");
            arena.presentation=view; view.eyes=arena.eyes; view.body=arena.player;
            if(!view.weaponPivot)
            { view.weaponPivot=new GameObject("First person weapon / procedural motion").transform; view.weaponPivot.SetParent(arena.eyes.transform,false); }
            var holder=new GameObject("Imported FPS arms and weapon"); holder.SetActive(false);
            holder.transform.SetParent(view.weaponPivot,false);
            Undo.RegisterCreatedObjectUndo(holder,"Connect FPS model");
            var model=(GameObject)PrefabUtility.InstantiatePrefab(source,holder.transform);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            // Asset prefabs can carry positions from their demo scene (Easy FPS had x=18.47).
            model.transform.localPosition=Vector3.zero;
            model.transform.localRotation=Quaternion.identity;
            Vector3 restOffset=Vector3.zero, sightOffset=new Vector3(-.12f,.035f,.06f);
            foreach(var script in model.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(!script) continue;
                var settings=new SerializedObject(script);
                var rest=settings.FindProperty("restPlacePosition");
                var aim=settings.FindProperty("aimPlacePosition");
                if(rest!=null && aim!=null)
                { restOffset=rest.vector3Value; sightOffset=aim.vector3Value-restOffset; }
            }
            var authoredCamera=model.GetComponentInChildren<Camera>(true);
            if(authoredCamera)
            {
                // Keep the original model-to-camera relationship instead of guessing the FBX axes.
                Vector3 p=holder.transform.InverseTransformPoint(authoredCamera.transform.position);
                Quaternion r=Quaternion.Inverse(holder.transform.rotation)*authoredCamera.transform.rotation;
                model.transform.localPosition=Quaternion.Inverse(r)*(model.transform.localPosition-p);
                model.transform.localRotation=Quaternion.Inverse(r)*model.transform.localRotation;
            }
            var animators=model.GetComponentsInChildren<Animator>(true);
            var legacy=model.GetComponentInChildren<Animation>(true);
            var available=new List<AnimationClip>();
            foreach(var animator in animators)
                if(animator.runtimeAnimatorController) available.AddRange(animator.runtimeAnimatorController.animationClips);
            if(legacy) available.AddRange(AnimationUtility.GetAnimationClips(legacy.gameObject));
            foreach(var script in model.GetComponentsInChildren<MonoBehaviour>(true)) if(script) DestroyImmediate(script);
            foreach(var transform in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            foreach(var controller in model.GetComponentsInChildren<CharacterController>(true)) DestroyImmediate(controller);
            foreach(var collider in model.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach(var rigidbody in model.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rigidbody);
            foreach(var camera in model.GetComponentsInChildren<Camera>(true)) DestroyImmediate(camera);
            foreach(var listener in model.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(listener);
            foreach(var audio in model.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(audio);
            foreach(var light in model.GetComponentsInChildren<Light>(true)) DestroyImmediate(light);
            foreach(var canvas in model.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
            foreach(var particles in model.GetComponentsInChildren<ParticleSystem>(true)) particles.gameObject.SetActive(false);
            view.weaponAnimator=animators.FirstOrDefault();
            var legacyRoot=legacy ? legacy.gameObject : null;
            if(legacy) DestroyImmediate(legacy);
            if(!view.weaponAnimator && legacyRoot) view.weaponAnimator=legacyRoot.AddComponent<Animator>();
            view.idleClip=PrepareClip(idle ? idle : Find(available,"idle","pose"));
            view.walkClip=PrepareClip(walk ? walk : Find(available,"walk"));
            view.runClip=PrepareClip(run ? run : Find(available,"run","sprint"));
            view.fireClip=PrepareClip(fire ? fire : Find(available,"fire","shoot","shot"));
            view.inspectClip=PrepareClip(inspect ? inspect : Find(available,"inspect"));
            if(view.weaponAnimator && view.idleClip) view.idleClip.SampleAnimation(view.weaponAnimator.gameObject,0);
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            { renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false; }
            ConvertMaterials(model);
            string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            int slotIndex=view.weapons.FindIndex(slot=>slot.id==id);
            if(slotIndex>=0 && view.weapons[slotIndex].root) Undo.DestroyObjectImmediate(view.weapons[slotIndex].root.gameObject);
            var slot=new FirstPersonPresentation.WeaponSlot {id=id,title=source.name,root=holder.transform,animator=view.weaponAnimator,
                idle=view.idleClip,walk=view.walkClip,run=view.runClip,fire=view.fireClip,inspect=view.inspectClip,
                hipOffset=restOffset,aimOffset=sightOffset,shotSound=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Easy FPS/Sounds/shotSound.wav"),
                automatic=!source.name.ToLowerInvariant().Contains("semi")};
            if(slotIndex<0) { slotIndex=view.weapons.Count; view.weapons.Add(slot); } else view.weapons[slotIndex]=slot;
            foreach(var transform in holder.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer=0;
            view.Equip(slotIndex); holder.SetActive(true);
            EditorUtility.SetDirty(arena); EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(arena.gameObject.scene); AssetDatabase.SaveAssets();
            Selection.activeGameObject=holder;
            Debug.Log("FPS model connected. Проверь кадр в Game. Hip Offset / Aim Offset на FirstPersonPresentation требуют подгонки под конкретную модель. Ctrl+Z отменяет подключение.");
        }

        static AnimationClip Find(List<AnimationClip> clips,params string[] terms)
            => clips.FirstOrDefault(clip=>clip && terms.Any(term=>clip.name.ToLowerInvariant().Contains(term)));

        static AnimationClip PrepareClip(AnimationClip clip)
        {
            if(!clip || !clip.legacy) return clip;
            Directory.CreateDirectory(Output+"/Animations"); AssetDatabase.Refresh();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip,out string guid,out long localId);
            string path=Output+"/Animations/"+clip.name.Replace('/','_')+"_"+guid+"_"+localId+".anim";
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(existing) return existing;
            var copy=Instantiate(clip); copy.legacy=false; AssetDatabase.CreateAsset(copy,path); return copy;
        }

        public static void ConvertMaterials(GameObject model,string output=Output)
        {
            Directory.CreateDirectory(output+"/Materials"); AssetDatabase.Refresh();
            var converted=new Dictionary<Material,Material>();
            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var slots=renderer.sharedMaterials;
                for(int i=0;i<slots.Length;i++)
                {
                    var src=slots[i]; if(!src) continue;
                    if(!converted.TryGetValue(src,out var dst))
                    {
                        if(src.shader && src.shader.name.StartsWith("Universal Render Pipeline/")) { converted[src]=src; continue; }
                        string id=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(src));
                        string path=output+"/Materials/"+src.name.Replace('/','_')+"_"+id+".mat";
                        dst=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(!dst)
                        {
                            dst=new Material(Shader.Find("Universal Render Pipeline/Lit")); dst.name=src.name+" URP";
                            if(src.HasProperty("_Color")) dst.SetColor("_BaseColor",src.GetColor("_Color"));
                            CopyTexture(src,dst,"_MainTex","_BaseMap"); CopyTexture(src,dst,"_BumpMap","_BumpMap");
                            CopyTexture(src,dst,"_MetallicGlossMap","_MetallicGlossMap"); CopyTexture(src,dst,"_OcclusionMap","_OcclusionMap");
                            CopyTexture(src,dst,"_EmissionMap","_EmissionMap");
                            if(src.HasProperty("_Metallic")) dst.SetFloat("_Metallic",src.GetFloat("_Metallic"));
                            dst.SetFloat("_Smoothness",src.HasProperty("_Glossiness") ? src.GetFloat("_Glossiness") : .3f);
                            if(dst.GetTexture("_BumpMap")) dst.EnableKeyword("_NORMALMAP");
                            if(src.HasProperty("_BumpScale")) dst.SetFloat("_BumpScale",src.GetFloat("_BumpScale"));
                            if(dst.GetTexture("_MetallicGlossMap")) dst.EnableKeyword("_METALLICSPECGLOSSMAP");
                            if(src.IsKeywordEnabled("_EMISSION") && src.HasProperty("_EmissionColor"))
                            { dst.EnableKeyword("_EMISSION"); dst.SetColor("_EmissionColor",src.GetColor("_EmissionColor")); }
                            if(src.HasProperty("_Mode") && src.GetFloat("_Mode")>=2)
                            {
                                dst.SetFloat("_Surface",1); dst.SetFloat("_SrcBlend",(int)BlendMode.SrcAlpha);
                                dst.SetFloat("_DstBlend",(int)BlendMode.OneMinusSrcAlpha); dst.SetFloat("_ZWrite",0);
                                dst.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); dst.SetOverrideTag("RenderType","Transparent"); dst.renderQueue=3000;
                            }
                            AssetDatabase.CreateAsset(dst,path);
                        }
                        converted[src]=dst;
                    }
                    slots[i]=dst;
                }
                renderer.sharedMaterials=slots;
            }
        }

        static void CopyTexture(Material src,Material dst,string from,string to)
        {
            if(!src.HasProperty(from) || !src.GetTexture(from)) return;
            dst.SetTexture(to,src.GetTexture(from)); dst.SetTextureScale(to,src.GetTextureScale(from)); dst.SetTextureOffset(to,src.GetTextureOffset(from));
        }
    }
}
