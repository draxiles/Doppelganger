using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZeroSector
{
    public static class BridgeVisualQuality
    {
        public static void UseHighestDetail(LODGroup group)
        {
            var levels=group.GetLODs();
            if(levels.Length==0) return;
            var highest=new HashSet<Renderer>(levels[0].renderers);
            group.enabled=false;
            foreach(var level in levels)
                foreach(var renderer in level.renderers)
                    if(renderer) renderer.enabled=highest.Contains(renderer);
        }

        public static void Apply(Camera camera)
        {
            foreach(var group in Object.FindObjectsByType<LODGroup>()) UseHighestDetail(group);
            if(camera)
            {
                var data=camera.GetUniversalAdditionalCameraData();
                data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality=AntialiasingQuality.High;
                camera.allowDynamicResolution=false;
            }
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.24f,.29f,.36f);
            foreach(var renderer in Object.FindObjectsByType<MeshRenderer>())
                foreach(var material in renderer.sharedMaterials)
                    if(material && material.shader && material.shader.name=="Universal Render Pipeline/Lit")
                    {
                        // The old kit's very fine normal/metal maps shimmer at grazing angles.
                        material.DisableKeyword("_NORMALMAP");
                        material.SetFloat("_BumpScale",0);
                        material.SetFloat("_Smoothness",.18f);
                        material.SetFloat("_Metallic",.12f);
                        material.DisableKeyword("_METALLICSPECGLOSSMAP");
                    }
        }
    }
}
