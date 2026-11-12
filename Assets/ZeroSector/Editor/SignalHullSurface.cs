using UnityEditor;
using UnityEngine;

namespace ZeroSector.Editor
{
    // All four channels share a single pattern so the seams, fasteners, reflectance
    // and relief line up on the angled exterior panels.
    internal sealed class SignalHullSurface
    {
        const int Size=512;
        const string Folder="Assets/DoppelgangerGenerated/Prologue/";
        readonly Texture2D color, normal, metal, cavity;

        public SignalHullSurface()
        {
            color=Load("Signal steel color");
            normal=Load("Signal steel normal");
            metal=Load("Signal steel reflectance");
            cavity=Load("Signal steel cavity");
            if(color && normal && metal && cavity) return;

            var height=new float[Size*Size];
            var colors=new Color32[height.Length];
            var normals=new Color32[height.Length];
            var reflectance=new Color32[height.Length];
            var occlusion=new Color32[height.Length];
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
            {
                int index=y*Size+x;
                float v=(y+.5f)/Size*2, row=Mathf.Floor(v);
                float u=(x+.5f)/Size*3+row*.37f, column=Mathf.Floor(u);
                float horizontal=Mathf.Repeat(u,1), vertical=Mathf.Repeat(v,1);
                float edgeX=Mathf.Min(horizontal,1-horizontal);
                float edgeY=Mathf.Min(vertical,1-vertical);
                float seam=Mathf.Min(edgeX,edgeY);
                float plate=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.005f,.025f,seam));
                float rim=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.022f,.053f,seam));
                float grit=Mathf.PerlinNoise(x*.247f+5,y*.289f+13);
                float brushed=Mathf.PerlinNoise(x*.028f+4,y*.63f+12);
                float patina=Mathf.PerlinNoise(x*.038f+50,y*.035f+38);
                float finish=Mathf.PerlinNoise(column*4.8f+16,row*6.3f+3);
                float screw=Vector2.Distance(new Vector2(edgeX,edgeY),new Vector2(.068f,.068f));
                float screwRim=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.011f,.023f,screw));
                float screwHead=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.007f,.012f,screw));
                float scrape=Mathf.Clamp01((brushed-.68f)*3.2f)*Mathf.SmoothStep(0,1,grit)*plate;
                float gasket=1-plate;
                float serviceInset=Mathf.Abs(vertical-.79f)<.012f && horizontal>.18f && horizontal<.77f &&
                    ((int)column+(int)row)%3==1 ? 1 : 0;

                height[index]=plate*.71f+(grit-.5f)*.045f+(brushed-.5f)*.065f
                    -screwRim*.39f+screwHead*.48f-serviceInset*.18f;
                float shade=Mathf.Lerp(.24f,.63f+finish*.18f+patina*.15f+scrape*.15f,plate)
                    +rim*plate*.09f-screwRim*.18f+screwHead*.26f-serviceInset*.17f;
                colors[index]=new Color32(Byte(shade*.96f),Byte(shade),Byte(shade*1.018f),255);
                float metallic=Mathf.Lerp(.16f,.49f+finish*.22f,plate)+scrape*.14f+screwHead*.13f;
                float smoothness=Mathf.Lerp(.23f,.32f+patina*.23f,plate)+scrape*.22f+screwHead*.10f;
                reflectance[index]=new Color32(Byte(metallic),0,0,Byte(smoothness));
                byte ao=Byte(1-gasket*.62f-screwRim*.21f-serviceInset*.28f-rim*.07f);
                occlusion[index]=new Color32(ao,ao,ao,255);
            }
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
            {
                float dx=Height(x-1,y)-Height(x+1,y);
                float dy=Height(x,y-1)-Height(x,y+1);
                Vector3 direction=new Vector3(dx*.85f,dy*.85f,1).normalized;
                normals[y*Size+x]=new Color32(Byte(.5f+.5f*direction.x),Byte(.5f+.5f*direction.y),
                    Byte(.5f+.5f*direction.z),255);
            }

            color=Store("Signal steel color",colors,false,color);
            normal=Store("Signal steel normal",normals,true,normal);
            metal=Store("Signal steel reflectance",reflectance,true,metal);
            cavity=Store("Signal steel cavity",occlusion,true,cavity);

            float Height(int x,int y) => height[(y&(Size-1))*Size+(x&(Size-1))];
        }

        public void Apply(Material material,float relief)
        {
            material.SetTexture("_BaseMap",color);
            material.SetTexture("_BumpMap",normal);
            material.SetTexture("_MetallicGlossMap",metal);
            material.SetTexture("_OcclusionMap",cavity);
            material.SetFloat("_BumpScale",relief);
            material.SetFloat("_OcclusionStrength",.76f);
            material.SetFloat("_SmoothnessTextureChannel",0);
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(material);
        }

        static Texture2D Load(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+name+".asset");

        static Texture2D Store(string name,Color32[] pixels,bool linear,Texture2D existing)
        {
            if(existing) return existing;
            var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,true,linear)
                {name=name,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
            texture.SetPixels32(pixels);
            texture.Apply(true,false);
            AssetDatabase.CreateAsset(texture,Folder+name+".asset");
            return texture;
        }

        static byte Byte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value)*255);
    }
}
