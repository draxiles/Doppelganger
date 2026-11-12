using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    public static class AdamRobotAuthoring
    {
        const string Stage = "ThirdPartyStaging/AdamCharacterPack/Assets/UnityTechnologies/Adam Character Pack";
        const string StoreImport = "Assets/UnityTechnologies/Adam Character Pack";
        const string Import = "Assets/ThirdParty/AdamCharacterPack";
        const string Output = "Assets/DoppelgangerGenerated/Characters";
        const string PulseCannonSource = "Assets/ThirdParty/UnityFpsSample/Models/Weapons/Robot_Weapon_A/Robot_Weapon_A.fbx";
        const string PulseCannonFinish = "Assets/DoppelgangerGenerated/Weapons/Materials/Robot_Gun.mat";
        const string PulseCannonEmission = "Assets/ThirdParty/UnityFpsSample/Textures/Weapons/Robot_Weapon_A/Robot_Weapon_A_Emissive.tif";
        public const string PrefabPath = Output + "/AdamRobot.prefab";

        /// <summary>The acquired ADAM pack is restricted to personal/non-commercial use.</summary>
        public static GameObject Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("ADAM authoring must run outside Play mode.");
            string importRoot = ImportRoot();
            Directory.CreateDirectory(Output);
            if (importRoot == Import)
            {
                if (!Directory.Exists(Stage)) throw new DirectoryNotFoundException("Download/import the official ADAM Character Pack first.");
                Directory.CreateDirectory(Import);
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (string source in Directory.GetFiles(Stage, "*", SearchOption.AllDirectories))
                    {
                        string relative = source.Substring(Stage.Length + 1).Replace('\\', '/');
                        // Only the requested robot: no demo scenes, pipelines, controllers or package scripts.
                        if (relative != "license.txt" && !relative.StartsWith("Adam/")) continue;
                        if (relative.Contains("LOD_") || relative.Contains("Crowd_") || relative.EndsWith(".controller") || relative.EndsWith(".controller.meta")) continue;
                        string destination = Import + "/" + relative;
                        if (File.Exists(destination)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        if (source.EndsWith(".tif.meta", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".psd.meta", StringComparison.OrdinalIgnoreCase))
                        {
                            string metadata = Regex.Replace(File.ReadAllText(source), @"maxTextureSize: \d+", "maxTextureSize: 2048");
                            File.WriteAllText(destination, metadata);
                        }
                        else File.Copy(source, destination);
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string modelPath = importRoot + "/Adam/Adam.FBX", walkPath = importRoot + "/Adam/Adam_Walk.FBX";
            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (!sourceModel) throw new InvalidOperationException("ADAM.FBX could not be imported.");
            var importedWalk = MainClip(walkPath);
            if (!importedWalk) throw new InvalidOperationException("ADAM_Walk.FBX has no animation clip.");
            var sourceAnimator = sourceModel.GetComponent<Animator>();
            var avatar = sourceAnimator ? sourceAnimator.avatar : null;
            if (!avatar || !avatar.isValid || !avatar.isHuman || !importedWalk.isHumanMotion)
                throw new InvalidOperationException("ADAM requires its valid humanoid avatar and humanoid walk clip.");
            // Generated animation copies own our loop/root settings. The purchased pack stays intact.
            var walk = LoopCopy(importedWalk, "Adam_Walk_InPlace");
            string idlePath = importRoot + "/Guard/Guard_Idle.FBX";
            var sourceIdle = File.Exists(idlePath) ? MainClip(idlePath) : null;
            var idle = sourceIdle && sourceIdle.isHumanMotion ? LoopCopy(sourceIdle, "Adam_CombatIdle") : null;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing)
            {
                // Upgrade the existing prefab instead of silently keeping the old frozen presentation.
                // Editing its contents preserves bone and prefab references in all three scenes.
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var view = contents.GetComponent<RobotPresentation>();
                    if (!view || !view.animator || !view.model)
                        throw new InvalidOperationException("The generated ADAM prefab is missing its animation rig.");
                    ConfigureRig(view, avatar, idle, walk, importedWalk);
                    AttachGuardWeapon(view);
                    RestoreGuardMaterials(view);
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    AssetDatabase.SaveAssets();
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                WriteReport(modelPath, walkPath, idlePath, avatar, idle, walk);
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            var root = new GameObject("ADAM / Двойник"); root.SetActive(false);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(sourceModel, root.transform);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "ADAM / original rigged robot";
                model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
                var animator = model.GetComponent<Animator>(); if (!animator) animator = model.AddComponent<Animator>();
                animator.avatar = avatar;
                walk.SampleAnimation(model, 0);
                Bounds bounds = BoundsOf(model);
                if (bounds.size.y < .01f) throw new InvalidOperationException("ADAM has no visible body bounds.");
                float scale = 1.8f / bounds.size.y; model.transform.localScale *= scale;
                bounds = BoundsOf(model); model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                    if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
                }
                FpsArenaGraphics.ConvertMaterials(model, Output + "/Adam");
                CloneMaterials(model);
                var view = root.AddComponent<RobotPresentation>();
                view.animator = animator; view.model = model.transform; view.standingHeight = 1.8f;
                ConfigureRig(view, avatar, idle, walk, importedWalk);
                Transform Bone(string name) => model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                view.hips = Bone("Bip01");
                view.leftUpperLeg = Bone("Bip01 L Thigh"); view.leftLowerLeg = Bone("Bip01 L Calf"); view.leftFoot = Bone("Bip01 L Foot");
                view.rightUpperLeg = Bone("Bip01 R Thigh"); view.rightLowerLeg = Bone("Bip01 R Calf"); view.rightFoot = Bone("Bip01 R Foot");
                view.rightHand = Bone("Bip01 R Hand");
                var muzzle = new GameObject("Wrist emitter / visual origin").transform;
                muzzle.SetParent(view.rightHand ? view.rightHand : model.transform, false);
                muzzle.localPosition = new Vector3(0, .06f, .08f); view.muzzle = muzzle;
                var flash = muzzle.gameObject.AddComponent<Light>(); flash.type = LightType.Point;
                flash.color = new Color(.15f, .7f, 1); flash.intensity = 4; flash.range = 2; flash.enabled = false;
                flash.shadows = LightShadows.None;
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                foreach (var script in model.GetComponentsInChildren<MonoBehaviour>(true)) if (script) UnityEngine.Object.DestroyImmediate(script);
                AttachGuardWeapon(view);
                RestoreGuardMaterials(view);
                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                WriteReport(modelPath, walkPath, idlePath, avatar, idle, walk);
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static string ImportRoot() => File.Exists(StoreImport + "/Adam/Adam.FBX") ? StoreImport : Import;

        public static void UpgradeGuardPresentation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var existing = prefab ? prefab.GetComponent<RobotPresentation>() : null;
            if (!existing) return;
            bool restoredMaterials = RestoreGuardMaterials(existing);
            if (existing.guardCannon)
            {
                if (restoredMaterials) AssetDatabase.SaveAssets();
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                bool attached = AttachGuardWeapon(root.GetComponent<RobotPresentation>());
                if (attached) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (!attached && !restoredMaterials) return;
                AssetDatabase.SaveAssets();
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static bool RestoreGuardMaterials(RobotPresentation view)
        {
            string originals = ImportRoot() + "/Adam/Materials/";
            const string generated = Output + "/Adam/Materials/";
            bool changed = false;
            foreach (var material in view.model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct())
            {
                if (!material) continue;
                string path=AssetDatabase.GetAssetPath(material);
                if (!path.StartsWith(generated,StringComparison.Ordinal)) continue;
                string name = Regex.Replace(Path.GetFileNameWithoutExtension(path),@"_[a-f0-9]{32}$","");
                var source = AssetDatabase.LoadAssetAtPath<Material>(originals + name + ".mat");
                if (!source || source.shader != material.shader || !source.HasProperty("_BaseMap")) continue;
                var albedo = source.GetTexture("_BaseMap");
                var normal = source.GetTexture("_BumpMap");
                var occlusion = source.GetTexture("_OcclusionMap");
                var emission = source.GetTexture("_EmissionMap");
                if (!albedo && !emission) continue;
                if (albedo == material.GetTexture("_BaseMap") && emission == material.GetTexture("_EmissionMap")
                    && material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF")) continue;
                // FBX fallback slots have flat swatches. The corresponding URP
                // materials from the pack contain the correct UV-bound surface maps.
                // Keep a plain opaque URP variant so isolated cutscene cameras
                // do not inherit the old demo's specular and probe settings.
                material.shaderKeywords=Array.Empty<string>();
                material.SetOverrideTag("RenderType","Opaque");
                material.renderQueue=(int)RenderQueue.Geometry;
                material.SetFloat("_Surface",0);
                material.SetFloat("_AlphaClip",0);
                material.SetFloat("_ZWrite",1);
                material.SetFloat("_SrcBlend",(float)BlendMode.One);
                material.SetFloat("_DstBlend",(float)BlendMode.Zero);
                material.SetTexture("_BaseMap",albedo);
                material.SetTexture("_BumpMap",normal);
                material.SetTexture("_OcclusionMap",occlusion);
                material.SetTexture("_EmissionMap",emission);
                material.SetTexture("_SpecGlossMap",null);
                material.SetColor("_BaseColor",source.GetColor("_BaseColor"));
                material.SetColor("_EmissionColor",emission ? source.GetColor("_EmissionColor") : Color.black);
                material.SetFloat("_WorkflowMode",1);
                material.SetFloat("_EnvironmentReflections",0);
                material.SetFloat("_Metallic",name.EndsWith("_metal",StringComparison.OrdinalIgnoreCase) ? .65f : .06f);
                material.SetFloat("_Smoothness",name.EndsWith("_rubber",StringComparison.OrdinalIgnoreCase) ? .25f : .48f);
                if (normal) material.EnableKeyword("_NORMALMAP");
                if (occlusion) material.EnableKeyword("_OCCLUSIONMAP");
                material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                material.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                changed = true;
            }
            return changed;
        }

        static bool AttachGuardWeapon(RobotPresentation view)
        {
            if (!view || view.guardCannon || !view.rightHand || !view.muzzle) return false;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PulseCannonSource);
            var finish = AssetDatabase.LoadAssetAtPath<Material>(PulseCannonFinish);
            if (!source || !finish) return false;

            var hand = view.rightHand;
            var middle = view.animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var index = view.animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var little = view.animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            Vector3 finger = middle ? middle.position - hand.position : -hand.right;
            Vector3 dorsal = index && little ? Vector3.Cross(finger, index.position - little.position) : view.transform.forward;
            if (Vector3.Dot(dorsal, view.transform.forward) < 0) dorsal = -dorsal;
            if (dorsal.sqrMagnitude < .000001f) dorsal = view.transform.forward;
            var mount = new GameObject("Guard / forearm pulse cannon").transform;
            mount.SetParent(hand, false);
            mount.rotation = Quaternion.LookRotation(finger, dorsal);
            mount.position = hand.position + mount.rotation * new Vector3(.012f,.067f,-.035f);

            var gun = (GameObject)PrefabUtility.InstantiatePrefab(source, mount);
            PrefabUtility.UnpackPrefabInstance(gun, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            gun.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            gun.transform.localScale = Vector3.one * .47f;
            var muzzle = gun.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "Barrel_C_Muzzle");
            if (!muzzle) throw new InvalidOperationException("The guard weapon is missing its barrel marker.");
            var animator = gun.GetComponent<Animator>();
            if (animator) UnityEngine.Object.DestroyImmediate(animator);

            const string materialPath = Output + "/Adam/Materials/GuardPulseCannon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(materialPath));
                material = new Material(finish) { name = "Doppelganger / guard pulse cannon" };
                material.SetTexture("_EmissionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(PulseCannonEmission));
                material.SetColor("_EmissionColor",new Color(.30f,.75f,1.25f));
                material.EnableKeyword("_EMISSION");
                AssetDatabase.CreateAsset(material,materialPath);
            }
            foreach (var renderer in gun.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            view.muzzle.SetParent(muzzle,false);
            view.muzzle.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            view.muzzle.name = "Pulse cannon / muzzle";
            view.guardCannon = mount;
            return true;
        }

        static AnimationClip MainClip(string path) => AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));

        static AnimationClip LoopCopy(AnimationClip source, string name)
        {
            string folder = Output + "/Animations";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string path = folder + "/" + name + ".anim";
            var destination = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = name;
            copy.legacy = false;
            copy.wrapMode = WrapMode.Loop;
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = settings.loopBlend = true;
            settings.loopBlendOrientation = true;
            // Keep the film clip's translation as extracted root motion and discard it at runtime.
            // Baking the downward/forward trajectory into the bones makes the mesh drift off its capsule.
            settings.loopBlendPositionXZ = settings.loopBlendPositionY = false;
            settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            if (destination)
            {
                EditorUtility.CopySerialized(copy, destination);
                UnityEngine.Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(destination);
            }
            else { destination = copy; AssetDatabase.CreateAsset(destination, path); }
            return destination;
        }

        static void ConfigureRig(RobotPresentation view, Avatar avatar, AnimationClip idle, AnimationClip walk, AnimationClip sourceWalk)
        {
            view.animator.avatar = avatar;
            view.animator.enabled = true;
            view.animator.runtimeAnimatorController = null;
            view.animator.applyRootMotion = false;
            view.animator.fireEvents = false;
            view.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            view.animator.updateMode = AnimatorUpdateMode.Normal;
            view.walkClip = walk;
            view.idleClip = idle;
            // The film walk is authored at about .76 m/s, not the 1.55 m/s generic controller default.
            // Match cadence to the original extracted root displacement, including our body scale.
            Vector3 sourceSpeed = sourceWalk.averageSpeed;
            float worldStrideSpeed = new Vector2(sourceSpeed.x, sourceSpeed.z).magnitude * Mathf.Abs(view.model.lossyScale.y);
            view.walkMetersPerSecond = worldStrideSpeed > .01f ? Mathf.Clamp(worldStrideSpeed, .35f, 2.5f) : .75f;
            // The pack includes a visible rig-control mesh under the feet. Keep its transform
            // for animation bindings, but never draw it as part of the in-game robot.
            foreach (var renderer in view.model.GetComponentsInChildren<Renderer>(true))
                if (renderer.gameObject.name == "RootMotion") renderer.enabled = false;
            foreach (var skin in view.model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.updateWhenOffscreen = true;
        }

        static void WriteReport(string modelPath, string walkPath, string idlePath, Avatar avatar, AnimationClip idle, AnimationClip walk)
        {
            Directory.CreateDirectory("Diagnostics/Characters");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var view = prefab ? prefab.GetComponent<RobotPresentation>() : null;
            string Clip(AnimationClip clip) => clip
                ? clip.name + " | humanoid=" + clip.isHumanMotion + " | length=" + clip.length.ToString("F3")
                    + " | looping=" + clip.isLooping + " | averageSpeed=" + clip.averageSpeed.ToString("F3")
                    + " | curves=" + AnimationUtility.GetCurveBindings(clip).Length
                : "not available";
            File.WriteAllText("Diagnostics/Characters/adam-art-result.txt",
                "Official ADAM robot: " + modelPath + "\nAvatar valid=" + avatar.isValid + ", human=" + avatar.isHuman
                + "\nWalk source: " + walkPath + "\nOriginal walk: " + Clip(MainClip(walkPath)) + "\nWalk: " + Clip(walk)
                + "\nIdle source: " + idlePath + "\nIdle: " + Clip(idle)
                + "\nModel scale: " + (view && view.model ? view.model.lossyScale.ToString("F3") : "none")
                + "\nCalibrated walk meters per second: " + (view ? view.walkMetersPerSecond.ToString("F3") : "none")
                + "\nHidden RootMotion rig-control renderers: " + (view && view.model
                    ? view.model.GetComponentsInChildren<Renderer>(true).Count(renderer => renderer.gameObject.name == "RootMotion" && !renderer.enabled).ToString()
                    : "none")
                + "\nRoot motion: translation extracted and ignored (applyRootMotion=false); displacement supplied by the collision controller."
                + "\nGenerated prefab updated in place; original animation/material assets unchanged."
                + "\nRestricted Asset: personal/non-commercial use only.\n");
        }

        static Bounds BoundsOf(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void CloneMaterials(GameObject model)
        {
            string folder = Output + "/Adam/Materials";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var copies = new Dictionary<Material, Material>();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    var source = slots[i]; if (!source) continue;
                    if (!copies.TryGetValue(source, out var material))
                    {
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                        string filename = Regex.Replace(source.name, @"[^\w.-]", "_") + "_" + guid + ".mat";
                        string path = folder + "/" + filename;
                        material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!material)
                        {
                            material = new Material(source) { name = "Doppelganger / ADAM / " + source.name };
                            // Allow the per-renderer damage flash without altering source package materials.
                            material.EnableKeyword("_EMISSION");
                            AssetDatabase.CreateAsset(material, path);
                        }
                        copies[source] = material;
                    }
                    slots[i] = material;
                }
                renderer.sharedMaterials = slots;
            }
        }
    }
}
