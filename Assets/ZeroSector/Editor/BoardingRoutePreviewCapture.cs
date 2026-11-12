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
using Object = UnityEngine.Object;

namespace ZeroSector.Editor
{
    // Render the shipped geometry and pistol in Edit Mode without initializing player inputs or profiles.
    public static class BoardingRoutePreviewCapture
    {
        const string Output = "Diagnostics/CampaignPreviews/Boarding";
        const int Width = 1600, Height = 900;

        public static void Run()
        {
            int result = 0;
            try { CaptureExisting(); }
            catch (Exception error) { Debug.LogException(error); result = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(result);
        }

        public static void CaptureExisting()
        {
            Directory.CreateDirectory(Output);
            var setup = EditorSceneManager.GetSceneManagerSetup().Where(scene => !string.IsNullOrEmpty(scene.path)).ToArray();
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            bool opened = false;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Preview needs Edit Mode and a graphics device (use -force-d3d11, without -nographics).");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before capturing previews.");
                ShaderUtil.allowAsyncCompilation = false;
                EditorSceneManager.OpenScene(CampaignGame.ScenePaths[1], OpenSceneMode.Single);
                opened = true;
                var game = Object.FindAnyObjectByType<CampaignGame>();
                if (!game || !game.eyes || !game.presentation) throw new InvalidDataException("Engineering scene has no campaign camera.");
                var route = BoardingRoute.Create(game.transform);
                if (!route) throw new InvalidDataException("Prepare the boarding route prefab before capture.");
                var camera = game.eyes;
                // Batch preview reloads can invalidate URP's volume stack; gameplay camera settings stay in the scene asset.
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                var view = game.presentation;
                if (!view.weaponPivot) throw new InvalidDataException("The campaign camera has no weapon mount.");
                view.weaponPivot.gameObject.SetActive(false);
                Shoot(camera, route.transform, "01-transfer-lock", new Vector3(-.84f, 1.73f, -16.8f), new Vector3(.88f, 1.82f, -8.4f));
                Shoot(camera, route.transform, "02-cargo-hold", new Vector3(-.88f, 1.95f, -5.12f), new Vector3(.25f, 2.50f, 7.4f));
                view.weaponPivot.gameObject.SetActive(true);
                view.InstallCombatSidearm();
                int pistol = view.weapons.FindIndex(slot => slot.id == FirstPersonPresentation.ServicePistolId);
                if (pistol < 0) throw new InvalidDataException("The P-7 combat viewmodel is missing.");
                view.Equip(pistol);
                Shoot(camera, route.transform, "03-cargo-pistol", new Vector3(1.95f, 1.73f, -.98f), new Vector3(2.2f, 1.86f, 10.1f));
                File.WriteAllText(Output + "/preview-check.txt", "RESULT: PASSED\nTransfer lock, cargo hold and P-7 rendered with the campaign camera.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(Output + "/preview-check.txt", "RESULT: FAILED\n" + error + "\n");
                throw;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                if (opened)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }
        }

        static void Shoot(Camera camera, Transform route, string file, Vector3 eye, Vector3 look)
        {
            camera.transform.SetPositionAndRotation(route.TransformPoint(eye), route.rotation * Quaternion.LookRotation(look - eye));
            var previous = RenderTexture.active;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                if (!target.Create()) throw new InvalidOperationException("Could not allocate capture buffer.");
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                frame.Apply(false);
                byte[] bytes = frame.EncodeToPNG();
                if (bytes.Length < 24000) throw new InvalidDataException("Rendered frame is blank: " + file);
                File.WriteAllBytes(Output + "/boarding-" + file + ".png", bytes);
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(frame);
            }
        }
    }
}
