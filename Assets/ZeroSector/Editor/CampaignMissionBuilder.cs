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

namespace ZeroSector.Editor
{
    /// <summary>Authored rooms, not clones of the learning arena. Runtime progression lives in CampaignGame.</summary>
    public static class CampaignMissionBuilder
    {
        const string Pack="Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Prefabs/";
        const string Styled="Assets/Sci-Fi Styled Modular Pack/Prefabs/";
        const string Output="Assets/DoppelgangerGenerated/Campaign";
        static readonly string[] Names={"01_CommandBridge","02_Engineering","03_DoppelgangerCore"};
        static readonly List<string> Report=new();
        static Transform geometry,lights,decor,signage;
        static HashSet<Vector2Int> cells;
        static readonly Vector2Int[] Neighbours={Vector2Int.up,Vector2Int.right,Vector2Int.down,Vector2Int.left};

        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before authoring campaign scenes.");
            // Preserve all user work. Existing campaign rooms are backed up and deliberately replaced.
            var oldScenes=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            foreach(var scene in oldScenes)
            {
                if(!scene.isDirty) continue;
                if(!scene.path.StartsWith(Output+"/",StringComparison.Ordinal))
                    throw new InvalidOperationException("Save the unrelated scene before building episodes: "+scene.name);
                if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not preserve scene: "+scene.name);
            }
            if(!AssetDatabase.IsValidFolder(Pack.TrimEnd('/'))) throw new DirectoryNotFoundException("Import Creepy Cat Starter Kit first.");
            Directory.CreateDirectory(Output); Directory.CreateDirectory("Diagnostics/CampaignPreviews");
            BackupScenes(); Report.Clear();
            EarthAuthoring.Prepare(); FpsSampleArt.Prepare();
            for(int chapter=0;chapter<3;chapter++)
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                try
                {
                    geometry=new GameObject("01 / Mission architecture").transform;
                    decor=new GameObject("02 / Mission furniture and consoles").transform;
                    lights=new GameObject("03 / Authored lighting").transform;
                    signage=new GameObject("04 / Wayfinding").transform;
                    if(chapter==0) Bridge(); else if(chapter==1) Engineering(); else Core();
                    var game=Actors(chapter);
                    Lighting(chapter,game.eyes);
                    FpsArenaGraphics.ConvertMaterials(geometry.gameObject,Output);
                    FpsArenaGraphics.ConvertMaterials(decor.gameObject,Output);
                    FinishMaterials();
                    if(chapter==0) EarthAuthoring.Create(new Vector3(-28,-130,240),135);
                    Validate(scene,game);
                    string path=Output+"/"+Names[chapter]+".unity";
                    EditorSceneManager.SaveScene(scene,path);
                    Report.Add(path+" | deck tiles "+cells.Count+" | mesh renderers "+scene.GetRootGameObjects().Sum(root=>root.GetComponentsInChildren<Renderer>().Length));
                }
                catch
                {
                    // Failed generated work is ours; discard it so a repair can be rerun without an unsaved-scene dialog.
                    string recovery=Output+"/"+Names[0]+".unity";
                    if(File.Exists(recovery)) EditorSceneManager.OpenScene(recovery,OpenSceneMode.Single);
                    throw;
                }
            }
            EditorBuildSettings.scenes=Names.Select(name=>new EditorBuildSettingsScene(Output+"/"+name+".unity",true)).ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Output+"/"+Names[0]+".unity",OpenSceneMode.Single);
            File.WriteAllLines("Diagnostics/CampaignPreviews/mission-build-result.txt",Report);
            Debug.Log("Двойник: три самостоятельных эпизода собраны. Мостик — исследование; инженерный — два маршрута; ядро — обход реактора и финал.");
        }

        static void BackupScenes()
        {
            string archive="Diagnostics/ArchivedCampaignRooms/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            foreach(string name in Names)
            {
                string source=Output+"/"+name+".unity";
                if(!File.Exists(source)) continue;
                Directory.CreateDirectory(archive);
                File.Copy(source,archive+"/"+name+".unity",false);
                if(File.Exists(source+".meta")) File.Copy(source+".meta",archive+"/"+name+".unity.meta",false);
            }
        }

        static void Bridge()
        {
            // Long, quiet command deck. Rear operations bays and a completely open forward viewing gallery.
            cells=new HashSet<Vector2Int>();
            for(int x=-2;x<=1;x++) for(int z=-2;z<=2;z++) cells.Add(new Vector2Int(x,z));
            Deck(0,cell=>new Vector3(cell.x*6+3,0,cell.y*6));
            for(int x=-9;x<=9;x+=6)
            {
                if(x!=-3) Furniture("Decorative elements/computer_station",new Vector3(x,0,10.1f),2.2f,180,1.35f);
                Furniture("Decorative elements/pilot_seat",new Vector3(x,0,8.1f),.9f,180,1.35f);
            }
            // Captain's table stays below eye level, leaving the Earth clearly visible beyond it.
            Furniture("Decorative elements/console",new Vector3(0,0,1),2.8f,180,1.25f);
            Furniture("Decorative elements/decorative_chair",new Vector3(0,0,-1.1f),1.0f,0,1.4f);
            Furniture("Decorative elements/cabinet",new Vector3(-10.3f,0,-4),1.4f,90,2.5f);
            Furniture("Decorative elements/cabinet",new Vector3(10.3f,0,-4),1.4f,-90,2.5f);
            Terminal("log","Прочитать журнал командира",new Vector3(-8.5f,0,-8.5f),35,"01 / SHIP LOG",new Color(.2f,.85f,1));
            Terminal("power","Восстановить питание мостика",new Vector3(9.2f,0,-5.8f),-90,"02 / AUX POWER",new Color(1,.55f,.15f));
            Terminal("nav","Проверить навигационный сигнал",new Vector3(-3,0,10),180,"03 / NAVIGATION",new Color(.2f,.85f,1));
            Terminal("exit","Открыть переход в инженерный отсек",new Vector3(0,0,-13.3f),0,"ENGINEERING / ACCESS",new Color(.2f,1,.6f));
            Module("Doors/DoorWay_01_Large",new Vector3(0,0,-14.6f),5.5f,0,true,false,geometry);
            AccentSymbol("Symbols/Word_Deck",new Vector3(-8,2.4f,-14.72f),1.8f,0);
            AccentSymbol("Symbols/Num_1",new Vector3(-5.5f,2.4f,-14.72f),.65f,0);
            for(int side=-1;side<=1;side+=2)
            {
                Module("Walls/Wall_Console_01_Half",new Vector3(side*11.45f,0,2),2.7f,side>0 ? -90 : 90,true,false,decor);
                Furniture("Decorative elements/decorative_plant",new Vector3(side*10,0,6.5f),.7f,0,1.2f);
            }
            Label("COMMAND BRIDGE",new Vector3(0,3,-14.65f),0,new Color(.3f,.8f,1),.065f);
        }

        static void Engineering()
        {
            // Entry corridor opens into two working halls, joined again at the northern pressure lock.
            cells=new HashSet<Vector2Int>();
            for(int z=-1;z<=1;z++) for(int x=-2;x<=2;x++) cells.Add(new Vector2Int(x,z));
            for(int z=-3;z<=3;z++) cells.Add(new Vector2Int(0,z));
            cells.Add(new Vector2Int(-1,-3)); cells.Add(new Vector2Int(1,-3));
            cells.Add(new Vector2Int(-1,3)); cells.Add(new Vector2Int(1,3));
            Deck(1,cell=>new Vector3(cell.x*6,0,cell.y*6));
            foreach(int side in new[]{-1,1})
            {
                for(int i=0;i<3;i++)
                {
                    float z=-5.4f+i*5.4f;
                    Furniture("Machines/generator",new Vector3(side*4.8f,0,z),2.6f,side>0 ? 90 : -90,3.5f);
                    Module("Stuff/Pipes_02",new Vector3(side*14.1f,0,z),1.5f,side>0 ? -90 : 90,true,false,decor);
                }
                Furniture("Machines/Battery_big",new Vector3(side*11.5f,0,-6.4f),1.75f,0,2.8f);
                Furniture("Machines/Capacitor",new Vector3(side*11.8f,0,6.7f),1.8f,0,3.4f);
                Cover(new Vector3(side*8.8f,0,-1.8f),1.7f,false);
                Cover(new Vector3(side*8.8f,0,5),1.7f,true);
                Module("Doors/DoorWay_01_Large",new Vector3(0,0,side*10),5.5f,0,true,false,geometry);
            }
            // Partial bulkheads create occlusion while retaining a six metre central opening and wide outer routes.
            foreach(int side in new[]{-1,1})
                foreach(float z in new[]{-1.8f,3.6f})
                    Module("Walls/Wall_Gear_02_Half",new Vector3(side*2.25f,0,z),1.4f,90,true,false,decor);
            Terminal("relayA","Перезапустить западное силовое реле",new Vector3(-12,0,2.8f),90,"RELAY A / WEST",new Color(1,.45f,.12f));
            Terminal("relayB","Перезапустить восточное силовое реле",new Vector3(12,0,2.8f),-90,"RELAY B / EAST",new Color(.2f,.8f,1));
            Terminal("exit","Разблокировать переход в ядро",new Vector3(0,0,19.6f),180,"CORE / PRESSURE LOCK",new Color(.2f,1,.6f));
            Supply("ammo1","Взять боеприпасы",new Vector3(-12,0,-2.8f),"AMMUNITION",new Color(1,.65f,.15f));
            Supply("health1","Использовать медицинский комплект",new Vector3(12,0,-2.8f),"MEDICAL / RESTORE",new Color(.2f,1,.55f));
            Supply("ammo2","Пополнить боеприпасы перед ядром",new Vector3(-5.6f,0,18.3f),"CORE / AMMUNITION",new Color(1,.65f,.15f));
            Furniture("Machines/storage_container_small",new Vector3(-5.8f,0,-18.6f),2.1f,90,1.8f);
            Furniture("Machines/storage_container_small",new Vector3(5.8f,0,18.6f),2.1f,-90,1.8f);
            Label("ENGINEERING / TWO CIRCUITS",new Vector3(0,3.8f,-8.65f),180,new Color(1,.55f,.22f),.055f);
            Label("WEST / A",new Vector3(-8,3.6f,-8.65f),180,new Color(1,.55f,.22f),.055f);
            Label("EAST / B",new Vector3(8,3.6f,-8.65f),180,new Color(.25f,.8f,1),.055f);
        }

        static void Core()
        {
            // The missing corner tiles give a stepped octagonal silhouette. A physical reactor splits sight lines.
            cells=new HashSet<Vector2Int>();
            for(int x=-2;x<=2;x++) for(int z=-2;z<=2;z++)
                if(!(Mathf.Abs(x)==2 && Mathf.Abs(z)==2)) cells.Add(new Vector2Int(x,z));
            Deck(2,cell=>new Vector3(cell.x*6,0,cell.y*6));
            var reactor=Furniture("Machines/Shield Core",new Vector3(0,0,2),4.9f,0,4.5f);
            reactor.name="Doppelganger neural reactor / physical line of sight cover";
            Furniture("Machines/shield_generator",new Vector3(0,0,2),2.5f,0,6.3f);
            for(int i=0;i<4;i++)
            {
                float angle=(45+i*90)*Mathf.Deg2Rad;
                Vector3 pos=new Vector3(Mathf.Sin(angle)*10.4f,0,Mathf.Cos(angle)*10.4f);
                Module("Walls/Column_01_Big",pos,1.65f,45+i*90,true,false,decor);
                Point("Reactor perimeter uplight",pos+Vector3.up*3,new Color(.45f,.2f,1),4,7,false);
            }
            foreach(int side in new[]{-1,1})
            {
                Cover(new Vector3(side*5.8f,0,-3.8f),1.8f,true);
                Cover(new Vector3(side*6.8f,0,5.4f),1.8f,false);
                Furniture("Machines/Battery_medium",new Vector3(side*11.6f,0,6),1.2f,side>0 ? -90 : 90,2.9f);
                Furniture("Machines/Capacitor",new Vector3(side*12,0,-5),1.6f,0,3.7f);
            }
            Terminal("coolantA","Перекрыть западный контур охлаждения",new Vector3(-11.8f,0,0),90,"COOLANT A",new Color(.15f,.8f,1));
            Terminal("coolantB","Перекрыть восточный контур охлаждения",new Vector3(11.8f,0,0),-90,"COOLANT B",new Color(.15f,.8f,1));
            Terminal("core","Отключить ядро Двойника",new Vector3(0,0,8.6f),180,"DOPPELGANGER / CORE",new Color(.9f,.25f,.5f));
            Module("Doors/DoorWay_01_Large",new Vector3(0,0,-14.5f),5.5f,0,true,false,geometry);
            Point("Reactor heart / violet",new Vector3(0,2.8f,2),new Color(.65f,.18f,1),8,12,true);
            Label("NEURAL CORE / AUTHORIZED PERSONNEL",new Vector3(0,3.5f,-14.6f),0,new Color(.7f,.45f,1),.055f);
        }

        static void Deck(int chapter,Func<Vector2Int,Vector3> position)
        {
            foreach(var cell in cells)
            {
                Vector3 center=position(cell);
                Module(chapter==1 ? "Floors/Floor_Squared_02_6x6" : "Floors/Floor_Squared_01_6x6",center,6,0,false,true,geometry);
                if(chapter==1) Module("Floors/Floor_Squared_01_6x6",center+Vector3.down*.28f,6,0,false,true,geometry);
                Box("Deck collision",center+Vector3.down*.19f,new Vector3(6,.38f,6),geometry);
                Module(chapter==2 ? "Roofs/Roof_Squared_02_6x6" : "Roofs/Roof_Squared_01_6x6",center+Vector3.up*8,6,0,false,true,geometry);
                for(int edge=0;edge<4;edge++)
                {
                    if(cells.Contains(cell+Neighbours[edge])) continue;
                    Vector3 outward=new Vector3(Neighbours[edge].x,0,Neighbours[edge].y);
                    Vector3 wallPosition=center+outward*3;
                    float yaw=edge==0 ? 180 : edge==1 ? -90 : edge==2 ? 0 : 90;
                    bool window=chapter==0 && edge==0 && cell.y==2;
                    string wall=window ? "Walls/Porthole_01_Long" : chapter==2 ? "Walls/Wall_Simple_03_Long" : "Walls/Wall_Simple_01_Long";
                    var lower=Module(wall,wallPosition,6,yaw,true,false,geometry);
                    float wallTop=BoundsOf(lower).max.y;
                    for(int tier=1;wallTop<7.98f && tier<8;tier++)
                    {
                        var upper=Module("Walls/Wall_Simple_01_Long",wallPosition+Vector3.up*(wallTop-.025f),6,yaw,true,false,geometry);
                        wallTop=BoundsOf(upper).max.y;
                    }
                    Box("Pressure hull collision",wallPosition+Vector3.up*4,edge%2==0 ? new Vector3(6,8,.3f) : new Vector3(.3f,8,6),geometry);
                    if(!window && chapter==1 && edge%2==1)
                        Module("Walls/Wall_Pipes_01_Large",wallPosition-outward*.08f,6,yaw,false,false,decor);
                    if(!window)
                    {
                        Module("Lights/Light_SideLight_01",wallPosition-outward*.22f+Vector3.up*2.6f,1.1f,yaw,false,false,decor);
                        if((cell.x+cell.y)%2==0)
                            Point("Hull accent",wallPosition-outward*.6f+Vector3.up*2.8f,chapter==1 ? new Color(1,.35f,.08f) : new Color(.12f,.5f,1),2.3f,7,false);
                    }
                }
                if((cell.x+cell.y)%2==0)
                {
                    var light=Point("Working deck pool",center+Vector3.up*5.4f,new Color(.65f,.78f,1),9.5f,11,false);
                    light.type=LightType.Spot; light.spotAngle=105; light.innerSpotAngle=70;
                    light.transform.rotation=Quaternion.Euler(90,0,0);
                }
            }
        }

        static CampaignGame Actors(int chapter)
        {
            var human=new GameObject("Commander / campaign player",typeof(CharacterController));
            var controller=human.GetComponent<CharacterController>();
            controller.height=1.8f; controller.radius=.35f; controller.center=Vector3.up*.9f;
            controller.skinWidth=.035f; controller.stepOffset=.28f; controller.slopeLimit=50;
            human.transform.position=new Vector3(0,.05f,chapter==0 ? -9.5f : chapter==1 ? -18 : -11.5f);
            var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
            camera.tag="MainCamera"; camera.transform.SetParent(human.transform,false); camera.transform.localPosition=Vector3.up*1.65f;
            camera.fieldOfView=75; camera.nearClipPlane=.035f; camera.farClipPlane=1500;
            var root=new GameObject("Doppelganger / playable campaign",typeof(CampaignGame),typeof(CampaignHud),typeof(FirstPersonPresentation));
            var game=root.GetComponent<CampaignGame>(); game.chapter=chapter; game.player=controller; game.eyes=camera;
            game.navigationMin=chapter==0 ? new Vector2(-12,-15) : chapter==1 ? new Vector2(-15,-21) : new Vector2(-15,-15);
            game.navigationMax=chapter==0 ? new Vector2(12,15) : chapter==1 ? new Vector2(15,21) : new Vector2(15,15);
            game.hud=root.GetComponent<CampaignHud>(); game.presentation=root.GetComponent<FirstPersonPresentation>();
            game.presentation.body=controller; game.presentation.eyes=camera;
            InstallWeapons(game.presentation,camera);
            var anchors=new GameObject("Mission route anchors / enemy spawn points").transform;
            Vector3[] positions=chapter==0 ? Array.Empty<Vector3>() : chapter==1 ? new[]{new Vector3(-9,0,0),new Vector3(9,0,0),new Vector3(0,0,15)} : new[]{new Vector3(0,0,11)};
            game.enemySpawns=positions.Select((position,index)=>
            {
                var point=new GameObject("Enemy spawn / "+index).transform; point.SetParent(anchors,false);
                point.position=position+Vector3.up*.05f; point.rotation=Quaternion.Euler(0,180,0); return point;
            }).ToArray();
            // Adam authoring can install its licensed prefab after this stage without touching geometry.
            string[] candidates={"Assets/DoppelgangerGenerated/Characters/AdamRobot.prefab","Assets/DoppelgangerGenerated/Characters/AdamEnemy.prefab","Assets/Resources/AdamEnemy.prefab"};
            game.enemyPrefab=candidates.Select(AssetDatabase.LoadAssetAtPath<GameObject>).FirstOrDefault(prefab=>prefab);
            return game;
        }

        static void InstallWeapons(FirstPersonPresentation view,Camera camera)
        {
            view.weaponPivot=new GameObject("First person weapon / procedural motion").transform;
            view.weaponPivot.SetParent(camera.transform,false);
            foreach(string kind in new[]{"Terraformer","Robot"})
            {
                string path="Assets/DoppelgangerGenerated/Weapons/"+kind+".prefab";
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(!prefab) throw new FileNotFoundException("Missing authored FPS weapon: "+path);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,view.weaponPivot);
                var slot=instance.GetComponent<WeaponArtBinding>().specification; slot.root=instance.transform;
                view.weapons.Add(slot);
            }
            view.Equip(0);
            view.walkSound=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Easy FPS/Sounds/playerSound/walk_sound 1.wav");
            view.runSound=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Easy FPS/Sounds/playerSound/run_sound 1.wav");
            view.reloadSound=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Easy FPS/Sounds/reloadSound.wav");
            view.switchSound=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Easy FPS/Sounds/pullweapon.wav");
            view.hitSound=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Easy FPS/Sounds/hitMarker.wav");
        }

        static GameObject Terminal(string id,string title,Vector3 position,float yaw,string sign,Color color)
        {
            var root=Furniture("Decorative elements/computer_station",position,1.55f,yaw,1.4f);
            root.name="DG.Interactable."+id;
            // Put the interaction anchor at the monitor; preserve the visible terminal and its collisions in world space.
            root.transform.position+=Vector3.up*1.3f;
            foreach(Transform child in root.transform) child.position-=Vector3.up*1.3f;
            var interaction=root.AddComponent<CampaignInteractable>(); interaction.id=id; interaction.title=title; interaction.range=3.3f;
            // Real mesh collider is on the authored terminal; a generous child trigger keeps it easy to target.
            var marker=new GameObject("Interaction target",typeof(BoxCollider)); marker.transform.SetParent(root.transform,false);
            marker.transform.position=position+Vector3.up*.9f; marker.transform.rotation=Quaternion.Euler(0,yaw,0);
            marker.transform.localScale=Vector3.one/root.transform.lossyScale.x;
            var box=marker.GetComponent<BoxCollider>(); box.size=new Vector3(1.65f,1.6f,.7f); box.isTrigger=true;
            Label(sign,position+Vector3.up*1.95f,yaw,color,.035f);
            Point(sign+" terminal glow",position+Vector3.up*1.7f,color,1.6f,3.7f,false);
            return root;
        }

        static void Cover(Vector3 position,float width,bool stacked)
        {
            var crate=Module("Props/Crate_01",position,width,0,true,false,decor);
            if(stacked) Module("Props/Crate_01",position+Vector3.up*BoundsOf(crate).size.y,width,0,true,false,decor);
        }

        static void Supply(string id,string title,Vector3 position,string sign,Color color)
        {
            var crate=Module("Props/Crate_01",position,.95f,0,true,false,decor);
            crate.name="DG.Interactable."+id;
            crate.transform.position+=Vector3.up*.6f;
            foreach(Transform child in crate.transform) child.position-=Vector3.up*.6f;
            var interaction=crate.AddComponent<CampaignInteractable>(); interaction.id=id; interaction.title=title; interaction.range=3;
            Label(sign,position+Vector3.up*1.2f,180,color,.028f);
            Point(sign+" locator",position+Vector3.up*1.2f,color,1,2.8f,false);
        }

        static GameObject Module(string relative,Vector3 position,float span,float yaw,bool solid,bool horizontal,Transform parent)
        {
            return Art(Pack+relative+".prefab",position,span,yaw,0,solid,horizontal,parent,relative.StartsWith("Floors/"));
        }
        static GameObject Furniture(string relative,Vector3 position,float width,float yaw,float maxHeight)
        {
            return Art(Styled+relative+".prefab",position,width,yaw,maxHeight,true,false,decor,false);
        }
        static GameObject Art(string path,Vector3 position,float span,float yaw,float maxHeight,bool solid,bool horizontal,Transform parent,bool floor)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!prefab) throw new FileNotFoundException("Missing imported module: "+path);
            var root=new GameObject(Path.GetFileNameWithoutExtension(path)); root.transform.SetParent(parent,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            model.transform.localPosition=Vector3.zero; model.transform.localRotation=Quaternion.identity;
            foreach(var script in model.GetComponentsInChildren<MonoBehaviour>(true)) if(script) UnityEngine.Object.DestroyImmediate(script);
            foreach(var item in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(item.gameObject);
            foreach(var light in model.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
            foreach(var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach(var group in model.GetComponentsInChildren<LODGroup>(true)) BridgeVisualQuality.UseHighestDetail(group);
            var bounds=BoundsOf(root);
            if(!horizontal && bounds.size.z>bounds.size.x*1.2f) model.transform.localRotation=Quaternion.Euler(0,90,0);
            bounds=BoundsOf(root);
            float scale=span/Mathf.Max(.01f,horizontal ? Mathf.Max(bounds.size.x,bounds.size.z) : bounds.size.x);
            if(maxHeight>0) scale=Mathf.Min(scale,maxHeight/bounds.size.y);
            root.transform.localScale=Vector3.one*scale; bounds=BoundsOf(root);
            model.transform.position-=new Vector3(bounds.center.x,floor ? bounds.max.y : bounds.min.y,bounds.center.z);
            root.transform.rotation=Quaternion.Euler(0,yaw,0); root.transform.position=position;
            if(solid && !floor)
                foreach(var filter in model.GetComponentsInChildren<MeshFilter>())
                    if(filter.sharedMesh && filter.GetComponent<Renderer>() && filter.GetComponent<Renderer>().enabled)
                    { var collision=filter.gameObject.AddComponent<MeshCollider>(); collision.sharedMesh=filter.sharedMesh; }
            return root;
        }

        static Bounds BoundsOf(GameObject root)
        {
            var renderers=root.GetComponentsInChildren<Renderer>().Where(renderer=>renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            if(renderers.Length==0) throw new InvalidOperationException("Module has no visible mesh: "+root.name);
            var bounds=renderers[0].bounds; foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds); return bounds;
        }
        static void Box(string name,Vector3 position,Vector3 size,Transform parent)
        {
            var collider=new GameObject(name,typeof(BoxCollider)).GetComponent<BoxCollider>(); collider.transform.SetParent(parent,false);
            collider.transform.position=position; collider.size=size;
        }
        static void AccentSymbol(string relative,Vector3 position,float width,float yaw)
        { Module(relative,position,width,yaw,false,false,decor); }

        static void Label(string text,Vector3 position,float yaw,Color color,float size)
        {
            var mesh=new GameObject("Wayfinding / "+text,typeof(TextMesh)).GetComponent<TextMesh>(); mesh.transform.SetParent(signage,false);
            mesh.transform.position=position; mesh.transform.rotation=Quaternion.Euler(0,yaw+180,0);
            mesh.text=text; mesh.anchor=TextAnchor.MiddleCenter; mesh.alignment=TextAlignment.Center;
            mesh.fontSize=64; mesh.characterSize=size; mesh.color=color;
            mesh.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            string path=Output+"/Materials/WorldSign.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material)
            {
                var shader=Shader.Find("Doppelganger/World Sign");
                if(!shader) throw new InvalidOperationException("World Sign shader has not imported.");
                material=new Material(shader); AssetDatabase.CreateAsset(material,path);
            }
            mesh.GetComponent<MeshRenderer>().sharedMaterial=material;
            mesh.gameObject.AddComponent<CampaignWorldSign>();
        }
        static Light Point(string name,Vector3 position,Color color,float intensity,float range,bool shadows)
        {
            var lamp=new GameObject(name,typeof(Light)).GetComponent<Light>(); lamp.transform.SetParent(lights,false);
            lamp.transform.position=position; lamp.type=LightType.Point; lamp.range=range; lamp.intensity=intensity; lamp.color=color;
            lamp.shadows=shadows ? LightShadows.Soft : LightShadows.None; return lamp;
        }
        static void Lighting(int chapter,Camera camera)
        {
            RenderSettings.ambientMode=AmbientMode.Trilight; RenderSettings.fog=false;
            RenderSettings.ambientSkyColor=new Color(.2f,.25f,.32f); RenderSettings.ambientEquatorColor=new Color(.12f,.15f,.21f); RenderSettings.ambientGroundColor=new Color(.065f,.075f,.1f);
            RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/DoppelgangerGenerated/LearningArena/Materials/Sky.mat");
            var fill=Point("Soft ship interior bounce",new Vector3(0,5,0),new Color(.67f,.77f,1),.55f,100,false);
            fill.type=LightType.Directional; fill.transform.rotation=Quaternion.Euler(30,-30,0);
            if(chapter==0)
            {
                var sun=Point("Earth-lit bridge gallery",new Vector3(0,5,14),new Color(.62f,.78f,1),.8f,100,true);
                sun.type=LightType.Directional; sun.transform.rotation=Quaternion.Euler(30,165,0);
            }
            camera.allowHDR=true; camera.allowDynamicResolution=false;
            var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=true;
            data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality=AntialiasingQuality.High;
            string path=Output+"/EpisodeAtmosphere"+chapter+".asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(!profile)
            {
                profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,path);
                profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
                var bloom=profile.Add<Bloom>(true); bloom.intensity.Override(.18f); bloom.threshold.Override(1.25f);
                var grade=profile.Add<ColorAdjustments>(true); grade.postExposure.Override(.5f); grade.contrast.Override(6);
                profile.Add<Vignette>(true).intensity.Override(.12f);
                foreach(var component in profile.components) AssetDatabase.AddObjectToAsset(component,profile);
            }
            var volume=new GameObject("ACES / controlled bloom",typeof(Volume)).GetComponent<Volume>(); volume.isGlobal=true; volume.priority=10; volume.sharedProfile=profile;
        }

        static void FinishMaterials()
        {
            var glass=AssetDatabase.LoadAssetAtPath<Material>("Assets/DoppelgangerGenerated/Earth/BridgeGlass.mat");
            foreach(var renderer in geometry.GetComponentsInChildren<Renderer>().Concat(decor.GetComponentsInChildren<Renderer>()))
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var material=materials[i]; if(!material) continue;
                    if(glass && material.name.IndexOf("glass",StringComparison.OrdinalIgnoreCase)>=0) materials[i]=glass;
                    else if(AssetDatabase.GetAssetPath(material).StartsWith(Output+"/Materials/") && material.HasProperty("_Cull"))
                    { material.SetFloat("_Cull",0); EditorUtility.SetDirty(material); }
                }
                renderer.sharedMaterials=materials;
            }
        }
        static void Validate(Scene scene,CampaignGame game)
        {
            Physics.SyncTransforms();
            var points=game.enemySpawns.Select(item=>item.position).Prepend(game.player.transform.position).ToArray();
            foreach(var point in points)
            {
                if(!Physics.RaycastAll(point+Vector3.up,Vector3.down,2).Any(hit=>hit.collider.gameObject.scene==scene && hit.collider!=game.player))
                    throw new InvalidOperationException("No walkable deck at mission spawn "+point);
                var obstacles=Physics.OverlapCapsule(point+Vector3.up*.45f,point+Vector3.up*1.35f,.31f).Where(item=>!item.isTrigger && item.gameObject.scene==scene && item!=game.player && !item.transform.IsChildOf(game.player.transform)).ToArray();
                if(obstacles.Length>0) throw new InvalidOperationException("Mission spawn overlaps "+string.Join(", ",obstacles.Select(item=>item.name))+" at "+point);
            }
            if(game.chapter==0 && game.enemySpawns.Length!=0) throw new InvalidOperationException("Bridge must not contain an arena opponent.");
            var interactions=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<CampaignInteractable>()).ToArray();
            if(interactions.Length!=(game.chapter==0 ? 4 : game.chapter==1 ? 6 : 3)) throw new InvalidOperationException("Mission interaction count mismatch.");
            foreach(var item in interactions) if(!item.GetComponentsInChildren<Collider>().Any()) throw new InvalidOperationException("Interaction has no collider: "+item.id);
            ValidateRoutes(scene,game,interactions);
            Report.Add("Chapter "+game.chapter+": "+interactions.Length+" real terminal interactions, "+game.enemySpawns.Length+" valid enemy spawns; no ImitationArena components.");
        }

        static void ValidateRoutes(Scene scene,CampaignGame game,CampaignInteractable[] interactions)
        {
            // Flood the actual collision geometry, rather than assuming that a floor tile implies a clear route.
            const float spacing=.75f;
            int width=Mathf.CeilToInt((game.navigationMax.x-game.navigationMin.x)/spacing);
            int height=Mathf.CeilToInt((game.navigationMax.y-game.navigationMin.y)/spacing);
            var clear=new bool[width,height]; var reached=new bool[width,height];
            Func<int,int,Vector3> point=(x,z)=>new Vector3(game.navigationMin.x+(x+.5f)*spacing,.05f,game.navigationMin.y+(z+.5f)*spacing);
            for(int x=0;x<width;x++) for(int z=0;z<height;z++)
            {
                Vector3 pos=point(x,z);
                bool deck=Physics.RaycastAll(pos+Vector3.up*.25f,Vector3.down,1).Any(hit=>hit.collider.gameObject.scene==scene && hit.collider!=game.player && !hit.collider.isTrigger);
                if(!deck) continue;
                clear[x,z]=!Physics.OverlapCapsule(pos+Vector3.up*.4f,pos+Vector3.up*1.4f,.3f).Any(item=>!item.isTrigger && item.gameObject.scene==scene && item!=game.player && !item.transform.IsChildOf(game.player.transform));
            }
            int startX=Mathf.Clamp(Mathf.FloorToInt((game.player.transform.position.x-game.navigationMin.x)/spacing),0,width-1);
            int startZ=Mathf.Clamp(Mathf.FloorToInt((game.player.transform.position.z-game.navigationMin.y)/spacing),0,height-1);
            if(!clear[startX,startZ]) throw new InvalidOperationException("Player start has no free navigation cell.");
            var queue=new Queue<Vector2Int>(); queue.Enqueue(new Vector2Int(startX,startZ)); reached[startX,startZ]=true;
            while(queue.Count>0)
            {
                var at=queue.Dequeue();
                foreach(var delta in Neighbours)
                {
                    var next=at+delta;
                    if(next.x<0 || next.y<0 || next.x>=width || next.y>=height || reached[next.x,next.y] || !clear[next.x,next.y]) continue;
                    reached[next.x,next.y]=true; queue.Enqueue(next);
                }
            }
            foreach(var terminal in interactions)
            {
                bool accessible=false;
                for(int x=0;x<width && !accessible;x++) for(int z=0;z<height;z++)
                    if(reached[x,z] && Vector2.Distance(new Vector2(point(x,z).x,point(x,z).z),new Vector2(terminal.transform.position.x,terminal.transform.position.z))<terminal.range-.35f)
                    { accessible=true; break; }
                if(!accessible) throw new InvalidOperationException("No collision-free player route reaches terminal "+terminal.id);
            }
            int reachable=0; for(int x=0;x<width;x++) for(int z=0;z<height;z++) if(reached[x,z]) reachable++;
            Report.Add("Chapter "+game.chapter+": collision-grid route check passed; "+reachable+" reachable cells; every objective and supply is accessible.");
        }
    }
}
