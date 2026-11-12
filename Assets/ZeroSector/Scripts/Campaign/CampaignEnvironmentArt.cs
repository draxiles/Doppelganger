using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSector.Campaign
{
    // Presentation only: all new meshes are colliderless. Gameplay remains the source of truth.
    public sealed class CampaignEnvironmentArt : MonoBehaviour
    {
        static readonly Color Ice=new(.15f,.8f,1), Amber=new(1,.46f,.11f), Violet=new(.57f,.26f,1), Mint=new(.18f,1,.64f);
        static readonly int TintId=Shader.PropertyToID("_BaseColor");
        readonly List<Mesh> meshes=new();
        readonly List<TerminalDisplay> terminals=new();
        MaterialPropertyBlock block;
        Material surface,signal,letters;
        Font font;
        GameObject dressing;
        CampaignGame game;
        MeshRenderer circuitA,circuitB,heartMesh;
        Transform rotor,counterRotor;
        Light fill,heart;
        readonly List<Light> emergencyLamps=new();
        TextMesh headerStatus;
        float elapsed,nextStatus;
        int chapter;
        bool preview,ready,power,relayA,relayB;
        Color heartColor;

        sealed class TerminalDisplay
        {
            public CampaignInteractable target;
            public TextMesh status;
            public MeshRenderer halo;
            public Color color;
        }

        public void Initialize(CampaignGame owner,IEnumerable<CampaignInteractable> targets,bool forPreview=false)
        {
            if(ready || !owner) return;
            surface=Resources.Load<Material>("Doppelganger/ShipDisplay");
            signal=Resources.Load<Material>("Doppelganger/PulseTrail");
            var textShader=Shader.Find("Doppelganger/World Sign");
            font=Resources.Load<Font>("Doppelganger/Interface/GolosText-Bold");
            if(!font) font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if(!surface || !signal || !textShader || !font) return;
            block=new MaterialPropertyBlock();
            letters=new Material(textShader){ name="Ship display / runtime glyphs" };
            game=owner; chapter=owner.chapter; preview=forPreview;
            dressing=new GameObject("05 / Reactive ship displays and route lights");
            dressing.transform.SetParent(transform,false);
            TuneLighting();
            if(chapter==0) Bridge();
            else if(chapter==1) Engineering();
            else Core();
            foreach(var item in targets) if(item && TerminalName(item.id)!=null) Terminal(item);
            ready=true; Refresh(); Animate(0);
        }

        void TuneLighting()
        {
            var lightRoot=Array.Find(game.gameObject.scene.GetRootGameObjects(),go=>go.name=="03 / Authored lighting");
            if(!lightRoot) return;
            foreach(var lamp in lightRoot.GetComponentsInChildren<Light>())
            {
                if(lamp.name=="Soft ship interior bounce")
                {
                    fill=lamp; fill.color=chapter switch { 0=>new Color(.74f,.86f,1),1=>new Color(1,.87f,.73f),_=>new Color(.87f,.79f,1) };
                    fill.intensity=chapter==0 ? .75f : .68f;
                }
                else if(lamp.name=="Working deck pool")
                {
                    lamp.color=chapter switch
                    {
                        0=>new Color(.76f,.87f,1),1=>lamp.transform.position.x>0 ? new Color(.63f,.85f,1) : new Color(1,.82f,.63f),
                        _=>new Color(.77f,.73f,1)
                    };
                }
                else if(lamp.name=="Hull accent" && chapter==1)
                    lamp.color=lamp.transform.position.x>0 ? new Color(.05f,.55f,.95f) : new Color(1,.33f,.08f);
                else if(lamp.name=="Reactor heart / violet") heart=lamp;
            }
        }

        void Bridge()
        {
            foreach(float x in new[]{-6.5f,6.5f})
            {
                var light=new GameObject("Emergency wash / bulkhead",typeof(Light)).GetComponent<Light>();
                light.transform.SetParent(dressing.transform,false);
                light.transform.position=new Vector3(x,2.55f,-8);
                light.type=LightType.Point; light.range=5.4f; light.color=new Color(1,.24f,.075f);
                light.shadows=LightShadows.None;
                emergencyLamps.Add(light);
            }
            headerStatus=Plate("01  /  КОМАНДНЫЙ МОСТИК","ОРБИТАЛЬНАЯ ВАХТА",
                new Vector3(0,3.65f,12.8f),0,5.4f,1.03f,Ice);
            var runway=new Shape();
            foreach(float side in new[]{-1f,1f})
                for(int i=0;i<13;i++)
                    runway.Line(new Vector3(side*2.15f,.028f,-12.2f+i*1.63f),new Vector3(side*2.15f,.028f,-11.13f+i*1.63f),Vector3.up,.045f,new Color(.12f,.68f,.96f,.65f));
            circuitA=Draw("Flight-deck guide lights",runway,signal);

            var baseShape=new Shape();
            baseShape.Arc(new Vector3(0,1.32f,1),Vector3.right,Vector3.forward,.79f,0,360,64,.035f,Ice);
            for(int i=0;i<8;i++) baseShape.Arc(new Vector3(0,1.34f,1),Vector3.right,Vector3.forward,.96f,i*45,26,6,.035f,new Color(.1f,.58f,1,.62f));
            Draw("Hologram projector rim",baseShape,signal);
            rotor=Pivot("Orbital plot / rotating globe",new Vector3(0,1.91f,1));
            var globe=new Shape();
            var wire=new Color(.16f,.66f,1,.64f);
            for(int i=0;i<4;i++)
            {
                var axis=Quaternion.Euler(0,i*45,0)*Vector3.right;
                globe.Arc(Vector3.zero,axis,Vector3.up,.43f,0,360,48,.014f,wire);
            }
            for(int i=-2;i<=2;i++)
                globe.Arc(Vector3.up*(i*.145f),Vector3.right,Vector3.forward,Mathf.Sqrt(.43f*.43f-i*i*.145f*.145f),0,360,40,.011f,wire);
            globe.Arc(Vector3.zero,Vector3.right,new Vector3(0,.38f,.93f).normalized,.76f,0,360,56,.024f,new Color(.24f,.85f,1,.82f));
            globe.Arc(Vector3.zero,Vector3.right,new Vector3(0,.38f,.93f).normalized,.83f,24,75,20,.015f,Ice);
            heartMesh=Draw("Orbital map / meridians and transfer path",globe,signal,rotor);
        }

        void Engineering()
        {
            Plate("02  /  СИЛОВАЯ ПАЛУБА","ДВА НЕЗАВИСИМЫХ КОНТУРА",new Vector3(0,3.34f,-8.8f),0,4.15f,.86f,Amber);
            Plate("A  /  ЗАПАД","СИЛОВОЕ РЕЛЕ",new Vector3(-8,3.33f,-7.8f),0,3.05f,.87f,Amber);
            Plate("B  /  ВОСТОК","СИЛОВОЕ РЕЛЕ",new Vector3(8,3.33f,-7.8f),0,3.05f,.87f,Ice);
            foreach(float side in new[]{-1f,1f})
            {
                var circuit=new Shape();
                var tint=side<0 ? Amber : Ice;
                for(int i=0;i<9;i++)
                    circuit.Line(new Vector3(side*1.58f,.047f,-20.1f+i*1.24f),new Vector3(side*1.58f,.047f,-19.27f+i*1.24f),Vector3.up,.066f,tint);
                for(int i=0;i<10;i++)
                    circuit.Line(new Vector3(side*(1.62f+i),.047f,-8.96f),new Vector3(side*(2.28f+i),.047f,-8.96f),Vector3.up,.065f,tint);
                for(int i=0;i<9;i++)
                    circuit.Line(new Vector3(side*11.6f,.047f,-8.92f+i*1.21f),new Vector3(side*11.6f,.047f,-8.16f+i*1.21f),Vector3.up,.063f,tint);
                // Overhead insulated bus bars above the generator rows.
                for(int i=0;i<7;i++)
                    circuit.Line(new Vector3(side*4.78f,4.2f,-6.7f+i*1.9f),new Vector3(side*4.78f,4.2f,-5.15f+i*1.9f),Vector3.right,.09f,tint);
                var mesh=Draw(side<0 ? "West power bus" : "East power bus",circuit,signal);
                if(side<0) circuitA=mesh; else circuitB=mesh;
            }
            var threshold=new Shape();
            for(int i=0;i<15;i++)
            {
                float x=-2.34f+i*.31f;
                threshold.Quad(new Vector3(x,.044f,-9.49f),new Vector3(x+.14f,.044f,-9.49f),new Vector3(x+.37f,.044f,-9.1f),new Vector3(x+.23f,.044f,-9.1f),new Color(.93f,.55f,.11f,.9f));
            }
            Draw("Hazard hatch threshold",threshold,signal);
        }

        void Core()
        {
            headerStatus=Plate("03  /  НЕЙРОННОЕ ЯДРО","КОНТУРЫ СДЕРЖИВАНИЯ АКТИВНЫ",new Vector3(0,4.9f,8.45f),0,5.3f,1.06f,Violet);
            rotor=Pivot("Reactor / counter-rotating rings",new Vector3(0,4.22f,2));
            counterRotor=Pivot("Reactor / suspended neural lattice",new Vector3(0,4.63f,2));
            var rings=new Shape();
            var wire=new Color(.8f,.78f,1,.83f);
            for(int band=0;band<3;band++)
            {
                Vector3 p=Vector3.up*(band*.5f-.55f);
                float radius=2.58f-band*.36f;
                rings.Arc(p,Vector3.right,Vector3.forward,radius,0,360,88,.045f,wire);
                for(int i=0;i<8;i++)
                    rings.Arc(p+Vector3.up*.035f,Vector3.right,Vector3.forward,radius+.13f,i*45+band*15,24,8,.058f,new Color(.9f,.9f,1,.66f));
            }
            heartMesh=Draw("Active containment bands",rings,signal,rotor);
            var lattice=new Shape();
            for(int i=0;i<6;i++)
            {
                Vector3 a=Quaternion.Euler(0,i*60,0)*Vector3.right*1.16f;
                Vector3 b=Quaternion.Euler(0,(i+1)*60,0)*Vector3.right*1.16f;
                lattice.Line(a,b,Vector3.up,.045f,wire);
                lattice.Line(a,Vector3.up*1.18f,Vector3.Cross(a,Vector3.up),.055f,wire);
                lattice.Line(a,Vector3.down*1.1f,Vector3.Cross(a,Vector3.up),.043f,wire);
                lattice.Line(a+Vector3.up*.13f,b+Vector3.down*.13f,Vector3.up,.022f,new Color(.6f,.5f,.95f,.55f));
            }
            Draw("Neural lattice / links",lattice,signal,counterRotor);
            for(int side=-1;side<=1;side+=2)
            {
                var channel=new Shape();
                for(int i=0;i<9;i++)
                    channel.Line(new Vector3(side*(3.25f+i*.92f),.041f,1.97f),new Vector3(side*(3.96f+i*.92f),.041f,1.97f),Vector3.up,.07f,Ice);
                var mesh=Draw(side<0 ? "Cooling A conduit" : "Cooling B conduit",channel,signal);
                if(side<0) circuitA=mesh; else circuitB=mesh;
            }
            heartColor=Violet;
        }

        void Terminal(CampaignInteractable target)
        {
            Color accent=target.id=="relayA" || target.id=="power" ? Amber : target.id=="core" ? Violet : Ice;
            var face=target.transform.forward; face.y=0; face.Normalize();
            if(face.sqrMagnitude<.1f) face=Vector3.forward;
            var sign=Plate(TerminalName(target.id),"ОЖИДАНИЕ",target.FocusPoint+Vector3.up*.95f+face*.15f,
                Quaternion.LookRotation(-face).eulerAngles.y,1.62f,.47f,accent);
            var halo=new Shape();
            Vector3 floor=target.FocusPoint+face*.47f; floor.y=.035f;
            for(int i=0;i<6;i++) halo.Arc(floor,Vector3.right,Vector3.forward,.98f,i*60+8,38,10,.036f,new Color(1,1,1,.72f));
            terminals.Add(new TerminalDisplay{target=target,status=sign,halo=Draw("Terminal approach / "+target.id,halo,signal),color=accent});
        }

        static string TerminalName(string id) => id switch
        {
            "log"=>"ЖУРНАЛ", "power"=>"ПИТАНИЕ", "nav"=>"НАВИГАЦИЯ", "exit"=>"ШЛЮЗ",
            "relayA"=>"ЗАПАД / A", "relayB"=>"ВОСТОК / B", "coolantA"=>"ОХЛАЖДЕНИЕ / A", "coolantB"=>"ОХЛАЖДЕНИЕ / B", "core"=>"ЯДРО", _=>null
        };

        void LateUpdate()
        {
            if(!ready || preview || !game) return;
            if(Time.unscaledTime>=nextStatus) { nextStatus=Time.unscaledTime+.12f; Refresh(); }
            Animate(game.Paused ? 0 : Mathf.Min(Time.deltaTime,.06f));
        }

        void Refresh()
        {
            var focus=preview ? null : game.NextObjective();
            power=CampaignSession.Has("power");
            relayA=CampaignSession.Has(chapter==2 ? "coolantA" : "relayA");
            relayB=CampaignSession.Has(chapter==2 ? "coolantB" : "relayB");
            foreach(var terminal in terminals)
            {
                if(!terminal.target) { if(terminal.status) terminal.status.transform.parent.gameObject.SetActive(false); if(terminal.halo) terminal.halo.enabled=false; continue; }
                bool done=CampaignSession.Has(terminal.target.id), active=terminal.target==focus;
                string text=done ? "ВЫПОЛНЕНО" : active ? "АКТИВНАЯ ЦЕЛЬ" : "ОЖИДАНИЕ";
                if(terminal.status.text!=text) terminal.status.text=text;
                terminal.status.color=done ? Mint : active ? new Color(1,.84f,.42f) : new Color(.51f,.66f,.75f);
                SetTint(terminal.halo,done ? Mint*.35f : active ? Amber*1.55f : terminal.color*.23f);
            }
            if(chapter==0)
            {
                if(fill) fill.intensity=power ? .95f : .56f;
                SetTint(circuitA,Color.white*(power ? 1f : .48f));
                SetTint(heartMesh,Color.white*(power ? 1.35f : .53f));
                headerStatus.text=CampaignSession.Has("nav") ? "КУРС ПРОЛОЖЕН" : power ? "ПИТАНИЕ ВОССТАНОВЛЕНО" : "АВАРИЙНОЕ ПИТАНИЕ";
                headerStatus.color=power ? Mint : Amber;
            }
            else if(chapter==1)
            {
                SetTint(circuitA,relayA ? Mint*1.3f : Color.white*.75f);
                SetTint(circuitB,relayB ? Mint*1.3f : Color.white*.75f);
            }
            else
            {
                bool online=!CampaignSession.Has("core"), exposed=relayA&&relayB;
                heartColor=!online ? Mint*.58f : exposed ? Amber : Violet;
                SetTint(circuitA,relayA ? Color.white*.13f : Color.white*1.23f);
                SetTint(circuitB,relayB ? Color.white*.13f : Color.white*1.23f);
                headerStatus.text=!online ? "ПЕРЕДАЧА ПРЕКРАЩЕНА" : exposed ? "ЗАЩИТНОЕ ПОЛЕ СНЯТО" : "КОНТУРЫ СДЕРЖИВАНИЯ АКТИВНЫ";
                headerStatus.color=heartColor;
                if(heart) heart.color=heartColor;
            }
        }

        void Animate(float delta)
        {
            if(delta>0) elapsed=(elapsed+delta)%3600f;
            for(int i=0;i<emergencyLamps.Count;i++)
            {
                float pulse=.72f+.28f*Mathf.Sin(elapsed*2.9f+i*1.8f);
                emergencyLamps[i].intensity=power ? .10f : 1.85f*pulse;
            }
            if(rotor) rotor.localRotation=Quaternion.Euler(0,elapsed*(chapter==0 ? power ? 20 : 6 : 13),0);
            if(counterRotor) counterRotor.localRotation=Quaternion.Euler(0,-elapsed*20,elapsed*7);
            if(chapter==2)
            {
                var pulse=.92f+.16f*Mathf.Sin(elapsed*2.1f);
                SetTint(heartMesh,heartColor*(1.55f*pulse));
                if(heart) heart.intensity=(relayA&&relayB ? 6.7f : 8.2f)*pulse;
            }
        }

        void SetTint(Renderer renderer,Color tint)
        {
            if(!renderer) return;
            block.SetColor(TintId,new Color(tint.r,tint.g,tint.b,1)); renderer.SetPropertyBlock(block);
        }

        TextMesh Plate(string title,string subtitle,Vector3 center,float yaw,float width,float height,Color accent)
        {
            var pivot=Pivot(title,center); pivot.rotation=Quaternion.Euler(0,yaw,0);
            float x=width*.5f,y=height*.5f,n=Mathf.Min(.13f,height*.23f);
            var plate=new Shape();
            plate.Quad(new(-x+n,-y,0),new(x-n,-y,0),new(x-n,y,0),new(-x+n,y,0),new Color(.012f,.024f,.041f));
            plate.Quad(new(-x+n,-y,0),new(-x,-y+n,0),new(-x,y-n,0),new(-x+n,y,0),new Color(.023f,.041f,.062f));
            plate.Quad(new(x-n,-y,0),new(x,-y+n,0),new(x,y-n,0),new(x-n,y,0),new Color(.023f,.041f,.062f));
            plate.Line(new(-x+n,y,-.008f),new(x-n,y,-.008f),Vector3.forward,.022f,accent);
            plate.Line(new(-x+n,-y,-.008f),new(x-n,-y,-.008f),Vector3.forward,.018f,accent*.49f);
            for(int i=0;i<4;i++)
                plate.Line(new(-x+n+i*.075f,-y+.058f,-.009f),new(-x+n+.04f+i*.075f,-y+.094f,-.009f),Vector3.forward,.021f,accent);
            Draw("Inset frame",plate,surface,pivot);
            Glyph(pivot,"Header",title,height*.10f,Mathf.Min(height*.3f,width/Mathf.Max(1,title.Length)*1.32f),new Color(.87f,.94f,1));
            return Glyph(pivot,"State",subtitle,-height*.25f,Mathf.Min(height*.19f,width/Mathf.Max(12,subtitle.Length)*1.25f),accent);
        }

        TextMesh Glyph(Transform parent,string name,string content,float y,float height,Color color)
        {
            var go=new GameObject(name,typeof(TextMesh));
            go.transform.SetParent(parent,false); go.transform.localPosition=new Vector3(0,y,-.014f);
            var label=go.GetComponent<TextMesh>();
            label.text=content; label.font=font; label.fontSize=64; label.characterSize=height*.15625f;
            label.anchor=TextAnchor.MiddleCenter; label.alignment=TextAlignment.Center; label.color=color;
            var renderer=go.GetComponent<MeshRenderer>(); renderer.sharedMaterial=letters;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            go.AddComponent<CampaignWorldSign>();
            return label;
        }

        Transform Pivot(string title,Vector3 point)
        {
            var obj=new GameObject(title); obj.transform.SetParent(dressing.transform,false); obj.transform.position=point;
            return obj.transform;
        }

        MeshRenderer Draw(string title,Shape shape,Material material,Transform parent=null)
        {
            var go=new GameObject(title,typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(parent ? parent : dressing.transform,false);
            var mesh=new Mesh { name=title };
            mesh.SetVertices(shape.points); mesh.SetColors(shape.colors); mesh.SetTriangles(shape.indices,0);
            mesh.RecalculateBounds(); mesh.UploadMeshData(true); meshes.Add(mesh);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            return renderer;
        }

        void OnDestroy()
        {
            foreach(var mesh in meshes) Release(mesh);
            Release(letters); Release(dressing);
        }
        static void Release(UnityEngine.Object item)
        { if(item) { if(Application.isPlaying) Destroy(item); else DestroyImmediate(item); } }

        sealed class Shape
        {
            public readonly List<Vector3> points=new();
            public readonly List<Color> colors=new();
            public readonly List<int> indices=new();
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
            {
                int k=points.Count; points.Add(a); points.Add(b); points.Add(c); points.Add(d);
                for(int i=0;i<4;i++) colors.Add(color);
                indices.Add(k); indices.Add(k+1); indices.Add(k+2); indices.Add(k); indices.Add(k+2); indices.Add(k+3);
            }
            public void Line(Vector3 a,Vector3 b,Vector3 normal,float width,Color color)
            {
                var offset=Vector3.Cross(b-a,normal).normalized*(width*.5f);
                Quad(a-offset,a+offset,b+offset,b-offset,color);
            }
            public void Arc(Vector3 center,Vector3 u,Vector3 v,float radius,float start,float sweep,int count,float width,Color color)
            {
                var normal=Vector3.Cross(u,v); Vector3 At(float t)=>center+(u*Mathf.Cos(t)+v*Mathf.Sin(t))*radius;
                var prior=At(start*Mathf.Deg2Rad);
                for(int i=1;i<=count;i++)
                {
                    var next=At((start+sweep*i/count)*Mathf.Deg2Rad);
                    Line(prior,next,normal,width,color); prior=next;
                }
            }
        }
    }
}
