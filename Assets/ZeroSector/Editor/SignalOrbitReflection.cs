using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSector.Editor
{
    internal static class SignalOrbitReflection
    {
        const string Path="Assets/DoppelgangerGenerated/Prologue/Signal orbital radiance.asset";
        public static readonly Vector3 SunDirection=new Vector3(.39f,.79f,.48f).normalized;

        public static void Install(Transform orbit)
        {
            var node=new GameObject("Orbital reflections / sun and Earth",typeof(ReflectionProbe));
            node.transform.SetParent(orbit,false);
            var probe=node.GetComponent<ReflectionProbe>();
            probe.mode=ReflectionProbeMode.Custom;
            probe.customBakedTexture=Radiance();
            probe.size=new Vector3(172,78,178);
            probe.blendDistance=10;
            probe.intensity=.95f;
            probe.importance=10;
            probe.boxProjection=false;
        }

        static Cubemap Radiance()
        {
            var existing=AssetDatabase.LoadAssetAtPath<Cubemap>(Path);
            const int size=64;
            var cube=new Cubemap(size,TextureFormat.RGBAHalf,true)
                {name="Signal orbital radiance",filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};
            var earthDirection=new Vector3(-.23f,-.88f,.40f).normalized;
            for(int mip=0;mip<cube.mipmapCount;mip++)
            {
                int width=Mathf.Max(1,size>>mip);
                float roughness=mip/(float)(cube.mipmapCount-1);
                for(int face=0;face<6;face++)
                {
                    var pixels=new Color[width*width];
                    for(int y=0;y<width;y++) for(int x=0;x<width;x++)
                    {
                        var ray=Direction(face,(x+.5f)*2/width-1,(y+.5f)*2/width-1);
                        float key=Mathf.Max(0,Vector3.Dot(ray,SunDirection));
                        float sky=Mathf.Max(0,Vector3.Dot(ray,earthDirection));
                        float limb=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.23f,.75f,sky))*.31f;
                        float sunlight=Mathf.Pow(key,Mathf.Lerp(160,3.8f,roughness))*Mathf.Lerp(5.6f,.48f,roughness);
                        float veil=Mathf.Pow(key,Mathf.Lerp(19,2.3f,roughness))*.11f;
                        Color value=new Color(.010f,.015f,.024f)+new Color(.33f,.62f,1f)*limb+
                            new Color(1,.79f,.54f)*(sunlight+veil);
                        value.a=1;
                        pixels[y*width+x]=value;
                    }
                    cube.SetPixels(pixels,(CubemapFace)face,mip);
                }
            }
            cube.Apply(false,false);
            if(existing)
            {
                EditorUtility.CopySerialized(cube,existing);
                Object.DestroyImmediate(cube);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(cube,Path);
            return cube;
        }

        static Vector3 Direction(int face,float u,float v) => (face switch
        {
            0 => new Vector3(1,v,-u), 1 => new Vector3(-1,v,u),
            2 => new Vector3(u,1,-v), 3 => new Vector3(u,-1,v),
            4 => new Vector3(u,v,1), _ => new Vector3(-u,v,-1)
        }).normalized;
    }
}
