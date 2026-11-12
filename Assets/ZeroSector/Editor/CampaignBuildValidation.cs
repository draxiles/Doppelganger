using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZeroSector.Editor
{
    // URP 17.6 constructs every referenced feature, including disabled ones. Its SSAO
    // resources can be stripped when no SSAO is active, so retaining a disabled SSAO
    // reference causes Create() to dereference missing resources in the player.
    public sealed class CampaignBuildValidation : IPreprocessBuildWithReport
    {
        const string SsaoType = "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion";
        public int callbackOrder => int.MinValue + 100;

        public void OnPreprocessBuild(BuildReport report)
        {
            ValidatePulseTrail();
            ValidateAdam();
            PrepareRenderers();
        }

        static void ValidateAdam()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AdamRobotAuthoring.PrefabPath);
            var view=prefab ? prefab.GetComponent<ZeroSector.Campaign.RobotPresentation>() : null;
            if(!view || !view.animator || !view.animator.avatar || !view.animator.avatar.isValid ||
                !view.animator.avatar.isHuman || !view.idleClip || !view.walkClip ||
                !view.idleClip.isHumanMotion || !view.walkClip.isHumanMotion)
                throw new BuildFailedException("ADAM needs its real humanoid avatar and separate idle/walk clips. Run ADAM authoring before building.");
        }

        static void ValidatePulseTrail()
        {
            const string materialPath = "Assets/ZeroSector/Resources/Doppelganger/PulseTrail.mat";
            const string shaderPath = "Assets/ZeroSector/Shaders/PulseTrail.shader";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (!material || !shader || material.shader != shader || shader.name != "Doppelganger/Pulse Trail")
                throw new BuildFailedException("Pulse trail player dependency is missing or mismatched: " + materialPath);
            if (Resources.Load<Material>("Doppelganger/PulseTrail") != material)
                throw new BuildFailedException("Pulse trail Resources path is missing or ambiguous.");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    throw new BuildFailedException("Pulse trail shader compilation failed: " + message.message);
        }

        public static void PrepareRenderers()
        {
            var pipelines = new HashSet<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset global)
                pipelines.Add(global);
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset quality)
                    pipelines.Add(quality);

            var renderers = new HashSet<ScriptableRendererData>();
            foreach (var pipeline in pipelines)
                foreach (var renderer in pipeline.rendererDataList)
                    if (renderer) renderers.Add(renderer);

            foreach (var renderer in renderers)
            {
                var serialized = new SerializedObject(renderer);
                var features = serialized.FindProperty("m_RendererFeatures");
                var map = serialized.FindProperty("m_RendererFeatureMap");
                if (features == null || map == null)
                    throw new BuildFailedException("Cannot validate URP renderer dependencies: " + renderer.name);
                int removed = 0;
                for (int i = features.arraySize - 1; i >= 0; i--)
                {
                    var feature = features.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererFeature;
                    if (!feature || feature.isActive || feature.GetType().FullName != SsaoType) continue;
                    // Clear the reference before removing the slot; the feature subasset is
                    // retained for reversible editing, but is no longer constructed at runtime.
                    features.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    features.DeleteArrayElementAtIndex(i);
                    removed++;
                }
                if (removed == 0) continue;
                map.arraySize = features.arraySize;
                for (int i = 0; i < features.arraySize; i++)
                {
                    var feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
                    long localId = 0;
                    if (feature) AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out localId);
                    map.GetArrayElementAtIndex(i).longValue = localId;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                renderer.SetDirty();
                EditorUtility.SetDirty(renderer);
                AssetDatabase.SaveAssetIfDirty(renderer);
                Debug.Log($"Player dependency repair: removed {removed} disabled SSAO reference(s) from {renderer.name}.");
            }
        }
    }
}
