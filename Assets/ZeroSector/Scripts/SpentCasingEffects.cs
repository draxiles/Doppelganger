using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSector
{
    // Render-only shell cases. Casting our own sweeps lets them bounce off the ship
    // without creating colliders that interfere with movement or the next shot.
    sealed class SpentCasingEffects
    {
        const float Lifetime=3.8f;
        const float ContactSkin=.003f;
        readonly Casing[] pool=new Casing[18];
        readonly RaycastHit[] sweepHits=new RaycastHit[12];
        readonly System.Random random=new();
        readonly GameObject root;
        readonly Mesh mesh;
        readonly Material material;
        readonly Texture2D palette;
        int next;

        sealed class Casing
        {
            public Transform transform;
            public Vector3 velocity, spin, size;
            public float age;
            public bool resting;
        }

        public static SpentCasingEffects Create(Material weaponMaterial)
        {
            // An authored weapon already includes its shader in the Windows build.
            var shader=weaponMaterial && weaponMaterial.HasProperty("_BaseColor") ?
                weaponMaterial.shader : Shader.Find("Universal Render Pipeline/Lit");
            return shader ? new SpentCasingEffects(shader) : null;
        }

        SpentCasingEffects(Shader shader)
        {
            root=new GameObject("Ejected shell casings");
            mesh=BuildMesh();
            palette=new Texture2D(4,1,TextureFormat.RGBA32,false)
            {
                name="Spent casing brass, primer and soot",
                filterMode=FilterMode.Point,
                wrapMode=TextureWrapMode.Clamp
            };
            palette.SetPixels(new[]
            {
                new Color(.83f,.56f,.25f),new Color(1,.76f,.41f),
                new Color(.085f,.048f,.028f),new Color(.54f,.48f,.37f)
            });
            palette.Apply(false,true);
            material=new Material(shader) {name="Spent casing / machined brass"};
            material.SetTexture("_BaseMap",palette);
            material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_Metallic",.43f);
            material.SetFloat("_Smoothness",.57f);
        }

        public void Eject(Vector3 origin,Transform camera,Vector3 inheritedVelocity,Vector3 size)
        {
            var item=pool[next] ??= CreateCasing();
            next=(next+1)%pool.Length;
            item.age=0;
            item.resting=false;
            item.size=size;
            item.velocity=camera.right*Between(1.05f,1.48f)+camera.forward*Between(.62f,.86f)+
                Vector3.up*Between(1.55f,2.05f)+inheritedVelocity;
            item.spin=new Vector3(Between(540,870),Between(-700,-350),Between(320,610));
            item.transform.SetPositionAndRotation(origin,camera.rotation*Quaternion.Euler(22,Between(-36,36),-63));
            item.transform.localScale=size;
            item.transform.gameObject.SetActive(true);
        }

        Casing CreateCasing()
        {
            var instance=new GameObject("Spent casing",typeof(MeshFilter),typeof(MeshRenderer));
            instance.transform.SetParent(root.transform,false);
            instance.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=instance.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            return new Casing {transform=instance.transform};
        }

        public void Tick(float dt)
        {
            if(dt<=0) return;
            foreach(var item in pool)
            {
                if(item==null || !item.transform.gameObject.activeSelf) continue;
                item.age+=dt;
                if(item.age>=Lifetime)
                {
                    item.transform.gameObject.SetActive(false);
                    continue;
                }
                item.transform.localScale=item.size*Mathf.Clamp01((Lifetime-item.age)/.23f);
                if(!item.resting) Move(item,dt);
            }
        }

        void Move(Casing item,float dt)
        {
            item.velocity+=Physics.gravity*dt;
            Vector3 position=item.transform.position;
            Quaternion rotation=Quaternion.Euler(item.spin*dt)*item.transform.rotation;
            Vector3 step=item.velocity*dt;
            float distance=step.magnitude;
            float radius=item.size.x*.52f;
            if(distance>.0001f && Sweep(position,step/distance,radius,distance,out var hit))
            {
                position=hit.point+hit.normal*(radius+ContactSkin);
                Vector3 alongSurface=Vector3.ProjectOnPlane(item.velocity,hit.normal)*.62f;
                Vector3 rebound=-Vector3.Project(item.velocity,hit.normal)*.31f;
                item.velocity=alongSurface+rebound;
                item.spin*=.72f;
                if(hit.normal.y>.5f && item.velocity.sqrMagnitude<.16f)
                {
                    Vector3 side=Vector3.ProjectOnPlane(rotation*Vector3.up,hit.normal);
                    if(side.sqrMagnitude<.001f) side=Vector3.Cross(hit.normal,Vector3.right);
                    rotation=Quaternion.FromToRotation(rotation*Vector3.up,side.normalized)*rotation;
                    item.resting=true;
                }
            }
            else position+=step;
            item.transform.SetPositionAndRotation(position,rotation);
        }

        bool Sweep(Vector3 origin,Vector3 direction,float radius,float distance,out RaycastHit closest)
        {
            int count=Physics.SphereCastNonAlloc(origin,radius,direction,sweepHits,distance,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            closest=default;
            float minDistance=distance+.0001f;
            for(int i=0;i<count;i++)
            {
                var hit=sweepHits[i];
                if(hit.distance<=0 || hit.distance>=minDistance || hit.collider is CharacterController) continue;
                closest=hit;
                minDistance=hit.distance;
            }
            return closest.collider;
        }

        float Between(float min,float max) => Mathf.Lerp(min,max,(float)random.NextDouble());

        static Mesh BuildMesh()
        {
            // Lathe from the primer to the recessed open mouth. All surfaces use
            // one palette and one material, including the dark inner wall.
            (float Radius,float Height,int Tone)[] profile=
            {
                (0,-.50f,3),(.15f,-.50f,1),(.47f,-.50f,1),(.50f,-.45f,1),(.50f,-.42f,2),
                (.39f,-.40f,2),(.39f,-.35f,1),(.44f,-.33f,0),(.44f,.26f,0),
                (.36f,.34f,0),(.36f,.47f,1),(.35f,.50f,1),(.26f,.50f,2),(.25f,.30f,2),(0,.28f,2)
            };
            const int sides=12;
            int quads=(profile.Length-1)*sides;
            var positions=new Vector3[quads*4];
            var normals=new Vector3[positions.Length];
            var uv=new Vector2[positions.Length];
            var indices=new int[quads*6];
            for(int band=0;band<profile.Length-1;band++)
            {
                var start=profile[band];
                var end=profile[band+1];
                float radial=end.Height-start.Height;
                float up=start.Radius-end.Radius;
                for(int side=0;side<sides;side++)
                {
                    int quad=band*sides+side;
                    int vertex=quad*4, triangle=quad*6;
                    float angle=side*(Mathf.PI*2/sides), nextAngle=(side+1)*(Mathf.PI*2/sides);
                    var a=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                    var b=new Vector3(Mathf.Cos(nextAngle),0,Mathf.Sin(nextAngle));
                    positions[vertex]=a*start.Radius+Vector3.up*start.Height;
                    positions[vertex+1]=a*end.Radius+Vector3.up*end.Height;
                    positions[vertex+2]=b*end.Radius+Vector3.up*end.Height;
                    positions[vertex+3]=b*start.Radius+Vector3.up*start.Height;
                    normals[vertex]=normals[vertex+1]=(a*radial+Vector3.up*up).normalized;
                    normals[vertex+2]=normals[vertex+3]=(b*radial+Vector3.up*up).normalized;
                    for(int corner=0;corner<4;corner++) uv[vertex+corner]=new Vector2((start.Tone+.5f)/4,.5f);
                    indices[triangle]=indices[triangle+3]=vertex;
                    indices[triangle+1]=vertex+1;
                    indices[triangle+2]=indices[triangle+4]=vertex+2;
                    indices[triangle+5]=vertex+3;
                }
            }
            var result=new Mesh {name="Rimmed spent casing",vertices=positions,normals=normals,uv=uv,triangles=indices};
            result.RecalculateBounds();
            return result;
        }

        public void Dispose()
        {
            Object.Destroy(root);
            Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(palette);
        }
    }
}
