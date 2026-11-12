using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZeroSector.Campaign
{
    public sealed class CampaignBulletMarks : MonoBehaviour
    {
        public const string MaterialPath = "Doppelganger/BulletMark";
        const int Capacity = 96;
        const float ProjectionDepth = .32f;
        const int PatternSize = 96;

        sealed class Mark
        {
            public Transform Volume;
            public Collider Surface;
            public Vector3 LocalPoint;
            public Quaternion LocalRotation;
        }

        readonly Mark[] marks = new Mark[Capacity];
        Material material;
        Texture2D pattern;
        Mesh volumeMesh;
        int nextMark, variation;

        public void Initialize(Camera eyes)
        {
            var template = Resources.Load<Material>(MaterialPath);
            if (!template || !template.shader || !template.shader.isSupported)
                return;

            eyes.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            pattern = CreatePattern();
            volumeMesh = CreateVolume();
            material = new Material(template);
            material.SetTexture("_BaseMap", pattern);
        }

        public void Place(RaycastHit hit, float diameter)
        {
            if (!material || !hit.collider || hit.collider is CharacterController ||
                hit.collider.GetComponentInParent<CampaignEnemy>())
                return;

            var mark = marks[nextMark] ??= CreateMark();
            nextMark = (nextMark + 1) % Capacity;
            mark.Surface = hit.collider;
            Transform surface = mark.Surface.transform;
            mark.LocalPoint = surface.InverseTransformPoint(hit.point);
            float roll = (++variation * 137.508f) % 360;
            mark.LocalRotation = Quaternion.Inverse(surface.rotation) *
                (Quaternion.LookRotation(hit.normal) * Quaternion.AngleAxis(roll, Vector3.forward));
            mark.Volume.localScale = new Vector3(diameter, diameter, ProjectionDepth);
            UpdatePose(mark);
            mark.Volume.gameObject.SetActive(true);
        }

        Mark CreateMark()
        {
            var volume = new GameObject("Bullet mark", typeof(MeshFilter), typeof(MeshRenderer));
            volume.transform.SetParent(transform, false);
            volume.GetComponent<MeshFilter>().sharedMesh = volumeMesh;
            var renderer = volume.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return new Mark { Volume = volume.transform };
        }

        void LateUpdate()
        {
            foreach (var mark in marks)
            {
                if (mark == null || !mark.Volume.gameObject.activeSelf)
                    continue;
                if (!mark.Surface || !mark.Surface.enabled || !mark.Surface.gameObject.activeInHierarchy)
                    mark.Volume.gameObject.SetActive(false);
                else
                    UpdatePose(mark);
            }
        }

        static void UpdatePose(Mark mark)
        {
            Transform surface = mark.Surface.transform;
            mark.Volume.SetPositionAndRotation(surface.TransformPoint(mark.LocalPoint),
                surface.rotation * mark.LocalRotation);
        }

        static Mesh CreateVolume()
        {
            return new Mesh
            {
                name = "Bullet mark projection volume",
                vertices = new Vector3[]
                {
                    new(-.5f,-.5f,-.5f), new(.5f,-.5f,-.5f), new(.5f,.5f,-.5f), new(-.5f,.5f,-.5f),
                    new(-.5f,-.5f,.5f), new(.5f,-.5f,.5f), new(.5f,.5f,.5f), new(-.5f,.5f,.5f)
                },
                triangles = new[] { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,4,7, 0,7,3, 1,2,6, 1,6,5, 0,1,5, 0,5,4, 3,7,6, 3,6,2 }
            };
        }

        static Texture2D CreatePattern()
        {
            var texture = new Texture2D(PatternSize, PatternSize, TextureFormat.RGBA32, true)
                { name = "Pitted metal and scorch", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[PatternSize * PatternSize];
            for (int y = 0; y < PatternSize; y++)
            for (int x = 0; x < PatternSize; x++)
            {
                float u = (x + .5f) / PatternSize * 2 - 1;
                float v = (y + .5f) / PatternSize * 2 - 1;
                float angle = Mathf.Atan2(v, u);
                float grain = Mathf.PerlinNoise(x * .23f + 2.3f, y * .23f + 1.7f);
                float radius = Mathf.Sqrt(u * u + v * v) *
                    (1 + .11f * Mathf.Sin(angle * 9 + .6f) + .07f * Mathf.Cos(angle * 17));
                float crater = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.19f, .28f, radius));
                float rim = Mathf.Max(0, 1 - Mathf.Abs(radius - .32f) / .085f);
                float soot = Mathf.Pow(Mathf.Clamp01(1 - radius / .86f), 1.55f) * (.65f + .7f * grain);
                float speck = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, .85f, grain)) *
                    Mathf.Clamp01(1 - radius / .9f) * .27f;
                Color pit = new(.033f, .028f, .025f, 1);
                Color metal = new(.52f, .46f, .39f, 1);
                float chip = rim * (.17f + .65f * Mathf.Clamp01(.48f + .36f * (u + v) + .5f * grain));
                Color pixel = Color.Lerp(pit, metal, chip * (1 - crater));
                pixel.a = Mathf.Clamp01(Mathf.Max(crater * .98f, soot + rim * .51f + speck));
                pixels[y * PatternSize + x] = pixel;
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        void OnDestroy()
        {
            if (material) Destroy(material);
            if (pattern) Destroy(pattern);
            if (volumeMesh) Destroy(volumeMesh);
        }
    }
}
