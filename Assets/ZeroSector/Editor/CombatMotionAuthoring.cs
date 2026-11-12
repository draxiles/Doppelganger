using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroSector.Editor
{
    /// <summary>Imports selected full-body clips from the official Unity FPS Sample.</summary>
    public static class CombatMotionAuthoring
    {
        const string Staging="ThirdPartyStaging/CombatMotion";
        const string Import="Assets/ThirdParty/UnityFpsSample/Animation/Characters/Terraformer/Clips";
        const string Output="Assets/Resources/Doppelganger/CombatMotion";

        static readonly (string source,string name,bool loop)[] Clips={
            ("Stand--Idle","Idle",true),
            ("Locomotion--Run_N","Run",true),
            ("Locomotion--Run_E","StrafeRight",true),
            ("Locomotion--Run_W","StrafeLeft",true),
            ("Locomotion--Run_S","Backpedal",true),
            ("Locomotion--Sprint_N","Sprint",true),
            ("Stand--ShootPose","Aim",true),
            ("Stand--Shoot","Fire",false)
        };

        public static void Prepare()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Combat animation authoring requires Edit mode.");
            Directory.CreateDirectory(Import);
            Directory.CreateDirectory(Output);
            foreach(var clip in Clips)
            {
                string file=clip.source+".anim.fbx";
                CopyMissing(file);
                CopyMissing(file+".meta");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var clip in Clips)
            {
                string path=Import+"/"+clip.source+".anim.fbx";
                var original=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(item=>item.isHumanMotion && !item.name.StartsWith("__preview",StringComparison.Ordinal));
                if(!original || original.length<=.015f)
                    throw new InvalidOperationException("Missing full-body Humanoid animation: "+path+". Import the Terraformer third-person avatar first.");
                SaveClip(original,clip.name,clip.loop);
            }
            AssetDatabase.SaveAssets();
            AdamRobotAuthoring.UpgradeGuardPresentation();
            Debug.Log("Combat motion: authored eight Humanoid FPS Sample animations for the guards and Doppelganger.");
        }

        static void CopyMissing(string file)
        {
            string destination=Import+"/"+file;
            if(!File.Exists(destination)) File.Copy(Staging+"/"+file,destination);
        }

        static void SaveClip(AnimationClip original,string name,bool loop)
        {
            string path=Output+"/"+name+".anim";
            var copy=UnityEngine.Object.Instantiate(original);
            copy.name=name;
            copy.legacy=false;
            copy.wrapMode=loop ? WrapMode.Loop : WrapMode.ClampForever;
            AnimationUtility.SetAnimationEvents(copy,Array.Empty<AnimationEvent>());
            var settings=AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime=loop;
            // World displacement belongs to the collision controller; use the source's
            // in-place settings for all locomotion and upper-body curves.
            AnimationUtility.SetAnimationClipSettings(copy,settings);
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(!existing) AssetDatabase.CreateAsset(copy,path);
            else
            {
                EditorUtility.CopySerialized(copy,existing);
                UnityEngine.Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(existing);
            }
        }
    }
}
