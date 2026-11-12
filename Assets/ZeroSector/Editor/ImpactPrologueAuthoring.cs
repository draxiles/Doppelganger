using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;
using Object=UnityEngine.Object;

namespace ZeroSector.Editor
{
    /// <summary>Assembles the orbital transport and its cutscene as an isolated resource prefab.</summary>
    public static class ImpactPrologueAuthoring
    {
        const string Output="Assets/DoppelgangerGenerated/Prologue";
        const string Kit="Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Prefabs/";
        const string Earth="Assets/DoppelgangerGenerated/Earth/";
        const string KeyArtPath="Assets/Resources/Doppelganger/Interface/SignalKeyArt.png";
        const int OrbitLayer=31;
        public const string PrefabPath="Assets/Resources/Doppelganger/ImpactPrologue.prefab";
        const string SoundPath=Output+"/Orbital collision.wav";

        enum Finish { Alloy, Bone, Underbody, Graphite, Amber, Glass, Rail, Running, Exhaust, Scar, Raider, RaiderTrim, Ember, Marker, RaiderLamp }
        const int PaletteSize=15;

        public static void Prepare()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Author the orbital cutscene in Edit Mode.");
            Directory.CreateDirectory(Output+"/Materials");
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            if(!AssetDatabase.LoadAssetAtPath<Mesh>(Earth+"EarthSphere.asset")) EarthAuthoring.Prepare();
            WriteSound();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var previous=SceneManager.GetActiveScene();
            var scratch=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scratch);
            try
            {
                var materials=Palette();
                var root=new GameObject("PROLOGUE / Orbital collision");
                var orbit=Group(root.transform,"Low Earth orbit",Vector3.zero);
                var ship=Group(orbit,"SIGNAL / orbital transport",Vector3.zero);
                var keyArt=AssetDatabase.LoadAssetAtPath<Texture2D>(KeyArtPath);
                if(!keyArt) throw new FileNotFoundException("SIGNAL opening key art is missing.",KeyArtPath);
                var lens=Group(root.transform,"Orbital tracking camera",Vector3.zero).gameObject.AddComponent<Camera>();
                lens.enabled=false;
                lens.depth=5;
                lens.allowHDR=true;
                lens.allowMSAA=false;
                lens.cullingMask=1<<OrbitLayer;
                var lensData=lens.GetUniversalAdditionalCameraData();
                lensData.renderPostProcessing=false;
                lensData.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                lensData.antialiasingQuality=AntialiasingQuality.High;
                var listener=lens.gameObject.AddComponent<AudioListener>();
                listener.enabled=false;
                var sound=root.AddComponent<AudioSource>();
                sound.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(SoundPath);
                sound.playOnAwake=false;
                sound.spatialBlend=0;
                sound.priority=18;

                BuildShip(ship,HeroPalette(materials),out var engines,out var breach);
                var globe=BuildBackdrop(orbit);
                SignalOrbitReflection.Install(orbit);
                var attacker=BuildAttacker(orbit,materials);
                var boardingDeck=BuildBoardingDeck(root.transform,materials,out var boarders,out var boardingAlarm);
                var scrap=new Geometry("Displaced hull plating");
                scrap.Hull(Vector3.zero,new[]{new Rib(-1.7f,.84f,.17f,.12f),new Rib(-.52f,1.07f,.29f,-.06f),
                    new Rib(.82f,.71f,.12f,.17f),new Rib(1.3f,.24f,.07f,.28f)},.81f,Finish.Alloy,Finish.Ember,Finish.Scar);
                var scrapMesh=Store("Hull strike shrapnel",scrap.ToMesh());
                var pieces=new Transform[20];
                for(int i=0;i<pieces.Length;i++)
                {
                    pieces[i]=Draw(orbit,"Torn hull panel "+i,scrapMesh,materials,Vector3.zero).transform;
                    pieces[i].localScale=Vector3.one*(.055f+(i%6)*.048f);
                    pieces[i].gameObject.SetActive(false);
                }
                var falloff=FalloffTexture();
                var flashMaterial=Plasma("Impact glow",new Color(2.6f,.66f,.13f),falloff);
                var flashDisc=Draw(orbit,"Impact envelope",Store("Impact glow billboard",GlowQuad()),new[]{flashMaterial},Vector3.zero).transform;
                flashDisc.gameObject.SetActive(false);
                var sparks=CreateSparks(orbit,falloff);
                var sunlight=AddLight(orbit,"Orbital sunlight",LightType.Directional,Vector3.zero,new Color(1,.86f,.69f),4.15f,0);
                sunlight.transform.rotation=Quaternion.LookRotation(-SignalOrbitReflection.SunDirection);
                sunlight.shadows=LightShadows.Soft;
                sunlight.shadowBias=.08f;
                sunlight.shadowNormalBias=.22f;
                var reflected=AddLight(orbit,"Earth bounce",LightType.Directional,Vector3.zero,new Color(.39f,.64f,1),.61f,0);
                reflected.transform.rotation=Quaternion.LookRotation(new Vector3(-.38f,.88f,-.28f));
                var rim=AddLight(orbit,"Orbital cool rim",LightType.Directional,Vector3.zero,new Color(.58f,.76f,1),.39f,0);
                rim.transform.rotation=Quaternion.LookRotation(new Vector3(.69f,-.15f,.71f));
                foreach(var light in new[]{sunlight,reflected,rim}) light.cullingMask=1<<OrbitLayer;
                var flash=AddLight(orbit,"Collision light",LightType.Point,Vector3.zero,new Color(1,.38f,.09f),0,32);
                flash.cullingMask=1<<OrbitLayer;
                flash.enabled=false;
                var emergency=AddLight(root.transform,"Bridge alarm wash",LightType.Point,Vector3.zero,new Color(1,.055f,.023f),0,12);
                emergency.enabled=false;

                foreach(var item in orbit.GetComponentsInChildren<Transform>(true)) item.gameObject.layer=OrbitLayer;
                foreach(var item in boardingDeck.GetComponentsInChildren<Transform>(true)) item.gameObject.layer=OrbitLayer;
                var timeline=new SerializedObject(root.AddComponent<CampaignPrologue>());
                Assign(timeline,"lens",lens);
                Assign(timeline,"listener",listener);
                Assign(timeline,"soundtrack",sound);
                Assign(timeline,"keyArt",keyArt);
                Assign(timeline,"orbit",orbit);
                Assign(timeline,"globe",globe);
                Assign(timeline,"orbitalSun",sunlight);
                Assign(timeline,"ship",ship);
                Assign(timeline,"attacker",attacker);
                Assign(timeline,"boardingDeck",boardingDeck);
                Assign(timeline,"boardingAlarm",boardingAlarm);
                Assign(timeline,"impactGlow",flashDisc);
                Assign(timeline,"breach",breach);
                Assign(timeline,"sparks",sparks);
                Assign(timeline,"flash",flash);
                Assign(timeline,"emergency",emergency);
                AssignArray(timeline,"hotPieces",pieces);
                AssignArray(timeline,"engines",engines);
                AssignArray(timeline,"boarders",boarders);
                timeline.ApplyModifiedPropertiesWithoutUndo();

                if(!sound.clip || sound.clip.length<CampaignPrologue.Duration-.05f)
                    throw new InvalidOperationException("The impact soundtrack must cover the complete cinematic.");
                if(ship.GetComponentsInChildren<MeshRenderer>(true).Length<12 || boarders.Length<2)
                    throw new InvalidOperationException("Orbital ship detail was not authored.");
                if(!PrefabUtility.SaveAsPrefabAsset(root,PrefabPath)) throw new IOException("Could not save the prologue prefab.");
                AssetDatabase.SaveAssets();
                Debug.Log("Impact prologue: SIGNAL key art, two animated spacecraft, ramming, breaching and bridge blackout ready.");
            }
            finally
            {
                if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scratch,true);
            }
        }

        static void BuildShip(Transform parent,Material[] materials,out Transform[] plumes,out Transform breach)
        {
            var structure=new Geometry("SIGNAL 06 / layered armored pressure hull");
            structure.Hull(Vector3.zero,new[]{
                new Rib(-29.2f,4.3f,2.32f,.48f),new Rib(-24.4f,6.4f,3.36f,.42f),new Rib(-17,7.45f,3.28f,.42f),
                new Rib(-6.3f,7.38f,3.24f,.37f),new Rib(5.9f,7.62f,3.36f,.47f),new Rib(12.5f,7.32f,2.88f,.66f),
                new Rib(19.5f,6.58f,2.42f,.80f),new Rib(25,5.55f,1.94f,.94f),new Rib(30.2f,4.36f,1.41f,1.00f),
                new Rib(33.45f,3.35f,.90f,1.13f),new Rib(34.45f,2.77f,.65f,1.16f)},
                .64f,Finish.Alloy,Finish.Bone,Finish.Underbody);
            structure.Hull(new Vector3(0,-2.44f,-.4f),new[]{
                new Rib(-22,2.55f,.63f,.46f),new Rib(-17,4.1f,1.2f,.53f),new Rib(-5,4.64f,1.44f,.31f),
                new Rib(7,4.76f,1.44f,.18f),new Rib(17,3.83f,1.10f,.25f),new Rib(25,2.05f,.53f,.38f)},
                .73f,Finish.Underbody,Finish.Graphite,Finish.Alloy);
            structure.Hull(Vector3.up*2.7f,new[]{
                new Rib(-21,1.61f,.41f,.28f),new Rib(-15,2.76f,.82f,.63f),new Rib(-8,3.4f,1.1f,.80f),
                new Rib(1,3.77f,.97f,1.30f),new Rib(10,3.51f,.83f,1.04f),new Rib(18,2.52f,.74f,.75f),
                new Rib(24.4f,2.08f,.37f,.52f),new Rib(31,1.24f,.23f,.08f)},
                .74f,Finish.Alloy,Finish.Graphite,Finish.Underbody);

            for(int side=-1;side<=1;side+=2)
            {
                // A load-bearing saddle replaces the old shelf wings. The cutter still hits the port at +X.
                structure.Hull(new Vector3(side*9.05f,-.23f,-11.8f),new[]{
                    new Rib(-16,1.29f,1.02f,.05f),new Rib(-11,2.3f,1.89f,.05f),new Rib(-4,2.62f,2.11f,.18f),
                    new Rib(3,2.73f,2.23f,.21f),new Rib(9,2.28f,1.83f,.32f),new Rib(13.6f,1.71f,1.42f,.54f),
                    new Rib(16.4f,.89f,.72f,.43f)},.61f,Finish.Alloy,Finish.Bone,Finish.Graphite);
                for(int pod=0;pod<2;pod++)
                {
                    Vector3 mount=DrivePod(side,pod);
                    BuildSignalNacelle(structure,mount);
                    float height=pod==0 ? 1 : -1;
                    for(int strut=0;strut<3;strut++)
                    {
                        float z=-23.8f+strut*5.6f;
                        var inboard=new Vector3(side*8.5f,height*1.67f,z+1.24f);
                        var outboard=new Vector3(side*10.58f,height*3.23f,z-.6f);
                        structure.Beam(inboard,outboard,1.05f,.87f,Finish.Underbody);
                        structure.Beam(inboard+Vector3.right*side*.53f,outboard+Vector3.right*side*.53f,.39f,.41f,Finish.Bone);
                        structure.Beam(inboard+Vector3.forward*.77f,outboard+Vector3.forward*.77f,.33f,.45f,Finish.Graphite);
                    }
                }

                BuildHeatExchanger(structure,side,1);
                BuildHeatExchanger(structure,side,-1);
                BuildSignalFlank(structure,side);
                BuildSignalBridgeCheek(structure,side);
            }

            BuildSignalSuperstructure(structure);
            Draw(parent,"Armored hull, recessed heat exchangers and ring nacelles",
                Store("Signal transport geometry",structure.ToMesh()),materials,Vector3.zero);

            Paint(parent,"SIGNAL  /  06",new Vector3(-.2f,4.89f,7.8f),Quaternion.Euler(90,0,0),.072f,new Color(.55f,.62f,.63f));
            Paint(parent,"06",new Vector3(7.82f,1.43f,7.66f),Quaternion.Euler(0,-90,0),.057f,new Color(.60f,.65f,.65f));

            var lens=FalloffTexture();
            var cyan=Plasma("Thruster plasma",new Color(.17f,.48f,.86f),lens);
            var inner=Plasma("Thruster core",new Color(.45f,.76f,1.0f),lens);
            var plumeMesh=Store("Thruster stream",PlumeMesh(1.50f,2.46f));
            var coreMesh=Store("Thruster core geometry",PlumeMesh(.93f,1.43f));
            plumes=new Transform[4];
            for(int i=0;i<plumes.Length;i++)
            {
                var mount=Group(parent,"Thruster exhaust "+i,DrivePod(i<2 ? -1 : 1,i%2));
                Draw(mount,"Outer ion plume",plumeMesh,new[]{cyan},Vector3.zero);
                Draw(mount,"Inner ion stream",coreMesh,new[]{inner},Vector3.forward*.22f);
                plumes[i]=mount;
            }
            var broken=new Geometry("Rupture");
            broken.Box(new Vector3(.44f,0,-.18f),new Vector3(2.7f,.21f,3.75f),Finish.Scar,Quaternion.Euler(9,18,-13));
            broken.Box(new Vector3(1.23f,.12f,-.72f),new Vector3(1.25f,.16f,2.89f),Finish.Ember,Quaternion.Euler(0,22,-25));
            broken.Box(new Vector3(-1.12f,.05f,1.10f),new Vector3(.5f,.15f,1.95f),Finish.Amber,Quaternion.Euler(-8,-24,22));
            breach=Draw(parent,"Torn impact plating",Store("Torn hull plate geometry",broken.ToMesh()),materials,new Vector3(11.7f,1.9f,-9.9f)).transform;
            breach.gameObject.SetActive(false);
        }

        static Vector3 DrivePod(int side,int bank) => bank==0
            ? new Vector3(side*10.63f,4.42f,-30.0f)
            : new Vector3(side*11.14f,-4.38f,-31.75f);


        static void BuildSignalNacelle(Geometry hull,Vector3 mount)
        {
            hull.Tube(mount,new[]{
                (-.32f,2.21f),(.17f,2.63f),(1.53f,3.04f),(4.18f,3.32f),(12.84f,3.36f),
                (15.12f,3.26f),(17.45f,3.06f),(18.93f,2.88f),(19.48f,2.37f)},32,Finish.Alloy,true);
            hull.Tube(mount,new[]{(-.34f,2.23f),(-.21f,1.86f),(.68f,1.52f),(2.35f,1.46f)},
                32,Finish.Graphite,false);
            hull.Disc(mount+Vector3.forward*2.31f,1.52f,32,Finish.Marker,false);
            hull.Tube(mount,new[]{(18.87f,2.91f),(19.48f,2.4f),(19.48f,1.92f),(18.6f,1.8f)},
                24,Finish.Underbody,false);
            hull.Disc(mount+Vector3.forward*18.57f,1.86f,24,Finish.Graphite,true);

            foreach(var belt in new[]{(1.65f,3.15f,.31f,Finish.Graphite),(3.66f,3.49f,.67f,Finish.Amber),
                (5.19f,3.37f,.20f,Finish.Underbody),(13.15f,3.41f,.29f,Finish.Bone),
                (16.16f,3.26f,.49f,Finish.Graphite),(17.94f,3.02f,.24f,Finish.Rail)})
            {
                var (z,r,width,finish)=belt;
                hull.Tube(mount,new[]{(z-width,r-.12f),(z-width*.74f,r),(z+width*.74f,r),(z+width,r-.10f)},
                    32,finish,true);
            }

            for(int rib=0;rib<16;rib++)
            {
                var angle=Quaternion.Euler(0,0,rib*22.5f);
                Vector3 up=angle*Vector3.up;
                Vector3 a=mount+up*3.355f+Vector3.forward*(7.14f+(rib%2)*.19f);
                Vector3 b=mount+up*3.375f+Vector3.forward*(10.63f-(rib%3)*.13f);
                hull.Box(a,new Vector3(1.04f,.13f,3.53f),rib%4==0 ? Finish.Bone : Finish.Alloy,angle);
                hull.Box(b,new Vector3(.95f,.10f,2.88f),rib%4==2 ? Finish.Graphite : Finish.Alloy,angle);
                hull.Box(b+up*.05f+angle*Vector3.right*.37f,new Vector3(.084f,.07f,2.37f),Finish.Underbody,angle);
                hull.Box(mount+up*3.12f+Vector3.forward*15.53f,new Vector3(.67f,.25f,2.36f),Finish.Bone,angle);
                hull.Box(mount+up*2.98f+Vector3.forward*17.78f,new Vector3(.48f,.19f,.93f),Finish.Underbody,angle);
                hull.Box(mount+up*2.09f+Vector3.forward*19.36f,new Vector3(.21f,.54f,.25f),Finish.Rail,angle);
                if(rib%4!=1) continue;
                hull.Box(mount+up*1.45f+Vector3.forward*18.84f,new Vector3(.45f,1.43f,.37f),Finish.Graphite,angle);
                for(int lamp=0;lamp<4;lamp++)
                    hull.Box(mount+up*(1.01f+lamp*.27f)+Vector3.forward*19.06f,
                        new Vector3(.23f,.17f,.09f),Finish.Marker,angle);
            }
            // Pale ridges, cast shoulders, and the dark bell lip remain visible at grazing incidence.
            for(int dog=0;dog<12;dog++)
            {
                var angle=Quaternion.Euler(0,0,15+dog*30);
                hull.Box(mount+angle*new Vector3(0,2.33f,.27f),new Vector3(.29f,.39f,1.0f),Finish.Rail,angle);
            }
        }

        static void BuildHeatExchanger(Geometry hull,int side,int tier)
        {
            // Slats are inclined on the lateral surface. Their tips and the frame sit away
            // from the collector so grazing light reveals actual depth through the gaps.
            Vector3 outwards=Vector3.right*side;
            Vector3 At(float y,float z,float lip=0) =>
                new(side*(10.65f-Mathf.Abs(y)*.12f+lip),y,z);
            float inner=tier>0 ? 2.88f : -2.46f;
            float outer=tier>0 ? 6.78f : -6.17f;
            float rear=tier>0 ? -12.15f : -11.60f;
            float tip=tier>0 ? 3.04f : 2.1f;
            float rake=tier>0 ? 2.8f : 1.65f;
            Vector3 A=At(inner,rear), B=At(inner,tip), C=At(outer,tip+rake), D=At(outer,rear+rake);
            Vector3 housing=(A+B+C+D)*.25f;
            Vector3 vertical=new(-side*tier*.12f,1,0);
            var corners=new[]{A,B,C,D};
            var profile=corners.Select(corner=>new Vector2(corner.z-housing.z,corner.y-housing.y)).ToArray();
            hull.Panel(housing-outwards*.79f,outwards,Vector3.forward,vertical,profile,.31f,Finish.Graphite,Finish.Underbody);
            hull.Beam(A,B,.64f,.53f,Finish.Bone);
            hull.Beam(B,C,.72f,.67f,Finish.Bone);
            hull.Beam(C,D,.67f,.58f,Finish.Bone);
            hull.Beam(D,A,.70f,.56f,Finish.Rail);
            hull.Beam(A-outwards*.64f,B-outwards*.64f,.41f,.52f,Finish.Underbody);
            hull.Beam(D-outwards*.59f,C-outwards*.59f,.40f,.44f,Finish.Graphite);
            hull.Beam(A-Vector3.up*tier*.32f,B-Vector3.up*tier*.32f,.43f,.53f,Finish.Graphite);
            for(int index=0;index<17;index++)
            {
                float z=Mathf.Lerp(rear+.55f,tip-.54f,index/16f);
                Vector3 toe=At(inner+tier*.26f,z,.10f);
                Vector3 head=At(outer-tier*.19f,z+rake,.10f);
                hull.Beam(toe,head,.42f,.42f,index%6==0 ? Finish.Rail : Finish.Underbody);
                hull.Beam(toe+outwards*.23f,head+outwards*.23f,.16f,.18f,Finish.Alloy);
                if(index%2==0) hull.Box(toe+Vector3.up*tier*.1f,new Vector3(.28f,.19f,.29f),Finish.Marker);
            }
            foreach(float z in new[]{rear+.26f,tip-.25f})
            {
                hull.Beam(At(inner,z,-.30f),new Vector3(side*6.5f,tier*1.8f,z+.92f),.52f,.64f,Finish.Graphite);
                hull.Box(At(outer,z+rake,.12f),new Vector3(.4f,.31f,.83f),Finish.Bone);
            }
        }

        static void BuildSignalFlank(Geometry hull,int side)
        {
            Vector3 outward=Vector3.right*side;
            var engineDoor=new[]{
                new Vector2(-3.56f,-.96f),new Vector2(-3.85f,-.46f),new Vector2(-3.85f,.62f),
                new Vector2(-3.39f,1.15f),new Vector2(3.54f,1.15f),new Vector2(3.74f,.70f),
                new Vector2(3.36f,-.97f)
            };
            Vector3 center=new(side*11.94f,.47f,-10.24f);
            hull.Panel(center,outward,Vector3.forward,Vector3.up,engineDoor,.37f,Finish.Graphite,Finish.Rail);
            hull.Panel(center+outward*.24f+Vector3.back*.72f,outward,Vector3.forward,Vector3.up,
                ChamferedPlate(2.24f,.79f,.26f),.19f,Finish.Underbody,Finish.Bone);
            for(int column=0;column<4;column++)
            {
                float z=-12.68f+column*1.13f;
                Vector3 sill=new(side*12.40f,.47f,z);
                hull.Box(sill,new Vector3(.12f,1.14f,.82f),Finish.Alloy);
                hull.Box(sill+Vector3.up*.49f+outward*.082f,new Vector3(.073f,.11f,.56f),Finish.Rail);
                hull.Box(sill+Vector3.down*.42f+outward*.075f,new Vector3(.065f,.09f,.49f),Finish.Graphite);
            }
            for(int lamp=0;lamp<5;lamp++)
                hull.Box(new Vector3(side*12.17f,1.76f,-12.82f+lamp*1.31f),
                    new Vector3(.27f,.15f,.48f),Finish.Marker);
            hull.Beam(new Vector3(side*11.65f,-.73f,-19.88f),new Vector3(side*12.32f,-.73f,-14.23f),
                .58f,.40f,Finish.Graphite);
            hull.Beam(new Vector3(side*11.55f,.08f,-19.27f),new Vector3(side*12.24f,.08f,-14.46f),
                .51f,.34f,Finish.Underbody);
            for(int clip=0;clip<5;clip++)
            {
                float z=-19.1f+clip*1.05f;
                hull.Box(new Vector3(side*Mathf.Lerp(11.73f,12.30f,clip/4f),-.32f,z),new Vector3(.24f,1.26f,.19f),Finish.Rail);
            }
            for(int i=0;i<4;i++)
            {
                float z=-25.78f+i*2.59f;
                float x=Mathf.Lerp(9.95f,11.34f,i/3f)*side;
                hull.Panel(new Vector3(x,1.20f,z),outward,new Vector3(side*.12f,0,1).normalized,Vector3.up,
                    ChamferedPlate(1.16f,.73f,.18f),.2f,i==1 ? Finish.Bone : Finish.Alloy,Finish.Graphite);
            }
            for(int i=0;i<5;i++)
            {
                float z=-5.86f+i*2.9f;
                float x=side*Mathf.Lerp(11.42f,7.46f,i/4f);
                hull.Box(new Vector3(x,.56f,z),new Vector3(.24f,1.56f,2.45f),i%3==0 ? Finish.Graphite : Finish.Underbody,
                    Quaternion.Euler(0,-side*14,0));
                hull.Box(new Vector3(x+side*.11f,.74f,z-.19f),new Vector3(.19f,.27f,1.44f),Finish.Bone);
                hull.Box(new Vector3(x+side*.145f,.94f,z-.19f),new Vector3(.11f,.07f,.91f),Finish.Marker);
            }
        }

        static void BuildSignalBridgeCheek(Geometry hull,int side)
        {
            var outwards=Vector3.right*side;
            // Angular dark chine and heat-shielded side plates pinch toward the prow.
            hull.Hull(new Vector3(3.27f*side,.46f,0),new[]{
                new Rib(3.65f,3.08f,1.96f,.03f),new Rib(10.55f,4.02f,2.14f,.21f),new Rib(17.38f,3.78f,1.91f,.17f),
                new Rib(24.6f,2.93f,1.46f,.23f),new Rib(29.7f,1.62f,1.02f,.47f),new Rib(33.83f,.51f,.51f,.68f)},
                .61f,Finish.Bone,Finish.Alloy,Finish.Underbody);
            for(int panel=0;panel<5;panel++)
            {
                float z=10.58f+panel*4.26f;
                float half=Mathf.Lerp(1.98f,1.68f,panel/4f);
                float y=Mathf.Lerp(.49f,1.14f,panel/4f);
                float x=(panel==0 ? 7.52f : Mathf.Lerp(7.38f,5.22f,(panel-1)/3f))*side;
                Vector3 u=new Vector3(-side*(panel==0 ? .02f : .17f),0,1).normalized;
                Vector3 v=new Vector3(-side*.20f,1,0).normalized;
                Vector3 at=new(x,y,z);
                hull.Panel(at,outwards,u,v,ChamferedPlate(half,Mathf.Lerp(1.12f,.72f,panel/4f),.37f),
                    .29f,Finish.Alloy,Finish.Underbody);
                hull.Panel(at+outwards*.25f+Vector3.up*.33f+u*.41f,outwards,u,v,
                    ChamferedPlate(.75f,.29f,.12f),.081f,Finish.Graphite,Finish.Bone);
                for(int fastener=0;fastener<4;fastener++)
                {
                    var washer=at+outwards*.25f+u*(-1.4f+fastener*.83f)+v*.61f;
                    hull.Box(washer,new Vector3(.087f,.091f,.103f),Finish.Rail);
                }
            }
            Vector3 access=new(side*7.71f,.80f,13.8f);
            Vector3 axis=new(-side*.12f,0,1);
            var hatch=new[]{new Vector2(-1.53f,-.64f),new Vector2(1.02f,-.64f),new Vector2(1.66f,.68f),new Vector2(-.96f,.68f)};
            hull.Panel(access,outwards,axis,Vector3.up,hatch,.24f,Finish.Graphite,Finish.Bone);
            hull.Panel(access+outwards*.27f,outwards,axis,Vector3.up,ChamferedPlate(.92f,.42f,.18f),
                .11f,Finish.Alloy,Finish.Underbody);
            var chevron=new[]{new Vector2(-.69f,-.91f),new Vector2(-.09f,-.91f),new Vector2(.85f,.91f),
                new Vector2(.25f,.91f),new Vector2(-.26f,-.12f),new Vector2(-.51f,-.12f)};
            hull.Panel(new Vector3(side*7.49f,.75f,17.12f),outwards,axis,Vector3.up,chevron,.085f,Finish.Amber,Finish.Amber);

            if(side<0)
                hull.Hull(Vector3.zero,new[]{
                    new Rib(17.4f,3.42f,.26f,2.53f),new Rib(22.3f,4.18f,.50f,2.57f),new Rib(28.2f,4.34f,.53f,2.34f),
                    new Rib(32.6f,3.49f,.39f,2.14f),new Rib(34.42f,2.76f,.24f,1.89f)},
                    .74f,Finish.Alloy,Finish.Graphite,Finish.Underbody);
            for(int window=0;window<7;window++)
            {
                float t=window/6f;
                float z=24.35f+window*1.43f;
                float x=Mathf.Lerp(5.27f,3.33f,t);
                float y=Mathf.Lerp(2.40f,1.97f,t);
                var tilt=Quaternion.Euler(-3,-side*12,side*11);
                hull.Box(new Vector3(side*(x-.32f),y,z),new Vector3(.78f,.49f,1.43f),Finish.Underbody,tilt);
                hull.Box(new Vector3(side*(x+.076f),y,z-.025f),new Vector3(.11f,.27f,1.14f),Finish.Glass,tilt);
                hull.Box(new Vector3(side*(x+.128f),y-.105f,z-.025f),new Vector3(.065f,.061f,1.02f),Finish.Marker,tilt);
            }
            for(int window=0;window<4;window++)
            {
                float x=side*(.315f+window*.59f);
                hull.Box(new Vector3(x,1.883f,34.45f),new Vector3(.525f,.30f,.13f),Finish.Graphite);
                hull.Box(new Vector3(x,1.876f,34.52f),new Vector3(.391f,.19f,.058f),Finish.Glass);
                hull.Box(new Vector3(x,1.789f,34.548f),new Vector3(.339f,.037f,.028f),Finish.Marker);
            }
            for(int i=0;i<7;i++)
            {
                float z=4.4f+i*3.42f;
                var lamp=new Vector3(side*Mathf.Lerp(6.5f,3.44f,i/6f),-1.79f,z);
                hull.Box(lamp,new Vector3(.49f,.14f,1.16f),Finish.Graphite);
                hull.Box(lamp+outwards*.22f+Vector3.down*.04f,new Vector3(.092f,.10f,.73f),Finish.Marker);
            }
        }

        static void BuildSignalSuperstructure(Geometry hull)
        {
            hull.Hull(new Vector3(0,4.09f,-.78f),new[]{
                new Rib(-11.8f,1.22f,.51f,.40f),new Rib(-7.3f,2.78f,.95f,.48f),
                new Rib(.62f,2.51f,.99f,.59f),new Rib(4.88f,2.13f,.78f,.36f),new Rib(10.9f,1.58f,.33f,.12f)},
                .76f,Finish.Bone,Finish.Underbody,Finish.Graphite);
            for(int deck=0;deck<3;deck++)
            {
                float y=5.49f+deck*.58f;
                hull.Box(new Vector3(.12f,y,-2.56f),new Vector3(3.01f-deck*.48f,.44f,3.75f-deck*.81f),Finish.Graphite);
                hull.Box(new Vector3(.12f,y+.27f,-2.53f),new Vector3(3.12f-deck*.48f,.14f,3.82f-deck*.81f),Finish.Rail);
            }
            hull.Beam(new Vector3(.42f,6.94f,-2.74f),new Vector3(.42f,10.98f,-2.74f),.19f,.21f,Finish.Rail);
            hull.Beam(new Vector3(.42f,7.56f,-2.74f),new Vector3(.42f,11.35f,-2.74f),.105f,.11f,Finish.Bone);
            hull.Box(new Vector3(.42f,11.36f,-2.74f),new Vector3(.11f,.18f,.11f),Finish.Marker);
            for(int spar=0;spar<3;spar++)
                hull.Box(new Vector3(.42f,7.76f+spar*.73f,-2.74f),new Vector3(2.60f-spar*.61f,.11f,.17f),Finish.Rail);
            hull.Beam(new Vector3(.45f,9.5f,-2.72f),new Vector3(1.66f,6.42f,-4.57f),.064f,.061f,Finish.Graphite);
            hull.Beam(new Vector3(.45f,8.65f,-2.72f),new Vector3(-1.23f,6.04f,-.97f),.072f,.070f,Finish.Graphite);
            var dome=new Vector3(1.62f,6.17f,5.64f);
            hull.Box(dome+Vector3.down*.61f,new Vector3(.53f,1.18f,.52f),Finish.Graphite);
            hull.Tube(dome,new[]{(-.76f,.01f),(-.66f,.47f),(-.39f,.73f),(0,.82f),(.39f,.73f),(.66f,.47f),(.76f,.01f)},24,Finish.Bone,true);
            hull.Tube(dome,new[]{(-.067f,.82f),(.067f,.82f)},24,Finish.Rail,true);
            for(int cabinet=0;cabinet<5;cabinet++)
            {
                float z=-22.6f+cabinet*3.1f;
                float x=(cabinet%2==0 ? 1 : -1)*2.85f;
                hull.Box(new Vector3(x,4.1f,z),new Vector3(1.12f,.76f,1.35f),Finish.Underbody);
                hull.Box(new Vector3(x,4.47f,z),new Vector3(1.28f,.21f,1.42f),Finish.Alloy);
                for(int grille=0;grille<4;grille++)
                    hull.Box(new Vector3(x,4.61f,z-.41f+grille*.26f),new Vector3(.94f,.085f,.10f),Finish.Graphite);
            }
        }

        static Vector2[] ChamferedPlate(float halfLength,float halfHeight,float clipped) => new[]{
            new Vector2(-halfLength+clipped,-halfHeight),new Vector2(halfLength-clipped,-halfHeight),
            new Vector2(halfLength,-halfHeight+clipped),new Vector2(halfLength,halfHeight-clipped),
            new Vector2(halfLength-clipped,halfHeight),new Vector2(-halfLength+clipped,halfHeight),
            new Vector2(-halfLength,halfHeight-clipped),new Vector2(-halfLength,-halfHeight+clipped)
        };

        static Transform BuildAttacker(Transform orbit,Material[] materials)
        {
            var root=Group(orbit,"Hostile assault cutter / breaching collar",Vector3.zero);
            var hull=new Geometry("Assault cutter / recessed troop carrier");
            hull.Hull(Vector3.zero,new[]{new Rib(-10.25f,3.55f,1.21f,.31f),new Rib(-8.9f,4.42f,1.91f,.38f),
                new Rib(-5.1f,4.19f,2.21f,.44f),new Rib(-1.0f,3.02f,1.83f,.22f),
                new Rib(4.36f,2.30f,1.55f,.08f),new Rib(6.84f,1.71f,1.23f,0)},.62f,
                Finish.Raider,Finish.RaiderTrim,Finish.Graphite);
            hull.Hull(new Vector3(0,1.85f,-1.7f),new[]{new Rib(-5.4f,1.62f,.28f,0),new Rib(-4f,2.4f,.74f,.22f),
                new Rib(-1.4f,2.25f,.93f,.15f),new Rib(2.41f,1.93f,.57f,-.13f),new Rib(4.9f,.89f,.19f,-.32f)},
                .76f,Finish.RaiderTrim,Finish.Graphite,Finish.Graphite);
            var collar=new Vector3(0,0,6.67f);
            hull.Tube(collar,new[]{(-.15f,1.76f),(.82f,1.71f),(2.52f,1.68f),(2.9f,1.44f),(3.73f,1.43f)},
                12,Finish.RaiderTrim,true);
            hull.Tube(collar,new[]{(1.10f,1.02f),(2.58f,1.05f),(3.73f,1.10f)},12,Finish.Graphite,false);
            hull.Disc(collar+Vector3.forward*1.25f,1.07f,12,Finish.Scar,true);
            for(int tooth=0;tooth<6;tooth++)
            {
                var angle=Quaternion.Euler(0,0,tooth*60);
                hull.Box(collar+angle*new Vector3(0,1.46f,2.84f),new Vector3(.67f,.34f,1.71f),Finish.Rail,angle);
                hull.Box(collar+angle*new Vector3(0,1.63f,2.77f),new Vector3(.29f,.08f,1.23f),Finish.RaiderLamp,angle);
            }
            for(int side=-1;side<=1;side+=2)
            {
                float x=side*3.29f;
                hull.Hull(new Vector3(x,-.15f,-6.33f),new[]{new Rib(-4.64f,1.01f,1.08f,0),new Rib(-3.35f,1.6f,1.17f,0),
                    new Rib(.14f,1.62f,1.1f,0),new Rib(3.8f,1.15f,.91f,0),new Rib(4.75f,.58f,.46f,.10f)},.77f,
                    Finish.RaiderTrim,Finish.Raider,Finish.Graphite);
                hull.Tube(new Vector3(x,-.13f,-10.55f),new[]{(-.62f,.79f),(-.42f,.98f),(.50f,1.04f),(1.35f,.83f)},10,Finish.Graphite,true);
                hull.Disc(new Vector3(x,-.13f,-11.04f),.73f,12,Finish.Ember,false);
                hull.Box(new Vector3(x,1.28f,-4.52f),new Vector3(1.27f,.43f,4.82f),Finish.Graphite,Quaternion.Euler(0,side*-12,0));
                hull.Box(new Vector3(side*2.32f,2.12f,-1.83f),new Vector3(.19f,.27f,3.98f),Finish.RaiderLamp,
                    Quaternion.Euler(0,side*-12,0));
                for(int i=0;i<7;i++)
                {
                    float z=-7.96f+i*1.39f;
                    float outer=Mathf.Lerp(4.62f,2.73f,i/6f)*side;
                    hull.Box(new Vector3(outer,.96f,z),new Vector3(.24f,.46f,1.22f),Finish.Graphite,Quaternion.Euler(0,-side*16,0));
                    hull.Box(new Vector3(outer+side*.16f,.93f,z),new Vector3(.09f,.12f,.73f),
                        i%3==0 ? Finish.RaiderLamp : Finish.Rail);
                }
                hull.Box(new Vector3(side*2.13f,.84f,3.96f),new Vector3(.29f,.48f,3.7f),Finish.Amber,Quaternion.Euler(0,-side*11,0));
                hull.Box(new Vector3(side*2.14f,1.09f,3.56f),new Vector3(.20f,.12f,2.87f),Finish.Graphite,Quaternion.Euler(0,-side*11,0));
            }
            for(int i=0;i<6;i++)
                hull.Box(new Vector3(-1.67f+i*.66f,2.29f,2.31f-Mathf.Abs(i-2.5f)*.15f),new Vector3(.55f,.33f,.22f),Finish.RaiderLamp);
            Draw(root,"Assault shuttle hull, red running lights and extended breaching ring",
                Store("Assault cutter geometry",hull.ToMesh()),materials,Vector3.zero);
            var exhaust=Plasma("Cutter exhaust",new Color(2.7f,.48f,.14f),FalloffTexture());
            var plume=Store("Cutter engine plume",PlumeMesh(.78f,4.2f));
            foreach(float side in new[]{-1f,1f})
                Draw(root,"Cutter hot exhaust",plume,new[]{exhaust},new Vector3(3.29f*side,-.13f,-11.16f));
            root.gameObject.SetActive(false);
            return root;
        }

        static Transform BuildBoardingDeck(Transform parent,Material[] materials,out RobotPresentation[] boarders,out Light alarm)
        {
            var deck=Group(parent,"SIGNAL / pressure lock 02",new Vector3(0,-50,0));
            var structure=new Geometry("Pressure lock 02 / breached shuttle collar");
            structure.Box(new Vector3(0,-.20f,1.1f),new Vector3(7.6f,.38f,15.6f),Finish.Graphite);
            structure.Box(new Vector3(0,3.12f,1.05f),new Vector3(6.72f,.33f,14.85f),Finish.Graphite);
            structure.Box(new Vector3(0,1.48f,6.3f),new Vector3(5.9f,3.0f,.29f),Finish.Underbody);
            for(int i=0;i<12;i++)
            {
                float z=-5.75f+i*1.04f;
                for(int lane=-1;lane<=1;lane++)
                {
                    structure.Box(new Vector3(lane*1.59f,-.023f,z),new Vector3(1.54f,.064f,1.003f),
                        lane==0 ? Finish.Alloy : Finish.Underbody);
                    structure.Box(new Vector3(lane*1.59f,-.003f,z-.50f),new Vector3(1.53f,.039f,.042f),Finish.Graphite);
                }
            }
            for(int side=-1;side<=1;side+=2)
            {
                structure.Box(new Vector3(side*3.0f,1.43f,.5f),new Vector3(.28f,3.02f,14.5f),Finish.Alloy);
                structure.Box(new Vector3(side*2.32f,1.49f,1.56f),new Vector3(1.85f,3.05f,.41f),Finish.Underbody);
                structure.Box(new Vector3(side*1.51f,1.33f,1.5f),new Vector3(.37f,2.66f,.68f),Finish.Bone);
                structure.Box(new Vector3(side*1.37f,1.18f,1.146f),new Vector3(.087f,2.03f,.05f),Finish.Ember);
                structure.Box(new Vector3(side*1.60f,2.40f,1.42f),new Vector3(.24f,.66f,.56f),Finish.Graphite,
                    Quaternion.Euler(0,0,-side*28));
                structure.Box(new Vector3(side*1.75f,2.62f,1.27f),new Vector3(.50f,.09f,.05f),Finish.Marker);
                structure.Box(new Vector3(side*1.65f,-.015f,1.11f),new Vector3(.46f,.11f,1.66f),Finish.Amber);
                for(int i=0;i<7;i++)
                {
                    float z=1.09f+i*.68f;
                    structure.Box(new Vector3(side*1.59f,-.004f,z),new Vector3(.32f,.028f,.56f),Finish.Amber,
                        Quaternion.Euler(0,-side*30,0));
                    structure.Box(new Vector3(side*2.86f,1.08f,z-4.4f),new Vector3(.083f,1.36f,.40f),
                        i%3==0 ? Finish.Rail : Finish.Underbody);
                    structure.Box(new Vector3(side*1.72f,2.79f,z),new Vector3(.24f,.24f,.60f),Finish.RaiderTrim);
                    structure.Box(new Vector3(side*1.72f,2.655f,z),new Vector3(.09f,.035f,.46f),Finish.RaiderLamp);
                }
            }
            structure.Box(new Vector3(0,2.72f,1.5f),new Vector3(3.2f,.63f,.62f),Finish.Bone);
            structure.Box(new Vector3(0,2.64f,1.16f),new Vector3(1.7f,.31f,.08f),Finish.Graphite);
            structure.Box(new Vector3(0,2.48f,1.149f),new Vector3(1.30f,.043f,.05f),Finish.Ember);
            for(int i=0;i<11;i++)
            {
                float x=Mathf.Sin(i*5.13f)*1.48f;
                float z=.36f+Mathf.Sin(i*8.73f)*.61f;
                structure.Box(new Vector3(x,.03f+i%3*.027f,z),new Vector3(.14f+i%4*.086f,.063f,.23f+i%3*.083f),
                    i%5==0 ? Finish.Ember : Finish.Scar,Quaternion.Euler(i*13,i*39,i*3));
            }
            Draw(deck,"Bulkhead, torn pressure lock and red boarding collar",Store("Boarding lock geometry",structure.ToMesh()),
                materials,Vector3.zero);
            Paint(deck,"AIRLOCK 02",new Vector3(0,2.67f,1.105f),Quaternion.identity,.018f,new Color(.72f,.78f,.8f));
            var machinery=Group(deck,"Pressure lock equipment",Vector3.zero);
            Mount(machinery,Kit+"Walls/Wall_Gear_03_Half.prefab",new Vector3(-2.64f,1.04f,-.7f),1.1f,Quaternion.Euler(0,-90,0));
            Mount(machinery,Kit+"Walls/Wall_Gear_02_Half.prefab",new Vector3(2.66f,1.06f,-.6f),1.1f,Quaternion.Euler(0,90,0));
            FpsArenaGraphics.ConvertMaterials(machinery.gameObject,Output);
            alarm=AddLight(deck,"Red strobe from attacker",LightType.Point,new Vector3(.1f,2.21f,3.15f),new Color(1,.30f,.19f),4.4f,8);
            AddLight(deck,"Neutral overhead key",LightType.Point,new Vector3(.14f,2.72f,-1.71f),new Color(1,.91f,.80f),4.6f,7);
            AddLight(deck,"Soft cold hall fill",LightType.Point,new Vector3(-1.58f,2.28f,-2.35f),new Color(.64f,.77f,1),2.7f,7);
            AddLight(deck,"Firelit boarder edge",LightType.Point,new Vector3(1.11f,1.18f,1.09f),new Color(1,.61f,.32f),1.9f,4.6f);
            foreach(var light in deck.GetComponentsInChildren<Light>()) light.cullingMask=1<<OrbitLayer;
            var guard=AssetDatabase.LoadAssetAtPath<GameObject>(AdamRobotAuthoring.PrefabPath);
            if(!guard) throw new FileNotFoundException("The pressure lock requires the campaign guard rig.",AdamRobotAuthoring.PrefabPath);
            boarders=new RobotPresentation[2];
            for(int i=0;i<boarders.Length;i++)
            {
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(guard,deck);
                PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                instance.name=i==0 ? "Assault squad / point" : "Assault squad / support";
                boarders[i]=instance.GetComponent<RobotPresentation>();
                if(!boarders[i] || !boarders[i].walkClip) throw new InvalidOperationException("The assault squad requires the animated guard rig.");
                ApplyBoardingMaterials(boarders[i].model);
            }
            deck.gameObject.SetActive(false);
            return deck;
        }

        static void ApplyBoardingMaterials(Transform model)
        {
            // The edit-time cinematic camera and live campaign use different light/probe setups.
            // Keep the original painted texture on the cameo and use one explicit opaque pass.
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var slots=renderer.sharedMaterials;
                for(int i=0;i<slots.Length;i++)
                {
                    var source=slots[i];
                    if(!source || !source.HasProperty("_BaseMap")) continue;
                    string name=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source));
                    var cinematic=GetMaterial("Boarder "+name,"Universal Render Pipeline/Simple Lit");
                    cinematic.SetTexture("_BaseMap",source.GetTexture("_BaseMap"));
                    cinematic.SetColor("_BaseColor",source.GetColor("_BaseColor"));
                    cinematic.SetColor("_SpecColor",new Color(.14f,.14f,.14f));
                    cinematic.SetFloat("_Smoothness",.29f);
                    cinematic.DisableKeyword("_SPECULAR_COLOR");
                    EditorUtility.SetDirty(cinematic);
                    slots[i]=cinematic;
                }
                renderer.sharedMaterials=slots;
            }
        }

        static Transform BuildBackdrop(Transform orbit)
        {
            var planet=EarthAuthoring.Create(new Vector3(-292,-366,431),424);
            planet.transform.SetParent(orbit,false);
            planet.transform.rotation=Quaternion.Euler(-2,167,-17);
            var stars=GetMaterial("Signal dark orbital starfield","Doppelganger/DeepSpace");
            stars.SetFloat("_BackdropExposure",.39f);
            EditorUtility.SetDirty(stars);
            var dome=Draw(orbit,"Deep orbit starfield",AssetDatabase.LoadAssetAtPath<Mesh>(Earth+"EarthSphere.asset"),new[]{stars},Vector3.zero);
            dome.transform.localScale=Vector3.one*3100;
            dome.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            return planet.transform;
        }

        static ParticleSystem CreateSparks(Transform parent,Texture2D falloff)
        {
            var particles=Group(parent,"Hull fragments / spark burst",Vector3.zero).gameObject.AddComponent<ParticleSystem>();
            var main=particles.main;
            main.duration=2;
            main.loop=false;
            main.playOnAwake=false;
            main.maxParticles=230;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.startLifetime=new ParticleSystem.MinMaxCurve(.18f,1.22f);
            main.startSpeed=new ParticleSystem.MinMaxCurve(7.5f,32);
            main.startSize=new ParticleSystem.MinMaxCurve(.038f,.22f);
            main.startColor=new ParticleSystem.MinMaxGradient(new Color(1,.14f,.024f,.8f),new Color(1,.76f,.34f,1));
            var emission=particles.emission;
            emission.rateOverTime=0;
            emission.SetBursts(new[]{new ParticleSystem.Burst(0,145),new ParticleSystem.Burst(.04f,52)});
            var shape=particles.shape;
            shape.enabled=true;
            shape.shapeType=ParticleSystemShapeType.Cone;
            shape.angle=58;
            shape.radius=.4f;
            var lifetime=particles.colorOverLifetime;
            lifetime.enabled=true;
            var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.98f,.27f,.06f),1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.7f,.24f),new GradientAlphaKey(0,1)});
            lifetime.color=new ParticleSystem.MinMaxGradient(gradient);
            var render=particles.GetComponent<ParticleSystemRenderer>();
            render.sharedMaterial=Plasma("Collision sparks",new Color(2.7f,1.55f,.6f),falloff);
            render.renderMode=ParticleSystemRenderMode.Stretch;
            render.lengthScale=2.8f;
            render.velocityScale=.08f;
            render.shadowCastingMode=ShadowCastingMode.Off;
            render.receiveShadows=false;
            return particles;
        }

        static Material[] Palette()
        {
            var plate=PlateTexture();
            return new[]{
            Lit("Anodised alloy",new Color(.092f,.108f,.132f),.49f,.56f,texture:plate),
            Lit("Ceramic exterior",new Color(.135f,.15f,.172f),.34f,.52f,texture:plate),
            Lit("Underside iron",new Color(.057f,.072f,.092f),.42f,.44f,texture:plate),
            Lit("Rail graphite",new Color(.045f,.062f,.077f),.20f,.50f),
            Lit("Amber warning paint",new Color(.44f,.163f,.045f),.14f,.49f),
            Lit("Cockpit glass",new Color(.39f,.197f,.068f),.48f,.86f,new Color(1.47f,.63f,.113f)),
            Lit("Service channels",new Color(.159f,.181f,.199f),.52f,.51f),
            Lit("Cyan navigation lamps",new Color(.045f,.32f,.43f),.16f,.45f,new Color(.07f,1.12f,1.65f)),
            Lit("Ion throat",new Color(.26f,.74f,.9f),.18f,.7f,new Color(.38f,1.55f,2.4f)),
            Lit("Heat scar",new Color(.025f,.02f,.02f),.06f,.17f),
            Lit("Cutter pressure hull",new Color(.178f,.125f,.12f),.41f,.50f,texture:plate),
            Lit("Cutter red painted trim",new Color(.33f,.073f,.052f),.28f,.50f,texture:plate),
            Lit("Heated seams",new Color(.25f,.11f,.04f),.05f,.23f,new Color(.86f,.12f,.016f)),
            Lit("Amber running lights",new Color(.61f,.24f,.053f),.12f,.53f,new Color(1.42f,.37f,.055f)),
            Lit("Cutter red beacons",new Color(.59f,.06f,.035f),.16f,.44f,new Color(2.55f,.085f,.028f))
            };
        }

        static Material[] HeroPalette(Material[] common)
        {
            var skin=(Material[])common.Clone();
            var surface=new SignalHullSurface();
            Steel(Finish.Alloy,"Signal worn hull steel",new Color(.147f,.170f,.184f),1.12f);
            Steel(Finish.Bone,"Signal beveled armor steel",new Color(.204f,.222f,.225f),.88f);
            Steel(Finish.Underbody,"Signal dark armor steel",new Color(.073f,.091f,.110f),.91f);
            Steel(Finish.Graphite,"Signal heat exchanger iron",new Color(.043f,.057f,.069f),.43f);
            Steel(Finish.Rail,"Signal machined edges",new Color(.112f,.130f,.140f),.67f);
            skin[(int)Finish.Amber]=Lit("Signal weathered copper markings",new Color(.47f,.167f,.057f),.32f,.43f);
            skin[(int)Finish.Glass]=Lit("Signal amber bridge glazing",new Color(.122f,.073f,.032f),.39f,.82f,
                new Color(1.24f,.43f,.091f));
            skin[(int)Finish.Marker]=Lit("Signal warm position lamps",new Color(.48f,.19f,.052f),.20f,.62f,
                new Color(2.2f,.68f,.123f));
            return skin;

            void Steel(Finish finish,string name,Color color,float relief)
            {
                Material material=Lit(name,color,.53f,.75f);
                surface.Apply(material,relief);
                skin[(int)finish]=material;
            }
        }

        static Material Lit(string name,Color baseColor,float metal,float smooth,Color? emission=null,Texture2D texture=null)
        {
            var material=GetMaterial(name,"Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor",baseColor);
            material.SetFloat("_Metallic",metal);
            material.SetFloat("_Smoothness",smooth);
            if(texture) material.SetTexture("_BaseMap",texture);
            if(emission.HasValue)
            {
                material.SetColor("_EmissionColor",emission.Value);
                material.EnableKeyword("_EMISSION");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Plasma(string name,Color color,Texture2D map)
        {
            var material=GetMaterial(name,"Doppelganger/Prologue Plasma");
            material.SetColor("_Glow",color);
            material.SetTexture("_GlowMap",map);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GetMaterial(string name,string shaderName)
        {
            string path=Output+"/Materials/"+name+".mat";
            var shader=Shader.Find(shaderName);
            if(!shader) throw new InvalidOperationException("Missing shader: "+shaderName);
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material) return material;
            material=new Material(shader){name=name};
            AssetDatabase.CreateAsset(material,path);
            return material;
        }

        static Mesh Store(string name,Mesh generated)
        {
            string path=Output+"/"+name+".asset";
            generated.name=name;
            var previous=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!previous)
            {
                AssetDatabase.CreateAsset(generated,path);
                return generated;
            }
            EditorUtility.CopySerialized(generated,previous);
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(previous);
            return previous;
        }

        static Texture2D FalloffTexture()
        {
            string path=Output+"/Plasma falloff.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(existing) return existing;
            const int size=64;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false,true){name="Plasma falloff",wrapMode=TextureWrapMode.Clamp};
            var colors=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float xx=2*(x+.5f)/size-1, yy=2*(y+.5f)/size-1;
                float radial=Mathf.Clamp01(1-xx*xx-yy*yy);
                float value=radial*radial*radial;
                colors[y*size+x]=new Color(value,value,value,value);
            }
            texture.SetPixels(colors);
            texture.Apply();
            AssetDatabase.CreateAsset(texture,path);
            return texture;
        }

        static Texture2D PlateTexture()
        {
            const int size=512;
            string path=Output+"/Orbital hull plating.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(existing) return existing;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,true){
                name="Worn titanium paneling",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=6};
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float v=(y+.5f)*2/size;
                int row=Mathf.FloorToInt(v);
                float u=(x+.5f)*3/size+(row%2)*.5f;
                int column=Mathf.FloorToInt(u)%3;
                float h=Mathf.Repeat(u,1),k=Mathf.Repeat(v,1);
                float across=Mathf.Min(h,1-h),height=Mathf.Min(k,1-k);
                float seam=Mathf.Min(across,height);
                float variation=.70f+.16f*Mathf.PerlinNoise(column*3.56f+.2f,row*7.13f+.1f);
                float wear=Mathf.PerlinNoise(x*.051f+31,y*.04f+2.4f)*.08f+
                    Mathf.PerlinNoise(x*.51f+10,y*.042f+14)*.035f;
                float value=(variation+wear)*Mathf.SmoothStep(.29f,1,Mathf.InverseLerp(.006f,.029f,seam));
                if(seam>.022f && seam<.031f) value+=.11f;
                float rivet=Vector2.Distance(new Vector2(across,height),new Vector2(.072f,.078f));
                if(rivet<.026f) value*=rivet>.014f ? .54f : 1.08f;
                if(Mathf.Abs(k-.86f)<.018f && h>.14f && h<.81f && (column+row)%3==0) value*=.78f;
                byte shade=(byte)Mathf.RoundToInt(Mathf.Clamp01(value)*255);
                pixels[y*size+x]=new Color32(shade,shade,shade,255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true,false);
            AssetDatabase.CreateAsset(texture,path);
            return texture;
        }

        static Mesh GlowQuad() => new Mesh {
            vertices=new[]{new Vector3(-2,-2,0),new Vector3(2,-2,0),new Vector3(2,2,0),new Vector3(-2,2,0)},
            uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},
            colors=new[]{Color.white,Color.white,Color.white,Color.white},triangles=new[]{0,1,2,0,2,3}
        };

        static Mesh PlumeMesh(float radius,float length)
        {
            const int rings=7,sides=16;
            var positions=new Vector3[(rings+1)*(sides+1)];
            var uv=new Vector2[positions.Length];
            var colors=new Color[positions.Length];
            var triangles=new int[rings*sides*6];
            for(int i=0;i<=rings;i++) for(int j=0;j<=sides;j++)
            {
                float along=i/(float)rings,a=j*2*Mathf.PI/sides;
                float width=radius*(1+Mathf.Sin(along*Mathf.PI)*.20f)*Mathf.Pow(1-along,.51f);
                int n=i*(sides+1)+j;
                positions[n]=new Vector3(Mathf.Cos(a)*width,Mathf.Sin(a)*width,-length*along);
                uv[n]=new Vector2(.5f,.48f);
                colors[n]=new Color(1,1,1,.25f*Mathf.Pow(1-along,1.75f));
            }
            int p=0;
            for(int i=0;i<rings;i++) for(int j=0;j<sides;j++)
            {
                int a=i*(sides+1)+j,b=a+sides+1;
                triangles[p++]=a; triangles[p++]=a+1; triangles[p++]=b;
                triangles[p++]=a+1; triangles[p++]=b+1; triangles[p++]=b;
            }
            return new Mesh{vertices=positions,uv=uv,colors=colors,triangles=triangles};
        }

        static void Mount(Transform parent,string path,Vector3 point,float width,Quaternion rotation)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!prefab) throw new FileNotFoundException("Ship surface module missing: "+path);
            var mount=Group(parent,prefab.name+" / exterior mount",point);
            mount.localRotation=rotation;
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,mount);
            PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach(var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) if(behaviour) Object.DestroyImmediate(behaviour);
            foreach(var light in instance.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light);
            foreach(var lod in instance.GetComponentsInChildren<LODGroup>(true)) BridgeVisualQuality.UseHighestDetail(lod);
            var renderers=instance.GetComponentsInChildren<Renderer>().Where(item=>item.enabled).ToArray();
            if(renderers.Length==0) throw new InvalidOperationException("Ship module has no mesh: "+path);
            Bounds BoundsOf()
            {
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                return bounds;
            }
            var before=BoundsOf();
            float factor=width/Mathf.Max(.01f,Mathf.Max(before.size.x,before.size.z));
            instance.transform.localScale*=factor;
            instance.transform.localPosition=-mount.InverseTransformPoint(BoundsOf().center);
        }

        static void Paint(Transform parent,string words,Vector3 position,Quaternion rotation,float size,Color color)
        {
            var item=Group(parent,"Hull lettering / "+words,position);
            item.localRotation=rotation;
            var text=item.gameObject.AddComponent<TextMesh>();
            text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text=words;
            text.fontSize=98;
            text.fontStyle=FontStyle.Bold;
            text.characterSize=size;
            text.anchor=TextAnchor.MiddleCenter;
            text.alignment=TextAlignment.Center;
            text.color=color;
            var renderer=text.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=text.font.material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
        }

        static GameObject Draw(Transform parent,string name,Mesh mesh,Material[] materials,Vector3 position)
        {
            if(!mesh) throw new InvalidOperationException("Missing cinematic mesh: "+name);
            var obj=Group(parent,name,position).gameObject;
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterials=materials;
            return obj;
        }

        static Transform Group(Transform parent,string name,Vector3 position)
        {
            var group=new GameObject(name).transform;
            group.SetParent(parent,false);
            group.localPosition=position;
            return group;
        }

        static Light AddLight(Transform parent,string name,LightType type,Vector3 position,Color color,float intensity,float range)
        {
            var lamp=Group(parent,name,position).gameObject.AddComponent<Light>();
            lamp.type=type;
            lamp.color=color;
            lamp.intensity=intensity;
            if(range>0) lamp.range=range;
            lamp.shadows=LightShadows.None;
            return lamp;
        }

        static void Assign(SerializedObject target,string field,Object value) => target.FindProperty(field).objectReferenceValue=value;

        static void AssignArray<T>(SerializedObject target,string field,T[] values) where T:Object
        {
            var property=target.FindProperty(field);
            property.arraySize=values.Length;
            for(int i=0;i<values.Length;i++) property.GetArrayElementAtIndex(i).objectReferenceValue=values[i];
        }

        static void WriteSound()
        {
            const int rate=44100,channels=2;
            int count=Mathf.CeilToInt(CampaignPrologue.Duration*rate);
            using var wave=new BinaryWriter(File.Create(SoundPath));
            void Name(string value) => wave.Write(System.Text.Encoding.ASCII.GetBytes(value));
            Name("RIFF"); wave.Write(36+count*channels*2); Name("WAVE"); Name("fmt "); wave.Write(16);
            wave.Write((short)1); wave.Write((short)channels); wave.Write(rate); wave.Write(rate*channels*2);
            wave.Write((short)(channels*2)); wave.Write((short)16); Name("data"); wave.Write(count*channels*2);
            uint seed=2187;
            float low=0,slow=0;
            static float Sin(float t,float hz) => Mathf.Sin(t*hz*6.2831853f);
            for(int i=0;i<count;i++)
            {
                float t=i/(float)rate, hit=t-CampaignPrologue.ImpactAt;
                seed=unchecked(seed*1664525+1013904223);
                float noise=(seed>>8)*(2f/16777216f)-1;
                low+=(noise-low)*.062f;
                slow+=(noise-slow)*.008f;
                float normal=Mathf.Clamp01(t*1.4f)*Mathf.Clamp01((CampaignPrologue.Duration-.35f-t)/1.05f);
                float approach=Mathf.Clamp01((t-8.0f)/3.15f);
                float after=t>=CampaignPrologue.BoardingAt ? .31f : 1;
                float chord=(Sin(t,55)*.087f+Sin(t,82.41f)*.042f+Sin(t,111.2f)*.034f+Sin(t,166.42f)*.016f)
                    *(.83f+.17f*Sin(t,.31f))*after;
                float machinery=(slow*.27f+Sin(t,43.7f)*.043f)*(.68f+approach*.9f)*after;
                float tension=approach*(1-Mathf.Clamp01(hit*1.9f))*(Sin(t,137.6f)*.024f+Sin(t,147.4f)*.022f+low*.075f);
                float slam=0,shrapnel=0,alarm=0,breach=0;
                if(hit>=0)
                {
                    slam=(Sin(hit,35)*.64f+Sin(hit,63)*.22f+low*.70f)*Mathf.Exp(-hit*4.6f);
                    slam+=noise*.4f*Mathf.Exp(-hit*30f)+Sin(hit,186)*.14f*Mathf.Exp(-hit*8.9f);
                    shrapnel=low*Mathf.Exp(-hit*2.2f)*.43f*(.52f+.48f*Sin(hit,16.8f));
                    float local=t-CampaignPrologue.BoardingAt;
                    if(local>=0)
                    {
                        float pulse=Mathf.Repeat(local,.69f);
                        float envelope=Mathf.Clamp01(pulse/.015f)*Mathf.Clamp01((.17f-pulse)/.059f);
                        alarm=(Sin(local,592)*.11f+Sin(local,888)*.037f)*envelope;
                        alarm+=Sin(local,22)*.088f*Mathf.Clamp01((3.25f-local)/.7f);
                        if(t<CampaignPrologue.BridgeAt)
                        {
                            float boot=Mathf.Repeat(local+.22f,.47f);
                            float latch=Mathf.Exp(-local*9);
                            breach=(noise*.083f+Sin(local,61)*.17f)*latch
                                +(Sin(boot,92)*.11f+low*.16f)*Mathf.Exp(-boot*42);
                        }
                    }
                }
                float stereo=(Sin(t,110.06f)*.014f+Sin(t,139.4f)*.012f)*after;
                float left=(float)Math.Tanh((chord+machinery+tension+slam+shrapnel+alarm+breach+stereo)*1.25f)*normal;
                float right=(float)Math.Tanh((chord+machinery+tension+slam+shrapnel*.72f+alarm+breach*.84f-stereo)*1.22f)*normal;
                wave.Write((short)(left*31700)); wave.Write((short)(right*31700));
            }
        }

        readonly struct Rib
        {
            public readonly float Z,W,H,Y;
            public Rib(float z,float width,float height,float y) { Z=z; W=width; H=height; Y=y; }
            public Vector3[] Vertices(float corner) => new[]{
                new Vector3(-W*corner,Y+H,Z),new Vector3(W*corner,Y+H,Z),
                new Vector3(W,Y+H*corner,Z),new Vector3(W,Y-H*corner,Z),
                new Vector3(W*corner,Y-H,Z),new Vector3(-W*corner,Y-H,Z),
                new Vector3(-W,Y-H*corner,Z),new Vector3(-W,Y+H*corner,Z)
            };
        }

        sealed class Geometry
        {
            readonly string name;
            readonly List<Vector3> points=new(), normals=new();
            readonly List<Vector2> uv=new();
            readonly List<int>[] triangles=Enumerable.Range(0,PaletteSize).Select(_=>new List<int>()).ToArray();

            public Geometry(string title) { name=title; }

            public void Face(Finish material,Vector3 a,Vector3 b,Vector3 c,Vector3 outward=default)
            {
                if(outward==Vector3.zero) outward=a+b+c;
                var normal=Vector3.Cross(b-a,c-a).normalized;
                if(Vector3.Dot(normal,outward)<0) { (b,c)=(c,b); normal=-normal; }
                int start=points.Count;
                points.Add(a); points.Add(b); points.Add(c);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                Vector2 Projection(Vector3 p) => Mathf.Abs(normal.y)>.5f ? new Vector2(p.x,p.z) :
                    Mathf.Abs(normal.x)>.5f ? new Vector2(p.z,p.y) : new Vector2(p.x,p.y);
                uv.Add(Projection(a)*.18f); uv.Add(Projection(b)*.18f); uv.Add(Projection(c)*.18f);
                var target=triangles[(int)material];
                target.Add(start); target.Add(start+1); target.Add(start+2);
            }

            void Quad(Finish m,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward)
            { Face(m,a,b,c,outward); Face(m,a,c,d,outward); }

            public void Box(Vector3 origin,Vector3 size,Finish material,Quaternion? rotation=null)
            {
                var orientation=rotation ?? Quaternion.identity;
                Vector3 Corner(int x,int y,int z)=>origin+orientation*Vector3.Scale(size*.5f,new Vector3(x,y,z));
                var a=Corner(-1,-1,-1); var b=Corner(1,-1,-1); var c=Corner(1,1,-1); var d=Corner(-1,1,-1);
                var e=Corner(-1,-1,1); var f=Corner(1,-1,1); var g=Corner(1,1,1); var h=Corner(-1,1,1);
                Quad(material,a,b,c,d,orientation*Vector3.back);
                Quad(material,e,f,g,h,orientation*Vector3.forward);
                Quad(material,a,b,f,e,orientation*Vector3.down);
                Quad(material,d,c,g,h,orientation*Vector3.up);
                Quad(material,a,e,h,d,orientation*Vector3.left);
                Quad(material,b,f,g,c,orientation*Vector3.right);
            }

            public void Beam(Vector3 start,Vector3 end,float width,float depth,Finish material)
            {
                Vector3 line=end-start;
                Box((start+end)*.5f,new Vector3(width,depth,line.magnitude),material,
                    Quaternion.FromToRotation(Vector3.forward,line.normalized));
            }

            public void Panel(Vector3 center,Vector3 outward,Vector3 horizontal,Vector3 vertical,Vector2[] profile,
                float relief,Finish surface,Finish edge)
            {
                Vector3 Along(Vector2 point) => horizontal*point.x+vertical*point.y;
                Vector3 top=center+outward*relief;
                for(int i=0;i<profile.Length;i++)
                {
                    Vector3 a=Along(profile[i]),b=Along(profile[(i+1)%profile.Length]);
                    Vector3 cornerA=center+a,cornerB=center+b;
                    Vector3 chamferA=top+a*.92f,chamferB=top+b*.92f;
                    Face(surface,top,chamferA,chamferB,outward);
                    Quad(edge,cornerA,cornerB,chamferB,chamferA,(a+b).normalized+outward);
                    Quad(edge,cornerA-outward*.13f,cornerB-outward*.13f,cornerB,cornerA,a+b);
                }
            }

            public void Slab(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float height,Finish top,Finish edge)
            {
                var down=Vector3.down*height;
                Quad(top,a,b,c,d,Vector3.up);
                Quad(edge,a+down,b+down,c+down,d+down,Vector3.down);
                Vector3 center=(a+b+c+d)*.25f;
                Quad(edge,a,b,b+down,a+down,(a+b)*.5f-center);
                Quad(edge,b,c,c+down,b+down,(b+c)*.5f-center);
                Quad(edge,c,d,d+down,c+down,(c+d)*.5f-center);
                Quad(edge,d,a,a+down,d+down,(d+a)*.5f-center);
            }

            public void Hull(Vector3 offset,Rib[] ribs,float bevel,Finish dorsal,Finish mid,Finish ventral)
            {
                Vector3[][] vertices=ribs.Select(rib=>rib.Vertices(bevel)).ToArray();
                for(int i=0;i<ribs.Length-1;i++) for(int edge=0;edge<8;edge++)
                {
                    int next=(edge+1)%8;
                    Vector3 a=vertices[i][edge],b=vertices[i][next],c=vertices[i+1][next],d=vertices[i+1][edge];
                    var face=edge==0 ? dorsal : edge==3 || edge==4 || edge==5 ? ventral : mid;
                    var normal=(a+b+c+d)*.25f;
                    normal.y-=(ribs[i].Y+ribs[i+1].Y)*.5f;
                    normal.z=0;
                    Quad(face,a+offset,b+offset,c+offset,d+offset,normal);
                }
                for(int side=0;side<2;side++)
                {
                    int index=side==0 ? 0 : ribs.Length-1;
                    Vector3 center=new Vector3(0,ribs[index].Y,ribs[index].Z)+offset;
                    for(int edge=0;edge<8;edge++)
                        Face(mid,center,vertices[index][edge]+offset,vertices[index][(edge+1)%8]+offset,
                            side==0 ? Vector3.back : Vector3.forward);
                }
            }

            public void Tube(Vector3 offset,(float z,float radius)[] rings,int sides,Finish material,bool outside)
            {
                Vector3 Point(int ring,int index)
                {
                    float angle=index*2*Mathf.PI/sides;
                    return new Vector3(Mathf.Cos(angle)*rings[ring].radius,Mathf.Sin(angle)*rings[ring].radius,rings[ring].z)+offset;
                }
                for(int i=0;i<rings.Length-1;i++) for(int j=0;j<sides;j++)
                {
                    var a=Point(i,j); var b=Point(i,j+1); var c=Point(i+1,j+1); var d=Point(i+1,j);
                    Vector3 normal=(a+b+c+d)*.25f-offset;
                    normal.z=0;
                    Quad(material,a,b,c,d,outside ? normal : -normal);
                }
            }

            public void Disc(Vector3 position,float radius,int sides,Finish material,bool forward)
            {
                for(int i=0;i<sides;i++)
                {
                    float a=i*2*Mathf.PI/sides,b=(i+1)*2*Mathf.PI/sides;
                    Face(material,position,position+new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radius,
                        position+new Vector3(Mathf.Cos(b),Mathf.Sin(b),0)*radius,forward ? Vector3.forward : Vector3.back);
                }
            }

            public Mesh ToMesh()
            {
                var mesh=new Mesh{name=name,indexFormat=points.Count>65530 ? IndexFormat.UInt32 : IndexFormat.UInt16};
                mesh.SetVertices(points);
                mesh.SetNormals(normals);
                mesh.SetUVs(0,uv);
                mesh.subMeshCount=triangles.Length;
                for(int i=0;i<triangles.Length;i++) mesh.SetTriangles(triangles[i],i,false);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
