using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    // Generates only an exterior prefab and its dependencies. Campaign scenes stay untouched.
    public static class CampaignExteriorAuthoring
    {
        const string Kit="Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Prefabs/";
        const string Styled="Assets/Sci-Fi Styled Modular Pack/Prefabs/";
        const string Output="Assets/DoppelgangerGenerated/Exterior";
        const string Earth="Assets/DoppelgangerGenerated/Earth/";
        const string Prefab="Assets/ZeroSector/Resources/Doppelganger/Exterior.prefab";
        public static void Prepare()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before authoring the EVA exterior.");
            Directory.CreateDirectory(Output+"/Materials");
            Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
            Directory.CreateDirectory("Diagnostics/CampaignPreviews");
            AssetDatabase.Refresh();
            if(!AssetDatabase.LoadAssetAtPath<Mesh>(Earth+"EarthSphere.asset") || !AssetDatabase.LoadAssetAtPath<Material>(Earth+"Surface.mat"))
                EarthAuthoring.Prepare();
            var original=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root=new GameObject("Orbital antenna / exterior deck");
                var shell=Look("Deck basalt",new Color(.07f,.095f,.12f),.72f,.37f);
                var alloy=Look("Dish titanium",new Color(.48f,.56f,.64f),.72f,.43f);
                var safety=Look("Safety amber",new Color(.44f,.16f,.035f),.25f,.38f);
                var cyan=Look("EVA cyan marker",new Color(.07f,.31f,.46f),.2f,.4f,new Color(.07f,.85f,1.5f));
                var amber=Look("EVA amber marker",new Color(.47f,.18f,.035f),.2f,.4f,new Color(1.65f,.47f,.08f));
                var architecture=Group(root.transform,"Pressure hull and maintenance deck",Vector3.zero);
                for(int side=-1;side<=1;side+=2)
                    for(int row=0;row<3;row++)
                        Art(Kit+"Floors/Floor_Squared_02_6x6.prefab",architecture,new Vector3(side*3,0,-4+row*6),6,0,true);
                Beam(architecture,"Hull perimeter skirt",new Vector3(0,-.54f,2),new Vector3(12.23f,.65f,18.16f),shell);
                Collider(architecture,"Continuous walkable deck",new Vector3(0,-.17f,2),new Vector3(12,.34f,18));
                // The deck is a starboard maintenance shelf; the port-facing side still belongs to the ship.
                Beam(architecture,"Outer pressure hull",new Vector3(-9.1f,-.8f,1.2f),new Vector3(4.7f,5.7f,20.3f),shell);
                for(int row=0;row<3;row++)
                    Art(Kit+"Walls/Wall_Simple_03_Long.prefab",architecture,new Vector3(-6.47f,-.92f,-4+row*6),6,3.56f,false,90);
                Rod(architecture,"Hull longitudinal supply conduit",new Vector3(-6.14f,.42f,-6.8f),new Vector3(-6.14f,.42f,10.68f),.15f,safety);
                Art(Kit+"Doors/DoorWay_01_Large.prefab",architecture,new Vector3(0,0,-7.08f),4.2f,4.15f);
                foreach(int side in new[]{-1,1})
                    Art(Kit+"Walls/Wall_Simple_03_Long.prefab",architecture,new Vector3(side*4.15f,0,-7.16f),4.2f,4.25f);
                Beam(architecture,"Sealed inner door",new Vector3(0,1.36f,-7.43f),new Vector3(2.55f,2.74f,.14f),shell);
                Beam(architecture,"Airlock beacon",new Vector3(0,2.78f,-7.12f),new Vector3(1.53f,.07f,.12f),amber);
                Collider(architecture,"Airlock wall collision",new Vector3(0,1.7f,-7.12f),new Vector3(12.2f,3.4f,.22f));
                foreach(int side in new[]{-1,1})
                {
                    float x=side*5.83f;
                    Rail(architecture,new Vector3(x,0,-6.9f),new Vector3(x,0,10.92f),shell,amber);
                    Collider(architecture,"Edge rail collision",new Vector3(x,1.5f,2),new Vector3(.17f,3,18.1f));
                    for(int i=0;i<12;i++)
                        Beam(architecture,"Magnetic bootway / marker",new Vector3(side*1.02f,.027f,-5.85f+i*1.37f),
                            new Vector3(.055f,.036f,.86f),side<0 ? amber : cyan);
                }
                Rail(architecture,new Vector3(-5.83f,0,10.92f),new Vector3(5.83f,0,10.92f),shell,amber);
                Collider(architecture,"Front rail collision",new Vector3(0,1.5f,10.92f),new Vector3(12.2f,3,.17f));

                var equipment=Group(root.transform,"Directional antenna and uplink",Vector3.zero);
                var dish=Group(equipment,"DishPivot",new Vector3(-3.55f,2.26f,3.9f));
                dish.localRotation=Quaternion.Euler(5,-43,0);
                Art(Styled+"Machines/shield_generator.prefab",equipment,new Vector3(-3.55f,0,3.9f),1.52f,1.91f);
                Rod(equipment,"Azimuth trunnion",new Vector3(-3.55f,.55f,3.9f),dish.localPosition,.32f,shell);
                Collider(equipment,"Antenna base collision",new Vector3(-3.55f,.65f,3.9f),new Vector3(1.28f,1.3f,1.28f));
                var reflector=new GameObject("Parabolic titanium reflector",typeof(MeshFilter),typeof(MeshRenderer));
                reflector.transform.SetParent(dish,false);
                reflector.GetComponent<MeshFilter>().sharedMesh=Reflector();
                reflector.GetComponent<MeshRenderer>().sharedMaterial=alloy;
                var tip=new Vector3(0,0,1.13f);
                for(int i=0;i<8;i++)
                {
                    float theta=i*Mathf.PI*.25f;
                    var rim=new Vector3(1.18f*Mathf.Cos(theta),1.18f*Mathf.Sin(theta),.44f);
                    var next=new Vector3(1.18f*Mathf.Cos(theta+Mathf.PI*.25f),1.18f*Mathf.Sin(theta+Mathf.PI*.25f),.44f);
                    Rod(dish,"Segmented reflector rim",rim,next,.045f,shell);
                    if(i%2==0) Rod(dish,"Waveguide suspension",rim*.94f,tip,.04f,alloy);
                }
                Rod(dish,"Signal feed",new Vector3(0,0,.93f),tip+Vector3.forward*.12f,.11f,cyan);
                Beam(equipment,"Antenna maintenance chest",new Vector3(-4.26f,.27f,6.08f),new Vector3(.64f,.54f,1.12f),shell);
                Art(Kit+"Props/Crate_01.prefab",equipment,new Vector3(-4.26f,0,6.08f),.83f,.71f);
                Art(Styled+"Machines/Battery_medium.prefab",equipment,new Vector3(4.48f,0,8.45f),1.16f,1.4f);
                Terminal(equipment,"antennaAlign","Навести антенну на Землю","A  /  НАВЕДЕНИЕ",new Vector3(-2.2f,1.2f,1.5f),amber,shell);
                Terminal(equipment,"antennaSend","Передать сигнал бедствия","B  /  СИГНАЛ SOS",new Vector3(2.2f,1.2f,7.5f),cyan,shell);

                // Convert the source art before adding the special Earth and glyph shaders.
                FpsArenaGraphics.ConvertMaterials(root,Output);
                var planet=EarthAuthoring.Create(new Vector3(88,-53,227),101);
                planet.transform.SetParent(root.transform,true);
                var starMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/DoppelgangerGenerated/LearningArena/Materials/Sky.mat");
                if(starMaterial && starMaterial.shader && starMaterial.shader.name=="Doppelganger/DeepSpace")
                {
                    var dome=new GameObject("Orbital starfield",typeof(MeshFilter),typeof(MeshRenderer));
                    dome.transform.SetParent(root.transform,false); dome.transform.localScale=Vector3.one*720;
                    dome.GetComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Earth+"EarthSphere.asset");
                    var renderer=dome.GetComponent<MeshRenderer>(); renderer.sharedMaterial=starMaterial;
                    renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                }
                Label(root.transform,"ВНЕШНИЙ ШЛЮЗ  /  EVA-04",new Vector3(0,3.08f,-6.95f),180,new Color(.52f,.86f,1),.043f);
                Label(root.transform,"A  /  НАВЕДЕНИЕ",new Vector3(-2.2f,1.84f,1.87f),0,new Color(1,.72f,.32f),.033f);
                Label(root.transform,"B  /  СИГНАЛ SOS",new Vector3(2.2f,1.84f,7.87f),0,new Color(.52f,.88f,1),.033f);
                var sun=new GameObject("Orbital hard light",typeof(Light)).GetComponent<Light>();
                sun.transform.SetParent(root.transform,false); sun.transform.rotation=Quaternion.LookRotation(new Vector3(.55f,-.35f,.8f));
                sun.type=LightType.Directional; sun.color=new Color(.81f,.89f,1); sun.intensity=1.58f; sun.shadows=LightShadows.Soft;
                var fill=new GameObject("Airlock service lamp",typeof(Light)).GetComponent<Light>();
                fill.transform.SetParent(root.transform,false); fill.transform.localPosition=new Vector3(0,2.78f,-6.6f);
                fill.type=LightType.Point; fill.color=new Color(1,.62f,.31f); fill.range=5; fill.intensity=3.6f; fill.shadows=LightShadows.None;

                Validate(root);
                if(!PrefabUtility.SaveAsPrefabAsset(root,Prefab)) throw new IOException("Could not save the exterior prefab.");
                AssetDatabase.SaveAssets();
                File.WriteAllText("Diagnostics/CampaignPreviews/exterior-authoring.txt",
                    $"PASS: {Prefab}\nFloor verified at spawn and both antenna stations; rails and hull collider present.\n"+$"{root.GetComponentsInChildren<Renderer>().Length} renderers; no downloaded assets.\n");
            }
            finally
            {
                if(original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(scene,true);
            }
        }

        static void Validate(GameObject root)
        {
            var points=root.GetComponentsInChildren<CampaignInteractable>();
            if(points.Length!=2 || !points.Any(x=>x.id=="antennaAlign") || !points.Any(x=>x.id=="antennaSend"))
                throw new InvalidOperationException("EVA requires both interaction anchors.");
            Physics.SyncTransforms();
            foreach(var at in new[]{new Vector3(0,.05f,-5),new Vector3(-2.2f,.05f,.55f),new Vector3(2.2f,.05f,6.55f)})
            {
                if(!Physics.RaycastAll(at+Vector3.up*.22f,Vector3.down,1,~0,QueryTriggerInteraction.Ignore)
                    .Any(hit=>hit.collider.name=="Continuous walkable deck" && hit.collider.transform.IsChildOf(root.transform)))
                    throw new InvalidOperationException("No exterior deck below "+at);
                if(Physics.OverlapCapsule(at+Vector3.up*.42f,at+Vector3.up*1.4f,.31f,~0,QueryTriggerInteraction.Ignore)
                    .Any(hit=>hit.transform.IsChildOf(root.transform)))
                    throw new InvalidOperationException("An EVA approach is blocked at "+at);
            }
            foreach(var mesh in root.GetComponentsInChildren<MeshRenderer>())
                if(mesh.sharedMaterials.Any(mat=>!mat || !mat.shader)) throw new InvalidOperationException("Missing exterior material on "+mesh.name);
        }

        static Material Look(string name,Color color,float metal,float smooth,Color? emission=null)
        {
            string path=Output+"/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material)
            {
                var shader=Shader.Find("Universal Render Pipeline/Lit");
                if(!shader) throw new InvalidOperationException("URP Lit is missing.");
                material=new Material(shader); AssetDatabase.CreateAsset(material,path);
            }
            material.SetColor("_BaseColor",color); material.SetFloat("_Metallic",metal);
            material.SetFloat("_Smoothness",smooth); material.SetFloat("_Cull",0);
            if(emission.HasValue) { material.SetColor("_EmissionColor",emission.Value); material.EnableKeyword("_EMISSION"); }
            EditorUtility.SetDirty(material); return material;
        }

        static Transform Group(Transform parent,string name,Vector3 position)
        {
            var group=new GameObject(name).transform; group.SetParent(parent,false); group.localPosition=position; return group;
        }
        static void Collider(Transform parent,string name,Vector3 at,Vector3 size)
        { var box=Group(parent,name,at).gameObject.AddComponent<BoxCollider>(); box.size=size; }
        static GameObject Beam(Transform parent,string name,Vector3 at,Vector3 size,Material material)
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube); box.name=name; box.transform.SetParent(parent,false);
            box.transform.localPosition=at; box.transform.localScale=size;
            UnityEngine.Object.DestroyImmediate(box.GetComponent<BoxCollider>());
            box.GetComponent<MeshRenderer>().sharedMaterial=material; return box;
        }
        static void Rod(Transform parent,string name,Vector3 a,Vector3 b,float size,Material material)
        {
            var line=Beam(parent,name,(a+b)*.5f,new Vector3(size,size,Vector3.Distance(a,b)),material);
            line.transform.localRotation=Quaternion.LookRotation(b-a);
        }
        static void Rail(Transform parent,Vector3 a,Vector3 b,Material shell,Material signal)
        {
            Rod(parent,"Upper handrail",a+Vector3.up*1.04f,b+Vector3.up*1.04f,.12f,shell);
            Rod(parent,"Lower stanchion cable",a+Vector3.up*.47f,b+Vector3.up*.47f,.06f,shell);
            int count=Mathf.CeilToInt(Vector3.Distance(a,b)/2.8f);
            for(int i=0;i<=count;i++)
            {
                var at=Vector3.Lerp(a,b,i/(float)count);
                Beam(parent,"Safety rail stanchion",at+Vector3.up*.55f,new Vector3(.16f,1.1f,.16f),shell);
                Beam(parent,"Safety rail end cap",at+Vector3.up*1.13f,new Vector3(.19f,.07f,.19f),signal);
            }
        }

        static void Art(string path,Transform parent,Vector3 at,float width,float maxHeight,bool floor=false,float yaw=0)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!prefab) throw new FileNotFoundException("Missing EVA prop: "+path);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            model.transform.localPosition=Vector3.zero; model.transform.localRotation=Quaternion.identity;
            foreach(var script in model.GetComponentsInChildren<MonoBehaviour>(true)) if(script) UnityEngine.Object.DestroyImmediate(script);
            foreach(var item in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(item.gameObject);
            foreach(var lamp in model.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(lamp);
            foreach(var collision in model.GetComponentsInChildren<UnityEngine.Collider>(true)) UnityEngine.Object.DestroyImmediate(collision);
            foreach(var lod in model.GetComponentsInChildren<LODGroup>(true)) BridgeVisualQuality.UseHighestDetail(lod);
            var list=model.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if(list.Length==0) throw new InvalidOperationException("EVA module has no renderers: "+path);
            Bounds BoundsOf()
            { var bounds=list[0].bounds; foreach(var renderer in list) bounds.Encapsulate(renderer.bounds); return bounds; }
            var size=BoundsOf().size;
            if(!floor && size.z>size.x*1.2f) { model.transform.localRotation=Quaternion.Euler(0,90,0); size=BoundsOf().size; }
            float scale=width/Mathf.Max(.01f,floor ? Mathf.Max(size.x,size.z) : size.x);
            if(maxHeight>0) scale=Mathf.Min(scale,maxHeight/Mathf.Max(size.y,.01f));
            model.transform.localScale*=scale;
            var box=BoundsOf();
            model.transform.localPosition=at-(new Vector3(box.center.x,floor ? box.max.y : box.min.y,box.center.z)-parent.position);
            if(yaw!=0)
            {
                var mount=Group(parent,model.name+" / hull mount",at);
                model.transform.SetParent(mount,true); mount.localRotation=Quaternion.Euler(0,yaw,0);
            }
        }

        static void Terminal(Transform parent,string id,string title,string label,Vector3 point,Material beacon,Material shell)
        {
            var anchor=Group(parent,"EVA / "+label,point);
            var interaction=anchor.gameObject.AddComponent<CampaignInteractable>(); interaction.id=id; interaction.title=title; interaction.range=2.6f;
            Art(Styled+"Decorative elements/computer_station.prefab",parent,point+new Vector3(0,-1.2f,.38f),1.4f,1.42f);
            Beam(parent,"Station LED header",point+new Vector3(0,.39f,.31f),new Vector3(.91f,.045f,.13f),beacon);
            Beam(parent,"Station floor base",point+new Vector3(0,-1.14f,.37f),new Vector3(1.63f,.12f,1.12f),shell);
            Collider(parent,"Console stand collision",point+new Vector3(0,-.81f,.37f),new Vector3(.91f,.78f,.65f));
        }
        static void Label(Transform parent,string title,Vector3 at,float yaw,Color color,float size)
        {
            var obj=Group(parent,"Exterior wayfinding / "+title,at);
            obj.localRotation=Quaternion.Euler(0,yaw,0);
            var label=obj.gameObject.AddComponent<TextMesh>();
            label.text=title; label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.anchor=TextAnchor.MiddleCenter; label.alignment=TextAlignment.Center;
            label.color=color; label.fontSize=64; label.characterSize=size;
            var renderer=obj.GetComponent<MeshRenderer>();
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/DoppelgangerGenerated/Campaign/Materials/WorldSign.mat");
            if(material) renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            obj.gameObject.AddComponent<CampaignWorldSign>();
        }
        static Mesh Reflector()
        {
            string path=Output+"/ParabolicDish.asset";
            var found=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(found) return found;
            const int bands=8,sides=64;
            var positions=new Vector3[(bands+1)*(sides+1)]; var normals=new Vector3[positions.Length]; var uv=new Vector2[positions.Length];
            var triangles=new int[bands*sides*6]; int index=0;
            for(int r=0;r<=bands;r++) for(int s=0;s<=sides;s++)
            {
                float t=r/(float)bands,angle=s/(float)sides*2*Mathf.PI;
                float x=Mathf.Cos(angle)*1.18f*t,y=Mathf.Sin(angle)*1.18f*t;
                int at=r*(sides+1)+s;
                positions[at]=new Vector3(x,y,t*t*.44f); normals[at]=new Vector3(-x*.632f,-y*.632f,1).normalized;
                uv[at]=new Vector2(s/(float)sides,t);
                if(r==bands || s==sides) continue;
                int b=at+sides+1;
                triangles[index++]=at; triangles[index++]=b; triangles[index++]=b+1;
                triangles[index++]=at; triangles[index++]=b+1; triangles[index++]=at+1;
            }
            var mesh=new Mesh{name="EVA parabolic reflector",vertices=positions,normals=normals,uv=uv,triangles=triangles};
            mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,path); return mesh;
        }
    }
}
