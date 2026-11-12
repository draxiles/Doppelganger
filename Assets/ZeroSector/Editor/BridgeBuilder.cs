using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZeroSector.Editor
{
    public static class BridgeBuilder
    {
        const string Pack = "Assets/Sci-Fi Styled Modular Pack/";
        const string Output = "Assets/DoppelgangerGenerated";
        static readonly Dictionary<Material, Material> materials = new();
        static Transform architecture, furniture;
        static Material cyan, amber;

        public static void FixQuality()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Doppelganger", "Сначала остановите Play Mode.", "OK");
                return;
            }
            foreach (var path in new[] {"Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset"})
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (!pipeline) continue;
                pipeline.renderScale = 1;
                var settings = new SerializedObject(pipeline);
                var fade = settings.FindProperty("m_EnableLODCrossFade");
                if (fade != null) fade.boolValue = false;
                settings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline);
            }
            // Establish a clean baseline without the template's low-sample SSAO pass.
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if (rendererData)
            {
                foreach (var feature in rendererData.rendererFeatures)
                    if (feature && feature.GetType().Name == "ScreenSpaceAmbientOcclusion")
                    {
                        feature.SetActive(false);
                        EditorUtility.SetDirty(feature);
                    }
                rendererData.SetDirty();
                EditorUtility.SetDirty(rendererData);
            }
            BridgeVisualQuality.Apply(Camera.main);
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
                foreach (var material in renderer.sharedMaterials)
                    if (material && AssetDatabase.GetAssetPath(material).StartsWith(Output+"/")) EditorUtility.SetDirty(material);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("Doppelganger: native-scale render, SMAA, stable LODs and matte materials applied. Save the scene and press Play.");
        }

        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Doppelganger", "Сначала остановите Play Mode.", "OK");
                return;
            }
            if (!AssetDatabase.IsValidFolder(Pack.TrimEnd('/')))
                throw new InvalidOperationException("Sci-Fi Styled Modular Pack not found in Assets.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Output + "/Materials");
            Directory.CreateDirectory(Output + "/Audio");
            AssetDatabase.Refresh();
            materials.Clear();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            architecture = new GameObject("01 • Hull and observation deck").transform;
            furniture = new GameObject("02 • Command stations").transform;
            cyan = Plain("Navigation light", new Color(.08f,.65f,.85f), 3);
            amber = Plain("Emergency light", new Color(1,.16f,.025f), 3);

            // All visible room surfaces and furniture use the imported mesh kit.
            for (int x = -6; x <= 6; x += 4)
                for (int z = -8; z <= 8; z += 4)
                {
                    Fit("Floors/floor_1", new Vector3(x,-.15f,z), new Vector3(4,.3f,4), 0, architecture);
                    Fit("Floors/floor_2_blank", new Vector3(x,4.65f,z), new Vector3(4,.2f,4), 180, architecture, true);
                }
            Collision("Deck", new Vector3(0,-.2f,0), new Vector3(16,.4f,20));
            for (int z = -8; z <= 8; z += 4)
            {
                Fit("Walls/decorative_wall_1", new Vector3(-8,2.25f,z), new Vector3(.35f,4.5f,4), 90, architecture);
                Fit("Walls/decorative_wall_1", new Vector3(8,2.25f,z), new Vector3(.35f,4.5f,4), -90, architecture);
            }
            Collision("Port hull", new Vector3(-8.15f,2,0), new Vector3(.3f,5,20));
            Collision("Starboard hull", new Vector3(8.15f,2,0), new Vector3(.3f,5,20));
            Collision("Ceiling", new Vector3(0,4.8f,0), new Vector3(16,.3f,20));
            foreach (int x in new[] {-6,-2,2,6})
            {
                Fit("Windows/window_big", new Vector3(x,2.25f,10), new Vector3(4,4.5f,.45f), 0, architecture);
                if (Mathf.Abs(x) > 2)
                    Fit("Walls/decorative_wall_4_computer", new Vector3(x,2.25f,-10), new Vector3(4,4.5f,.35f), 180, architecture);
            }
            // Rear doorway is 4m wide; flank panels fill the remainder of the rear wall.
            foreach (float x in new[] {-3f,3f})
                Fit("Walls/decorative_wall_3", new Vector3(x,2.25f,-10), new Vector3(2,4.5f,.35f), 180, architecture);
            Collision("Observation glass", new Vector3(0,2.25f,10.2f), new Vector3(16,4.5f,.15f));
            Collision("Rear left", new Vector3(-5,2.25f,-10), new Vector3(6,4.5f,.2f));
            Collision("Rear right", new Vector3(5,2.25f,-10), new Vector3(6,4.5f,.2f));

            for (int z = -12; z >= -24; z -= 4)
            {
                Fit("Floors/floor_1", new Vector3(0,-.15f,z), new Vector3(4,.3f,4), 0, architecture);
                Fit("Floors/floor_2_blank", new Vector3(0,4.65f,z), new Vector3(4,.2f,4), 180, architecture, true);
                Fit("Walls/decorative_wall_6", new Vector3(-2,2.25f,z), new Vector3(.3f,4.5f,4), 90, architecture);
                Fit("Walls/decorative_wall_6", new Vector3(2,2.25f,z), new Vector3(.3f,4.5f,4), -90, architecture);
                Fit("Lights/light_celing_1", new Vector3(0,4.3f,z), new Vector3(1.3f,.2f,.8f), 0, furniture);
                Lamp("Corridor guide", new Vector3(0,3.5f,z), new Color(.1f,.6f,.8f), 2, 6);
            }
            Collision("Corridor deck", new Vector3(0,-.2f,-18), new Vector3(4,.4f,16));
            Collision("Corridor left", new Vector3(-2.1f,2,-18), new Vector3(.2f,5,16));
            Collision("Corridor right", new Vector3(2.1f,2,-18), new Vector3(.2f,5,16));
            Fit("Walls/decorative_wall_4_computer", new Vector3(0,2.25f,-26), new Vector3(4,4.5f,.35f), 180, architecture);
            Collision("Corridor end", new Vector3(0,2,-26), new Vector3(4,5,.2f));
            var door = Fit("Doors/door_1", new Vector3(0,2.2f,-10), new Vector3(3.9f,4.4f,.35f), 0, architecture);
            var doorCollider = door.AddComponent<BoxCollider>();
            doorCollider.size = LocalWorldSize(door.transform, new Vector3(3.9f,4.4f,.35f));

            // Leave a central walking lane between the forward navigation consoles.
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -3.7f : 3.7f;
                Furnish("Decorative elements/console", new Vector3(x,0,6.3f), 2.7f, 0);
                Furnish("Decorative elements/pilot_seat", new Vector3(x,0,4.3f), 1.6f, 0);
                Furnish("Decorative elements/computer_station", new Vector3(i == 0 ? -6.5f : 6.5f,0,-3), 2.4f, i == 0 ? 90 : -90);
            }
            var terminal = Furnish("Decorative elements/console", new Vector3(0,0,1.5f), 2.5f, 0);
            terminal.name = "COMMAND TERMINAL • E to restore power";
            Furnish("Decorative elements/pilot_seat", new Vector3(0,0,-.6f), 1.7f, 0);
            Furnish("Machines/shield_generator", new Vector3(-6,0,-7.8f), 2, 0);
            Furnish("Machines/Battery_medium", new Vector3(6.3f,0,-8), 1.4f, 0);
            Furnish("Machines/storage_container_small", new Vector3(1.1f,0,-23.5f), 1, 10);

            var emergencyLights = new List<Light>();
            for (int z = -7; z <= 7; z += 7)
            {
                foreach (int side in new[] {-1,1})
                {
                    Fit("Lights/light_wall_1", new Vector3(side*7.7f,2.7f,z), new Vector3(.25f,.55f,1.3f), side*90, furniture);
                    emergencyLights.Add(Lamp("Emergency / bridge", new Vector3(side*6.1f,3,z), new Color(1,.21f,.07f), 1.8f, 9));
                    Strip(new Vector3(side*7.4f,.1f,z), new Vector3(.045f,.035f,5.7f), cyan);
                }
            }
            Lamp("Forward instrument glow", new Vector3(0,3,7), new Color(.15f,.55f,1), 2.8f, 12);
            Lamp("Console glow", new Vector3(0,2.7f,1), new Color(.1f,.75f,1), 1.6f, 6);
            var sun = new GameObject("Earthlight", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(.48f,.67f,1);
            sun.intensity = .65f;
            sun.transform.rotation = Quaternion.Euler(35,-35,0);
            sun.shadows = LightShadows.Soft;

            var skyShader = Shader.Find("Doppelganger/DeepSpace");
            RenderSettings.skybox = SaveMaterial(new Material(skyShader), "Deep Space");
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.09f,.13f,.2f);
            RenderSettings.fog = false;
            var earth = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            earth.name = "Earth / orbital sunrise";
            earth.transform.position = new Vector3(-70,-110,290);
            earth.transform.localScale = Vector3.one*300;
            earth.GetComponent<Renderer>().sharedMaterial = SaveMaterial(new Material(Shader.Find("Doppelganger/OrbitalPlanet")), "Earth");
            UnityEngine.Object.DestroyImmediate(earth.GetComponent<Collider>());

            var player = new GameObject("Commander • First Person", typeof(CharacterController));
            player.transform.position = new Vector3(0,.1f,-5);
            var cc = player.GetComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = .28f;
            cc.center = new Vector3(0,.9f,0);
            cc.stepOffset = .3f;
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.localPosition = new Vector3(0,1.65f,0);
            var camera = cameraObject.GetComponent<Camera>();
            camera.nearClipPlane = .06f;
            camera.farClipPlane = 1500;
            camera.fieldOfView = 72;
            camera.allowHDR = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var episode = player.AddComponent<BridgeEpisode>();
            episode.eyes = camera;
            episode.terminal = terminal.transform;
            episode.airlock = door.transform;
            episode.bridgeLights = emergencyLights.ToArray();
            episode.ambience = CreateAmbience();

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(.35f);
            bloom.threshold.Override(1.1f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.23f);
            var oldProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Output+"/BridgeAtmosphere.asset");
            if (oldProfile) AssetDatabase.DeleteAsset(Output+"/BridgeAtmosphere.asset");
            AssetDatabase.CreateAsset(profile, Output+"/BridgeAtmosphere.asset");
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            var volume = new GameObject("03 • Atmosphere", typeof(Volume)).GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            Physics.SyncTransforms();
            // Prevent a broken layout from being silently presented as ready.
            if (!Physics.Raycast(player.transform.position+Vector3.up, Vector3.down, out _, 2))
                throw new InvalidOperationException("Bridge validation failed: no floor beneath spawn.");
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, Output+"/DoppelgangerBridge.unity");
            Selection.activeGameObject = player;
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0,1.6f,0), Quaternion.Euler(15,155,0), 18);
            Debug.Log("Doppelganger: bridge built and floor validated. Open Game and press Play. WASD / Mouse / E.");
        }

        static Vector3 LocalWorldSize(Transform t, Vector3 size) => new(size.x/t.lossyScale.x,size.y/t.lossyScale.y,size.z/t.lossyScale.z);

        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("No mesh renderers: "+go.name);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) if(r.enabled) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        static GameObject Fit(string relative, Vector3 position, Vector3 size, float yaw, Transform parent, bool flip = false)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/"+relative+".prefab");
            if (!prefab) throw new InvalidOperationException("Missing prefab: "+relative);
            var wrapper = new GameObject(Path.GetFileName(relative));
            wrapper.transform.SetParent(parent, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.transform.SetParent(wrapper.transform, false);
            model.transform.rotation = Quaternion.Euler(flip ? 180 : 0, yaw, 0);
            foreach (var c in model.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (var lod in model.GetComponentsInChildren<LODGroup>()) BridgeVisualQuality.UseHighestDetail(lod);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var shared = renderer.sharedMaterials;
                for (int i=0;i<shared.Length;i++) if (shared[i]) shared[i] = ConvertMaterial(shared[i]);
                renderer.sharedMaterials = shared;
            }
            Bounds b = BoundsOf(model);
            wrapper.transform.localScale = new Vector3(size.x/Mathf.Max(b.size.x,.001f),size.y/Mathf.Max(b.size.y,.001f),size.z/Mathf.Max(b.size.z,.001f));
            b = BoundsOf(model);
            model.transform.position -= b.center;
            wrapper.transform.position = position;
            return wrapper;
        }

        static GameObject Furnish(string relative, Vector3 floor, float width, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack+"Prefabs/"+relative+".prefab");
            if (!prefab) throw new InvalidOperationException("Missing prefab: "+relative);
            var probe = UnityEngine.Object.Instantiate(prefab);
            probe.transform.rotation = Quaternion.Euler(0,yaw,0);
            var b = BoundsOf(probe);
            float scale = width/Mathf.Max(b.size.x,b.size.z);
            var size = b.size*scale;
            UnityEngine.Object.DestroyImmediate(probe);
            var item = Fit(relative, floor+Vector3.up*size.y*.5f, size, yaw, furniture);
            // Static props get a simple collider to keep the commander out of consoles.
            var box = item.AddComponent<BoxCollider>();
            box.size = LocalWorldSize(item.transform, size);
            box.center = item.transform.InverseTransformPoint(floor+Vector3.up*size.y*.5f);
            return item;
        }

        static void Collision(string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name+" [collision]");
            go.transform.SetParent(architecture);
            go.transform.position=position;
            go.AddComponent<BoxCollider>().size=size;
        }

        static Light Lamp(string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject(name,typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(furniture);
            light.transform.position=position;
            light.type=LightType.Point;
            light.color=color;
            light.intensity=intensity;
            light.range=range;
            light.shadows=LightShadows.None;
            return light;
        }

        static void Strip(Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name="Recessed navigation strip";
            go.transform.SetParent(furniture);
            go.transform.position=pos;
            go.transform.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=mat;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        static Material Plain(string name, Color color, float emission)
        {
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color=color;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor",color*emission);
            return SaveMaterial(mat,name);
        }

        static Material SaveMaterial(Material mat, string name)
        {
            mat.name=name;
            string path=Output+"/Materials/"+name+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(mat,existing);
                UnityEngine.Object.DestroyImmediate(mat);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mat,path);
            return mat;
        }

        static Material ConvertMaterial(Material source)
        {
            if (materials.TryGetValue(source,out var cached)) return cached;
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            string name=source.name.ToLowerInvariant();
            Color color=source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            mat.SetColor("_BaseColor",color);
            foreach (string property in new[] {"_MainTex","_BumpMap","_MetallicGlossMap","_EmissionMap"})
                if (source.HasProperty(property) && source.GetTexture(property))
                {
                    string dest=property=="_MainTex" ? "_BaseMap" : property;
                    mat.SetTexture(dest,source.GetTexture(property));
                    mat.SetTextureScale(dest,source.GetTextureScale(property));
                    mat.SetTextureOffset(dest,source.GetTextureOffset(property));
                }
            if (mat.GetTexture("_BumpMap")) mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_BumpScale",source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1);
            mat.SetFloat("_Smoothness",.38f);
            mat.SetFloat("_Metallic",.5f);
            if (name.Contains("screen") || name.Contains("emision") || name.Contains("emission") || name.Contains("hologram"))
            {
                Color emission=name.Contains("white") ? new Color(.5f,.8f,1) : new Color(.035f,.6f,.9f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor",emission*1.8f);
            }
            if (name.Contains("glass") || name=="window")
            {
                mat.SetColor("_BaseColor",new Color(.1f,.3f,.4f,.08f));
                mat.SetFloat("_Surface",1);
                mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite",0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue=3000;
                mat.SetOverrideTag("RenderType","Transparent");
            }
            string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            mat=SaveMaterial(mat,source.name+"_"+guid.Substring(0,Math.Min(8,guid.Length)));
            materials[source]=mat;
            return mat;
        }

        static AudioClip CreateAmbience()
        {
            string path=Output+"/Audio/ShipAmbience.wav";
            if (!File.Exists(path))
            {
                const int rate=22050, count=rate*8;
                using var writer=new BinaryWriter(File.Create(path));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+count*2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate*2);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count*2);
                var random=new System.Random(18);
                for (int i=0;i<count;i++)
                {
                    double t=i/(double)rate;
                    double hum=Math.Sin(t*Math.PI*2*55)*.22+Math.Sin(t*Math.PI*2*82.5)*.08+(random.NextDouble()-.5)*.025;
                    double fade=Math.Min(1,Math.Min(t,(count-i)/(double)rate)*3);
                    writer.Write((short)(hum*fade*short.MaxValue));
                }
            }
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
