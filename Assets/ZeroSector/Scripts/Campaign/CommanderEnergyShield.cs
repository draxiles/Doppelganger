using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSector.Campaign
{
    // The visual panel and interception surface share a transform. No invisible
    // all-round armour: a bullet must cross this finite panel from the front.
    public sealed class CommanderEnergyShield : MonoBehaviour
    {
        public bool Deployed => DeployAmount>=.95f && owner && owner.Energy==1 && owner.Running;
        public float DeployAmount { get; private set; }
        public float Charge => owner ? owner.Shield : 0;
        public bool IsDepleted => Charge<=.001f;
        public int BlockedHits { get; private set; }
        public float BlockedDamage { get; private set; }
        public Vector3 LastImpact { get; private set; }
        public Transform Surface => panel;
        const float RadiusX=.56f,RadiusY=.70f,DeploymentSeconds=.3f;
        CampaignGame owner;
        Transform panel,impact;
        MeshRenderer fieldRenderer,impactRenderer;
        Material fieldMaterial,impactMaterial;
        Mesh fieldMesh,impactMesh;
        float hitFlash;
        readonly Vector3 readyPosition=new(-.16f,-.32f,.78f);

        public void Initialize(CampaignGame game)
        {
            owner=game;
            if(panel || !owner || !owner.eyes) return;
            var template=Resources.Load<Material>("Doppelganger/PulseTrail");
            if(!template || !template.shader)
            {
                Debug.LogWarning("Deployable shield: PulseTrail material dependency is missing.");
                return;
            }
            fieldMaterial=new Material(template){name="Commander shield holographic field"};
            impactMaterial=new Material(template){name="Commander shield impact ripple"};
            panel=new GameObject("Commander / deployable wrist shield").transform;
            panel.SetParent(owner.eyes.transform,false);
            fieldMesh=BuildPanel(); fieldRenderer=MeshObject("Hexagonal energy membrane",panel,fieldMesh,fieldMaterial);
            impactMesh=BuildRipple(); impactRenderer=MeshObject("Bullet interception ripple",panel,impactMesh,impactMaterial);
            impact=impactRenderer.transform; impactRenderer.enabled=false;
            panel.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if(!owner || owner.Paused || owner.TerminalOpen) return;
            Tick(Mathf.Min(Time.deltaTime,.05f),owner.Running && owner.Energy==1);
        }

        // Public deterministic tick is also used by the isolated collision check.
        public void Tick(float dt,bool requested)
        {
            if(!panel || (owner && (owner.Paused || owner.TerminalOpen))) return;
            DeployAmount=Mathf.MoveTowards(DeployAmount,requested ? 1 : 0,Mathf.Max(0,dt)/DeploymentSeconds);
            panel.gameObject.SetActive(DeployAmount>.001f);
            float ease=DeployAmount*DeployAmount*(3-2*DeployAmount);
            panel.localPosition=readyPosition+new Vector3(-.24f,-.56f,-.16f)*(1-ease);
            panel.localRotation=Quaternion.Euler((1-ease)*42,(1-ease)*-48,(1-ease)*-35);
            panel.localScale=Vector3.one*Mathf.Lerp(.28f,1,ease);
            hitFlash=Mathf.MoveTowards(hitFlash,0,Mathf.Max(0,dt)*3.6f);
            Color tint=IsDepleted ? new Color(1,.26f,.06f,.27f) : new Color(.15f,.8f,1,.75f);
            fieldMaterial.SetColor("_BaseColor",tint*(1+hitFlash*.9f));
            if(impactRenderer)
            {
                impactRenderer.enabled=hitFlash>0;
                impact.localScale=Vector3.one*Mathf.Lerp(1.9f,.3f,hitFlash);
                impactMaterial.SetColor("_BaseColor",new Color(.45f,1,1,hitFlash));
            }
        }

        public float TryAbsorb(float damage,Vector3 source,Vector3 target,out Vector3 surfacePoint)
        {
            surfacePoint=target;
            if(!Deployed || IsDepleted || damage<=0 || !panel) return 0;
            Vector3 towardSource=source-owner.eyes.transform.position;
            if(towardSource.sqrMagnitude<.001f || Vector3.Dot(owner.eyes.transform.forward,towardSource.normalized)<.5735764f) return 0;
            Vector3 segment=target-source;
            float denominator=Vector3.Dot(panel.forward,segment);
            if(denominator>=-.0001f) return 0;
            float t=Vector3.Dot(panel.forward,panel.position-source)/denominator;
            if(t<0 || t>1) return 0;
            surfacePoint=source+segment*t;
            Vector3 local=panel.InverseTransformPoint(surfacePoint);
            if(local.x*local.x/(RadiusX*RadiusX)+local.y*local.y/(RadiusY*RadiusY)>1) return 0;
            float absorbed=Mathf.Min(Charge,damage);
            if(absorbed<=0) return 0;
            BlockedHits++; BlockedDamage+=absorbed; LastImpact=surfacePoint; hitFlash=1;
            impact.localPosition=new Vector3(local.x,local.y,-.008f);
            return absorbed;
        }

        static MeshRenderer MeshObject(string label,Transform parent,Mesh mesh,Material material)
        {
            var child=new GameObject(label,typeof(MeshFilter),typeof(MeshRenderer)); child.transform.SetParent(parent,false);
            child.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=child.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            return renderer;
        }

        static Mesh BuildPanel()
        {
            var builder=new MeshBuilder();
            var dim=new Color(.3f,.65f,.8f,.035f);
            var bright=new Color(.5f,.92f,1,.7f);
            // Twelve bevelled segments establish an authored silhouette rather than
            // a sphere/cube primitive. Sparse cells keep the view through it clear.
            const int sides=12;
            for(int i=0;i<sides;i++)
            {
                float a=Mathf.PI*2*i/sides,b=Mathf.PI*2*(i+1)/sides;
                Vector3 outerA=new(Mathf.Cos(a)*RadiusX,Mathf.Sin(a)*RadiusY,0);
                Vector3 outerB=new(Mathf.Cos(b)*RadiusX,Mathf.Sin(b)*RadiusY,0);
                builder.Quad(outerA,outerB,outerB*.974f,outerA*.974f,bright);
                builder.Triangle(Vector3.zero,outerA,outerB,dim);
                if(i%3==0) builder.Quad(outerA*.94f,outerB*.94f,outerB*.928f,outerA*.928f,new Color(1,1,1,.5f));
            }
            const float cell=.08f;
            for(int row=-4;row<=4;row++) for(int col=-4;col<=4;col++)
            {
                Vector3 center=new(col*.145f+(row%2==0 ? 0 : .0725f),row*.125f,-.002f);
                if(center.x*center.x/(.475f*.475f)+center.y*center.y/(.60f*.60f)>1) continue;
                for(int side=0;side<6;side++)
                {
                    float a=side*Mathf.PI/3+Mathf.PI/6,b=(side+1)*Mathf.PI/3+Mathf.PI/6;
                    Vector3 p=new(Mathf.Cos(a)*cell,Mathf.Sin(a)*cell,0),q=new(Mathf.Cos(b)*cell,Mathf.Sin(b)*cell,0);
                    builder.Quad(center+p,center+q,center+q*.973f,center+p*.973f,new Color(.4f,.83f,1,.19f));
                }
            }
            // Wrist projector: layered octagonal cuff, top emitter and three fins.
            builder.Cuff(new Vector3(-.15f,.09f,-.23f),.11f,.085f,.23f,new Color(.1f,.15f,.18f,.9f));
            builder.Cuff(new Vector3(-.15f,.09f,-.23f),.118f,.091f,.07f,new Color(.23f,.32f,.34f,.8f));
            for(int i=0;i<3;i++) builder.Quad(new Vector3(-.24f+i*.07f,.19f,-.34f),new Vector3(-.205f+i*.07f,.19f,-.34f),
                new Vector3(-.205f+i*.07f,.22f,-.1f),new Vector3(-.24f+i*.07f,.22f,-.1f),new Color(.12f,.27f,.34f,.8f));
            return builder.Build("Commander authored shield / hex field and wrist projector");
        }

        static Mesh BuildRipple()
        {
            var builder=new MeshBuilder();
            for(int i=0;i<24;i++)
            {
                float a=i*Mathf.PI*2/24,b=(i+1)*Mathf.PI*2/24;
                Vector3 p=new(Mathf.Cos(a)*.16f,Mathf.Sin(a)*.16f,0),q=new(Mathf.Cos(b)*.16f,Mathf.Sin(b)*.16f,0);
                builder.Quad(p,q,q*.78f,p*.78f,Color.white);
            }
            return builder.Build("Shield bullet ripple");
        }

        sealed class MeshBuilder
        {
            readonly List<Vector3> vertices=new(); readonly List<Color> colors=new(); readonly List<int> triangles=new();
            public void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color)
            {
                int n=vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
                colors.Add(color); colors.Add(color); colors.Add(color); triangles.Add(n); triangles.Add(n+1); triangles.Add(n+2);
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color) { Triangle(a,b,c,color); Triangle(a,c,d,color); }
            public void Cuff(Vector3 center,float width,float height,float length,Color color)
            {
                for(int i=0;i<8;i++)
                {
                    float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;
                    Vector3 p=new(Mathf.Cos(a)*width,Mathf.Sin(a)*height,0),q=new(Mathf.Cos(b)*width,Mathf.Sin(b)*height,0);
                    Quad(center+p+Vector3.back*length*.5f,center+q+Vector3.back*length*.5f,
                        center+q+Vector3.forward*length*.5f,center+p+Vector3.forward*length*.5f,color);
                }
            }
            public Mesh Build(string label)
            {
                var mesh=new Mesh{name=label}; mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds(); return mesh;
            }
        }

        void OnDestroy()
        {
            Release(panel ? panel.gameObject : null); Release(fieldMaterial); Release(impactMaterial); Release(fieldMesh); Release(impactMesh);
        }
        static void Release(Object value) { if(value) { if(Application.isPlaying) Destroy(value); else DestroyImmediate(value); } }
    }
}
