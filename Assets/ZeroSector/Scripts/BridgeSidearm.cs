using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSector
{
    // The same P-7 is used for the safe bridge draw and as a combat viewmodel.
    public sealed class BridgeSidearm : MonoBehaviour
    {
        readonly List<Object> art = new();
        Transform slideMount, magazineMount, supportHand;
        Texture2D surface;
        public Transform Muzzle { get; private set; }
        public AudioClip ShotSound { get; private set; }

        public static BridgeSidearm Create(Transform parent,bool combat=false)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if(!shader) return null;
            var mount = new GameObject("P-7 / Service pistol");
            mount.transform.SetParent(parent,false);
            var sidearm = mount.AddComponent<BridgeSidearm>();
            sidearm.Assemble(shader,combat);
            if(combat) sidearm.CreateShotSound();
            return sidearm;
        }

        void Assemble(Shader shader,bool combat)
        {
            surface=CreateSurface();
            var frame = Finish(shader,"Graphite polymer",new Color(.075f,.095f,.11f),.16f,.32f);
            var slide = Finish(shader,"Parkerized steel",new Color(.21f,.245f,.255f),.44f,.51f);
            var detail = Finish(shader,"Machined steel",new Color(.46f,.49f,.48f),.59f,.46f);
            var dark = Finish(shader,"Recesses",new Color(.025f,.033f,.042f),.12f,.18f);
            var signal = Finish(shader,"Amber safety",new Color(.9f,.36f,.075f),.12f,.45f);
            signal.EnableKeyword("_EMISSION");
            signal.SetColor("_EmissionColor",new Color(.55f,.19f,.04f));
            var glove = Finish(shader,"Sealed glove",new Color(.07f,.11f,.15f),.08f,.28f);
            var padding = Finish(shader,"Glove armor",new Color(.18f,.22f,.27f),.16f,.38f);

            // Profiles use forward, up coordinates. Vary the width and cut the corners
            // so the receiver reads as machined hardware at close range.
            slideMount=Mount("Recoiling slide");
            magazineMount=Mount("Detachable magazine");
            Muzzle=Mount("P-7 muzzle");
            Muzzle.localPosition=new Vector3(0,.035f,.16f);
            Profile("Slide",.047f,slide,slideMount,(-.107f,.010f),(.144f,.010f),(.156f,.022f),
                (.147f,.054f),(.125f,.060f),(-.087f,.060f),(-.107f,.044f));
            Profile("Frame",.042f,frame,(-.114f,.014f),(.143f,.014f),(.129f,-.019f),
                (.036f,-.029f),(-.086f,-.030f),(-.114f,-.009f));
            Profile("Machined rail",.0435f,detail,(-.100f,.008f),(.139f,.008f),(.128f,.003f),(-.099f,.002f));
            Profile("Grip",.034f,frame,(-.099f,-.019f),(-.028f,-.019f),
                (-.049f,-.133f),(-.111f,-.144f),(-.133f,-.121f));
            Profile("Magazine body",.032f,dark,magazineMount,(-.127f,-.058f),(-.062f,-.060f),
                (-.057f,-.133f),(-.131f,-.142f));
            Profile("Magazine plate",.038f,detail,magazineMount,(-.137f,-.126f),(-.052f,-.126f),
                (-.050f,-.142f),(-.127f,-.153f));
            Profile("Guard front",.021f,frame,(.034f,-.019f),(.053f,-.020f),
                (.041f,-.062f),(.022f,-.076f),(.017f,-.068f));
            Profile("Guard lower",.021f,frame,(-.053f,-.056f),(-.042f,-.065f),
                (.029f,-.062f),(.022f,-.077f),(-.049f,-.078f));
            Profile("Trigger",.014f,detail,(-.030f,-.022f),(-.016f,-.025f),
                (-.028f,-.042f),(-.029f,-.054f),(-.041f,-.049f));

            Profile("Extractor port",.049f,dark,slideMount,(-.013f,.022f),(.052f,.023f),
                (.042f,.044f),(.000f,.044f));
            Profile("Chamber through the extractor",.0495f,detail,slideMount,(.001f,.025f),(.042f,.026f),
                (.033f,.034f),(.002f,.035f));
            Profile("Slide accent",.0472f,signal,slideMount,(.071f,.045f),(.13f,.045f),(.126f,.041f),(.070f,.041f));
            for(int i=0;i<5;i++)
            {
                float z=-.092f+i*.010f;
                Profile("Slide grip cut",.048f,frame,slideMount,(z,.016f),(z+.004f,.016f),
                    (z+.013f,.046f),(z+.009f,.046f));
            }
            foreach(float x in new[]{-.014f,.014f})
            {
                var sight=Mount("Rear notch / steel ear",slideMount);
                sight.localPosition=new Vector3(x,0,0);
                Profile("Rear sight",.011f,dark,sight,(-.106f,.056f),(-.082f,.056f),
                    (-.081f,.072f),(-.101f,.073f));
                Profile("Rear sight mark",.004f,signal,sight,(-.107f,.063f),(-.104f,.063f),
                    (-.104f,.069f),(-.107f,.069f));
            }
            Profile("Front sight",.010f,signal,slideMount,(.113f,.056f),(.129f,.056f),
                (.125f,.067f),(.115f,.067f));
            Profile("Safe indicator",.044f,signal,(-.091f,-.012f),(-.068f,-.012f),
                (-.065f,-.004f),(-.091f,-.004f));
            Bore(detail,dark);

            // The bridge keeps the index finger outside the trigger guard.
            var arm = new GameObject("Right arm / pressure glove").transform;
            arm.SetParent(transform,false);
            arm.localPosition = new Vector3(.024f,0,0);
            Profile("Sleeve",.075f,glove,arm,(-.264f,-.226f),(-.148f,-.155f),
                (-.111f,-.119f),(-.086f,-.132f),(-.132f,-.195f),(-.255f,-.267f));
            Profile("Cuff armor",.080f,padding,arm,(-.180f,-.168f),(-.145f,-.146f),
                (-.126f,-.175f),(-.159f,-.201f));
            Profile("Knuckle armor",.055f,padding,arm,(-.144f,-.116f),(-.125f,-.072f),
                (-.094f,-.052f),(-.073f,-.064f),(-.075f,-.114f),(-.103f,-.145f));
            Profile("Palm",.068f,glove,arm,(-.143f,-.092f),(-.125f,-.046f),
                (-.093f,-.033f),(-.057f,-.048f),(-.058f,-.102f),(-.103f,-.136f));
            if(combat)
                Profile("Trigger finger",.026f,glove,arm,(-.091f,-.045f),(-.045f,-.043f),
                    (-.018f,-.036f),(-.016f,-.050f),(-.046f,-.062f),(-.090f,-.058f));
            else
                Profile("Indexed finger",.024f,glove,arm,(-.094f,-.040f),(-.056f,-.027f),
                    (.027f,-.027f),(.035f,-.038f),(-.035f,-.049f),(-.091f,-.054f));
            for(int i=0;i<3;i++)
            {
                float y=-.063f-i*.019f, z=-.036f-i*.006f;
                Profile("Wrapped grip finger",.066f,glove,arm,(z-.032f,y+.007f),(z,y+.008f),
                    (z+.010f,y-.001f),(z-.004f,y-.010f),(z-.033f,y-.010f));
            }
            if(combat)
            {
                supportHand=Mount("Left arm / supporting the grip");
                Profile("Support sleeve",.072f,glove,supportHand,(-.257f,-.237f),(-.159f,-.143f),
                    (-.107f,-.119f),(-.086f,-.146f),(-.13f,-.184f),(-.246f,-.268f));
                Profile("Support cuff",.076f,padding,supportHand,(-.175f,-.157f),(-.15f,-.134f),
                    (-.123f,-.157f),(-.154f,-.19f));
                Profile("Support palm",.053f,glove,supportHand,(-.139f,-.142f),(-.106f,-.081f),
                    (-.064f,-.061f),(-.041f,-.076f),(-.066f,-.124f),(-.113f,-.15f));
                Profile("Support thumb",.042f,padding,supportHand,(-.091f,-.09f),(-.072f,-.063f),
                    (-.025f,-.051f),(-.02f,-.062f),(-.066f,-.087f));
            }
        }

        Transform Mount(string title,Transform parent=null)
        {
            var node=new GameObject(title).transform;
            node.SetParent(parent ? parent : transform,false);
            return node;
        }

        public void AnimateCombat(float shot,float reload)
        {
            float rack=Mathf.Sin(Mathf.Clamp01((reload-.77f)/.16f)*Mathf.PI);
            slideMount.localPosition=Vector3.back*Mathf.Max(.036f*shot,.043f*rack);
            float release=Mathf.SmoothStep(0,1,Mathf.Clamp01((reload-.22f)/.15f));
            float insert=Mathf.SmoothStep(1,0,Mathf.Clamp01((reload-.52f)/.18f));
            float exchange=release*insert;
            magazineMount.localPosition=new Vector3(-.01f,-.12f,-.045f)*exchange;
            magazineMount.gameObject.SetActive(reload<.37f || reload>=.52f);
            float reach=Mathf.Sin(Mathf.Clamp01((reload-.68f)/.31f)*Mathf.PI);
            supportHand.localPosition=new Vector3(-.034f,0,0)+new Vector3(-.04f,-.14f,-.07f)*exchange+
                new Vector3(-.007f,.13f,.026f)*reach;
            supportHand.localRotation=Quaternion.Euler(16*exchange,-12*reach,13*exchange);
        }

        void CreateShotSound()
        {
            const int rate=24000;
            var samples=new float[rate/5];
            var random=new System.Random(119);
            float body=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=(float)i/rate;
                float noise=(float)random.NextDouble()*2-1;
                body=Mathf.Lerp(body,noise,.24f);
                samples[i]=Mathf.Clamp(noise*.62f*Mathf.Exp(-t*136)+
                    body*.64f*Mathf.Exp(-t*40)+Mathf.Sin(t*1040)*.25f*Mathf.Exp(-t*83),-.9f,.9f);
            }
            ShotSound=AudioClip.Create("P-7 pulse discharge",samples.Length,1,rate,false);
            ShotSound.SetData(samples,0);
            art.Add(ShotSound);
        }

        Material Finish(Shader shader,string title,Color color,float metal,float smoothness)
        {
            var material = new Material(shader) {name=title};
            material.SetColor("_BaseColor",color);
            material.SetFloat("_Metallic",metal);
            material.SetFloat("_Smoothness",smoothness);
            material.SetTexture("_BaseMap",surface);
            art.Add(material);
            return material;
        }

        Texture2D CreateSurface()
        {
            const int size=64;
            var texture=new Texture2D(size,size,TextureFormat.RGB24,true)
                {name="P-7 / brushed finish", wrapMode=TextureWrapMode.Repeat, filterMode=FilterMode.Trilinear, anisoLevel=4};
            var pixels=new Color32[size*size];
            var random=new System.Random(207);
            for(int y=0;y<size;y++)
            {
                int grain=random.Next(-12,10);
                for(int x=0;x<size;x++)
                {
                    byte value=(byte)Mathf.Clamp(231+grain+random.Next(-19,12),0,255);
                    pixels[y*size+x]=new Color32(value,value,value,255);
                }
            }
            texture.SetPixels32(pixels); texture.Apply(true,true);
            art.Add(texture);
            return texture;
        }

        void Profile(string name,float width,Material material,params (float forward,float up)[] outline)
            => Profile(name,width,material,transform,outline);

        void Profile(string name,float width,Material material,Transform parent,params (float forward,float up)[] outline)
        {
            var center = Vector3.zero;
            foreach(var (forward,up) in outline) center+=new Vector3(0,up,forward);
            center/=outline.Length;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float halfWidth=width*.5f;
            const float bevel=.0012f;

            for(int i=0;i<outline.Length;i++)
            {
                var (z1,y1)=outline[i];
                var (z2,y2)=outline[(i+1)%outline.Length];
                var a=new Vector3(0,y1,z1);
                var b=new Vector3(0,y2,z2);
                var insetA=Vector3.Lerp(a,center,.11f);
                var insetB=Vector3.Lerp(b,center,.11f);
                Vector3 rim=Vector3.right*(halfWidth-bevel), outer=Vector3.right*halfWidth;
                Quad(a-rim,b-rim,b+rim,a+rim);
                Quad(a+rim,b+rim,insetB+outer,insetA+outer);
                Quad(a-rim,b-rim,insetB-outer,insetA-outer);
                Triangle(center+outer,insetA+outer,insetB+outer);
                Triangle(center-outer,insetA-outer,insetB-outer);
            }
            AddMesh(name,parent,material,vertices,triangles);

            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                Triangle(a,b,c);
                Triangle(a,c,d);
            }
            void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                Vector3 normal=Vector3.Cross(b-a,c-a);
                if(Vector3.Dot(normal,(a+b+c)/3-center)<0) (b,c)=(c,b);
                int index=vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles.Add(index); triangles.Add(index+1); triangles.Add(index+2);
            }
        }

        void Bore(Material rim,Material dark)
        {
            const int segments=20;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var well = new List<Vector3> {new Vector3(0,.035f,.158f)};
            var wellTriangles = new List<int>();
            for(int i=0;i<=segments;i++)
            {
                float angle=i*Mathf.PI*2/segments;
                var direction=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
                var center=new Vector3(0,.035f,.159f);
                vertices.Add(center+direction*.014f);
                vertices.Add(center+direction*.008f);
                well.Add(well[0]+direction*.0082f);
                if(i==segments) continue;
                int k=i*2;
                triangles.Add(k); triangles.Add(k+2); triangles.Add(k+1);
                triangles.Add(k+2); triangles.Add(k+3); triangles.Add(k+1);
                wellTriangles.Add(0); wellTriangles.Add(i+1); wellTriangles.Add(i+2);
            }
            AddMesh("Muzzle crown",transform,rim,vertices,triangles);
            AddMesh("Bore",transform,dark,well,wellTriangles);
        }

        void AddMesh(string title,Transform parent,Material material,List<Vector3> vertices,List<int> triangles)
        {
            var model=new GameObject(title,typeof(MeshFilter),typeof(MeshRenderer));
            model.transform.SetParent(parent,false);
            var mesh=new Mesh {name=title};
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();
            var normals=mesh.normals;
            var uv=new Vector2[vertices.Count];
            for(int i=0;i<uv.Length;i++)
            {
                Vector3 point=vertices[i];
                uv[i]=Mathf.Abs(normals[i].x)>.6f
                    ? new Vector2(point.z,point.y)*19 : new Vector2(point.z+point.y,point.x)*19;
            }
            mesh.uv=uv;
            mesh.RecalculateBounds();
            art.Add(mesh);
            model.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=model.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
        }

        void OnDestroy()
        {
            foreach(var asset in art) if(asset) Destroy(asset);
        }
    }
}
