using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZeroSector.Editor
{
    public static class SignalPresentationAuthoring
    {
        public static void Prepare()
        {
            const string art="Assets/Resources/Doppelganger/Interface/SignalKeyArt.png";
            if(AssetImporter.GetAtPath(art) is TextureImporter importer && (importer.mipmapEnabled || importer.maxTextureSize!=2048))
            {
                importer.mipmapEnabled=false;
                importer.maxTextureSize=2048;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            SetCampaignTextureBudgets();

            const string campaign="Assets/DoppelgangerGenerated/Campaign";
            for(int chapter=0;chapter<3;chapter++)
            {
                var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(campaign+"/EpisodeAtmosphere"+chapter+".asset");
                if(!profile) continue;
                if(profile.TryGet<ColorAdjustments>(out var color))
                {
                    color.postExposure.Override(chapter==0 ? .25f : chapter==1 ? .32f : .38f);
                    color.contrast.Override(chapter==0 ? 12 : chapter==1 ? 9 : 13);
                    color.saturation.Override(chapter==1 ? -7 : -10);
                    EditorUtility.SetDirty(color);
                }
                if(profile.TryGet<Bloom>(out var bloom))
                {
                    bloom.intensity.Override(chapter==2 ? .30f : .22f);
                    bloom.threshold.Override(1.24f);
                    bloom.scatter.Override(.61f);
                    EditorUtility.SetDirty(bloom);
                }
                if(profile.TryGet<Vignette>(out var vignette))
                {
                    vignette.intensity.Override(chapter==0 ? .16f : .12f);
                    vignette.smoothness.Override(.58f);
                    EditorUtility.SetDirty(vignette);
                }
            }

            foreach(string folder in new[]{campaign+"/Materials","Assets/DoppelgangerGenerated/Exterior/Materials","Assets/DoppelgangerGenerated/OperationSignal/Materials"})
            {
                if(!AssetDatabase.IsValidFolder(folder)) continue;
                foreach(string id in AssetDatabase.FindAssets("t:Material",new[]{folder}))
                {
                    var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(id));
                    if(!material || !material.name.StartsWith("base color_") || !material.HasProperty("_Smoothness")) continue;
                    // This palette's source micro-normal and gloss produced visible sparkling on the generators.
                    material.SetFloat("_Smoothness",.34f);
                    if(material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap",null);
                    if(material.HasProperty("_MetallicGlossMap")) material.SetTexture("_MetallicGlossMap",null);
                    material.DisableKeyword("_NORMALMAP");
                    material.DisableKeyword("_METALLICSPECGLOSSMAP");
                    EditorUtility.SetDirty(material);
                }
            }
            AssetDatabase.SaveAssets();
        }

        static void SetCampaignTextureBudgets()
        {
            const string adam="Assets/UnityTechnologies/Adam Character Pack/Adam/Textures/Adam_";
            foreach(string part in new[]{"Arms","Head","Legs","Torso"})
            {
                SetStandaloneTexture(adam+part+"_n.tif",TextureImporterFormat.BC5);
                SetStandaloneTexture(adam+part+"_sg.tif",TextureImporterFormat.BC7);
            }

            const string kit="Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Textures/HD_";
            foreach(string group in new[]{"Floor_01","Wall_01","Wall_02","Wall_03","Stuff_01","Stuff_02","Stuff_03"})
                SetStandaloneTexture(kit+group+"_Dif_Style_02_Norm.png",TextureImporterFormat.BC5);
        }

        static void SetStandaloneTexture(string path,TextureImporterFormat format)
        {
            if(!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            var platform=importer.GetPlatformTextureSettings("Standalone");
            if(platform.overridden && platform.format==format && platform.maxTextureSize==2048) return;
            platform.overridden=true;
            platform.format=format;
            platform.maxTextureSize=2048;
            platform.compressionQuality=(int)TextureCompressionQuality.Best;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
        }
    }
}
