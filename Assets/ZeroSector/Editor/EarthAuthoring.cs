using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSector.Editor
{
    public static class EarthAuthoring
    {
        const string Output="Assets/DoppelgangerGenerated/Earth";
        public static void Prepare()
        {
            Directory.CreateDirectory(Output+"/Textures");
            foreach(string name in new[]{"8k_earth_daymap.jpg","2k_earth_nightmap.jpg","8k_earth_clouds.jpg","LICENSE.txt"})
            {
                string source="ThirdPartyStaging/SolarSystemScope/"+name;
                if(!File.Exists(source)) throw new FileNotFoundException("Earth texture missing: "+source);
                string destination=Output+"/Textures/"+name;
                if(!File.Exists(destination)) File.Copy(source,destination);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string name in new[]{"8k_earth_daymap.jpg","2k_earth_nightmap.jpg","8k_earth_clouds.jpg"})
            {
                var importer=AssetImporter.GetAtPath(Output+"/Textures/"+name) as TextureImporter;
                if(!importer) continue;
                bool color=!name.Contains("clouds");
                if(importer.maxTextureSize==4096 && importer.sRGBTexture==color && importer.filterMode==FilterMode.Trilinear) continue;
                importer.maxTextureSize=4096; importer.sRGBTexture=color; importer.mipmapEnabled=true;
                importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=4;
                importer.wrapModeU=TextureWrapMode.Repeat; importer.wrapModeV=TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            if(!AssetDatabase.LoadAssetAtPath<Mesh>(Output+"/EarthSphere.asset")) AssetDatabase.CreateAsset(Sphere(),Output+"/EarthSphere.asset");
            Material surface=Material("Surface","Doppelganger/Earth Surface");
            surface.SetTexture("_DayTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Output+"/Textures/8k_earth_daymap.jpg"));
            surface.SetTexture("_NightTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Output+"/Textures/2k_earth_nightmap.jpg"));
            Material clouds=Material("Clouds","Doppelganger/Earth Clouds");
            clouds.SetTexture("_CloudTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Output+"/Textures/8k_earth_clouds.jpg"));
            Material("Atmosphere","Doppelganger/Earth Atmosphere");
            Material("BridgeGlass","Doppelganger/Bridge Glass");
            EditorUtility.SetDirty(surface); EditorUtility.SetDirty(clouds); AssetDatabase.SaveAssets();
        }

        static Material Material(string name,string shaderName)
        {
            string path=Output+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader=Shader.Find(shaderName);
            if(!shader) throw new System.InvalidOperationException("Missing shader: "+shaderName);
            if(!material) { material=new Material(shader); material.name=name; AssetDatabase.CreateAsset(material,path); }
            else material.shader=shader;
            return material;
        }

        public static GameObject Create(Vector3 position,float radius)
        {
            var root=new GameObject("Earth / satellite surface, clouds and atmosphere"); root.transform.position=position;
            root.transform.rotation=Quaternion.Euler(8,145,-13);
            Layer(root.transform,"Surface",radius,"Surface");
            Layer(root.transform,"Cloud deck",radius*1.006f,"Clouds");
            Layer(root.transform,"Atmospheric scattering",radius*1.025f,"Atmosphere");
            return root;
        }

        static void Layer(Transform parent,string name,float radius,string material)
        {
            var layer=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            layer.layer=2; layer.transform.SetParent(parent,false); layer.transform.localScale=Vector3.one*radius;
            layer.GetComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Output+"/EarthSphere.asset");
            var renderer=layer.GetComponent<MeshRenderer>(); renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Output+"/"+material+".mat");
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        }

        static Mesh Sphere()
        {
            const int longitude=160,latitude=80;
            var vertices=new Vector3[(longitude+1)*(latitude+1)];
            var uv=new Vector2[vertices.Length]; var triangles=new int[longitude*latitude*6];
            for(int y=0;y<=latitude;y++) for(int x=0;x<=longitude;x++)
            {
                float u=x/(float)longitude,v=y/(float)latitude;
                float phi=v*Mathf.PI,theta=u*Mathf.PI*2;
                int index=y*(longitude+1)+x;
                vertices[index]=new Vector3(Mathf.Sin(phi)*Mathf.Cos(theta),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(theta));
                uv[index]=new Vector2(u,1-v);
            }
            int at=0;
            for(int y=0;y<latitude;y++) for(int x=0;x<longitude;x++)
            {
                int a=y*(longitude+1)+x,b=a+longitude+1;
                triangles[at++]=a; triangles[at++]=a+1; triangles[at++]=b;
                triangles[at++]=a+1; triangles[at++]=b+1; triangles[at++]=b;
            }
            var mesh=new Mesh {name="Earth smooth UV sphere",vertices=vertices,normals=vertices,uv=uv,triangles=triangles};
            mesh.RecalculateBounds(); return mesh;
        }
    }
}
