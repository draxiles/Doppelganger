using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroSector.Campaign;

namespace ZeroSector.Editor
{
    public static class BoardingRouteAuthoring
    {
        const string Kit = "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Prefabs/";
        const string Styled = "Assets/Sci-Fi Styled Modular Pack/Prefabs/";
        const string Output = "Assets/DoppelgangerGenerated/BoardingRoute";
        const string Prefab = "Assets/ZeroSector/Resources/Doppelganger/BoardingRoute.prefab";
        const string MaterialLibrary = "Assets/DoppelgangerGenerated/Campaign";

        static readonly Color Ember = new(1, .35f, .075f);
        static readonly Color Cold = new(.39f, .78f, 1);
        static readonly Color WarmWhite = new(.96f, .86f, .68f);
        static readonly Color White = new(.83f, .92f, 1);

        public static void Prepare()
        {
            Directory.CreateDirectory(Output + "/Materials");
            Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
            AssetDatabase.Refresh();
            var previousScene = SceneManager.GetActiveScene();
            var authoringScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(authoringScene);
            try
            {
                var route = new GameObject("Boarding route / Decks A–17 and C–04").AddComponent<BoardingRoute>();
                var modules = Node(route.transform, "00 / Imported hull sections and equipment", Vector3.zero);
                var lockRoom = Node(route.transform, "01 / A–17 Transfer lock", Vector3.zero);
                var cargo = Node(route.transform, "02 / C–04 Cargo and security", Vector3.zero);
                var dressing = new ShipFinish();
                BuildLock(route, modules, lockRoom, dressing);
                BuildCargo(route, modules, cargo, dressing);
                FpsArenaGraphics.ConvertMaterials(modules.gameObject, MaterialLibrary);
                if (!PrefabUtility.SaveAsPrefabAsset(route.gameObject, Prefab))
                    throw new IOException("Could not save boarding route prefab: " + Prefab);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(authoringScene, true);
            }
        }

        static void BuildLock(BoardingRoute route, Transform modules, Transform room, ShipFinish f)
        {
            route.entry = Node(room, "Entry / from bridge ladder", new Vector3(0, .05f, -19.2f));
            Collision(room, "Pressure deck", new Vector3(0, -.22f, -13.5f), new Vector3(6.4f, .44f, 15));
            Collision(room, "Ceiling", new Vector3(0, 3.71f, -13.5f), new Vector3(6.4f, .5f, 15));
            Collision(room, "Pressure barrier / port", new Vector3(-3.2f, 1.85f, -13.5f), new Vector3(.24f, 3.7f, 15));
            Collision(room, "Pressure barrier / starboard", new Vector3(3.2f, 1.85f, -13.5f), new Vector3(.24f, 3.7f, 15));
            Collision(room, "Sealed ladder", new Vector3(0, 1.85f, -21), new Vector3(6.4f, 3.7f, .24f));
            Plate(room, "Port pressure hull", new Vector3(-3.54f, 1.74f, -13.5f), new Vector3(.12f, 3.62f, 15.1f), f.Dark);
            Plate(room, "Aft pressure hull", new Vector3(0, 1.74f, -21.24f), new Vector3(6.6f, 3.62f, .12f), f.Dark);

            for (int i = 0; i < 3; i++)
            {
                float z = -18.5f + 5 * i;
                Model(modules, Kit + "Floors/Floor_Third_02_Strip_2x6", new Vector3(0, -.16f, z), new Vector3(6.4f, .32f, 5));
                Model(modules, Kit + "Roofs/Roof_Squared_01_6x6", new Vector3(0, 3.65f, z), new Vector3(6.6f, .30f, 5));
                Model(modules, Kit + "Walls/Wall_Pipes_02_Large", new Vector3(-3.28f, 1.75f, z), new Vector3(5.02f, 3.5f, .44f), 90);
                if (i != 1)
                {
                    Model(modules, Kit + "Walls/Wall_Gear_01_Half", new Vector3(3.28f, 1.75f, z), new Vector3(5.02f, 3.5f, .44f), -90);
                    Plate(room, "Intact starboard pressure hull", new Vector3(3.54f, 1.74f, z), new Vector3(.12f, 3.62f, 5.02f), f.Dark);
                }
            }

            Model(modules, Kit + "Walls/Wall_Simple_03_Long", new Vector3(0, 1.75f, -21), new Vector3(6.5f, 3.5f, .38f));
            Plate(room, "Transfer lock signage", new Vector3(0, 2.85f, -6.44f), new Vector3(3.1f, .56f, .12f), f.Dark);
            Sign(room, "A–17   /   ПЕРЕХОДНЫЙ ШЛЮЗ", new Vector3(0, 2.91f, -6.51f), WarmWhite, .044f, f.Sign);
            route.pressureDisplay = Sign(room, "РАЗГЕРМЕТИЗАЦИЯ", new Vector3(0, 2.73f, -6.51f), Ember, .029f, f.Sign);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 2.72f;
                Plate(room, "Power runner / orange", new Vector3(x, .03f, -13.5f), new Vector3(.12f, .045f, 14.5f), f.Amber);
                Plate(room, "Overhead power carrier", new Vector3(side * 2.72f, 3.27f, -13.5f), new Vector3(.26f, .23f, 14.6f), f.Dark);
                Plate(room, "Inboard ceiling track", new Vector3(side * 1.12f, 3.44f, -13.5f), new Vector3(.09f, .055f, 12.7f), f.Amber);
                for (int i = 0; i < 4; i++)
                {
                    float z = -20.2f + 4.42f * i;
                    Plate(room, "Beveled lock-frame jamb", new Vector3(side * 3.05f, 1.42f, z), new Vector3(.20f, 2.84f, .25f), f.Alloy);
                    Strut(room, "Beveled upper brace", new Vector3(side * 3.05f, 2.75f, z), new Vector3(side * 2.22f, 3.53f, z), .20f, f.Alloy);
                }
            }

            var gate = Node(room, "A–17 Pressure gate", new Vector3(0, 1.24f, -6.14f));
            var door = gate.gameObject.AddComponent<CampaignDoor>();
            if (!MonoScript.FromMonoBehaviour(door)) throw new InvalidDataException("The A–17 gate controller must reference a serializable Unity script.");
            door.unlockId = BoardingRoute.SealId;
            door.openOffset = Vector3.up * 2.6f;
            FpsArenaGraphics.ConvertMaterials(Model(gate, Kit + "Doors/Door_Vert_01", Vector3.zero, new Vector3(2.7f, 2.48f, .28f)), MaterialLibrary);
            Collision(gate, "Pressure leaf collision", Vector3.zero, new Vector3(2.72f, 2.48f, .22f));
            Plate(gate, "Pressure gate lock indicator", new Vector3(0, .65f, -.18f), new Vector3(.84f, .059f, .05f), f.Amber);
            var seal = new Vector3(-2.28f, 0, -9.25f);
            route.sealConsoleDisplay = Terminal(room, BoardingRoute.SealId, "Восстановить герметичность шлюза", seal, "КОНТУР  /  A–17", WarmWhite, f, 12);
            route.pressureLight = Lamp(room, "Pressure indicator", new Vector3(0, 2.82f, -8.12f), Ember, 3.8f, 6);
            Lamp(room, "Lower lock / service bounce", new Vector3(-.6f, 2.85f, -18.75f), WarmWhite, 3, 6.4f);
            Lamp(room, "Radiation from the breach", new Vector3(2.9f, 2.78f, -13.5f), Ember, 5.4f, 7);
            Fixture(room, new Vector3(-.6f, 3.49f, -18.75f), .82f, f, false);
            Fixture(room, new Vector3(0, 3.49f, -8.12f), .84f, f, true);
            BuildBreach(route, modules, room, f);
        }

        static void BuildBreach(BoardingRoute route, Transform modules, Transform room, ShipFinish f)
        {
            var origin = new Vector3(3.42f, 1.72f, -13.5f);
            Model(modules, Kit + "Doors/DoorWay_01_Large", origin, new Vector3(5.08f, 3.55f, .75f), -90);
            var breach = Node(room, "Emergency field / exposed boarding collar", origin);
            Plate(breach, "Ablated floor panel", new Vector3(.68f, -1.49f, 0), new Vector3(1.4f, .16f, 4.8f), f.Dark);
            Plate(breach, "Boarding tube structural rail", new Vector3(1.48f, 1.39f, .12f), new Vector3(2.55f, .26f, .29f), f.Alloy, new Vector3(0, 6, -11));
            Strut(breach, "Torn cable bundle", new Vector3(.15f, 1.6f, -.84f), new Vector3(1.7f, .1f, -.47f), .075f, f.Dark);
            Strut(breach, "Torn cable bundle", new Vector3(.36f, 1.68f, -.71f), new Vector3(1.52f, .07f, -.39f), .047f, f.Amber);
            for (int i = 0; i < 9; i++)
            {
                float z = -1.92f + .49f * i;
                Plate(breach, "Jagged sheared hull", new Vector3(.43f + .17f * Mathf.Cos(i * 2), 1.09f + .23f * Mathf.Sin(i * 2.1f), z),
                    new Vector3(.61f, .82f + .31f * Mathf.Sin(i), .50f), i % 3 == 0 ? f.Dark : f.Alloy,
                    new Vector3(13 * Mathf.Sin(i), 24 * Mathf.Cos(i * 2), 17 * Mathf.Sin(i * 3)));
            }
            var sparks = Node(breach, "Electrical arcs at hull rupture", new Vector3(.86f, .52f, -.59f));
            Strut(sparks, "Incandescent exposed core", new Vector3(-.22f, .48f, -.06f), new Vector3(.12f, -.42f, .11f), .023f, f.Amber);
            Lamp(sparks, "Shorted power coupler", Vector3.zero, Ember, 2.2f, 3.2f);
            var emitter = Node(sparks, "Scattered sparks", Vector3.zero);
            emitter.localRotation = Quaternion.LookRotation(new Vector3(-.74f, -.55f, .16f));
            var particles = emitter.gameObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.duration = 3;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.21f, .58f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.008f, .025f);
            main.startColor = new ParticleSystem.MinMaxGradient(Ember, WarmWhite);
            main.gravityModifier = .24f;
            main.maxParticles = 24;
            var emission = particles.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(.25f, 7), new ParticleSystem.Burst(1.47f, 4) });
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 16;
            shape.radius = .06f;
            var streaks = particles.GetComponent<ParticleSystemRenderer>();
            streaks.renderMode = ParticleSystemRenderMode.Stretch;
            streaks.lengthScale = 2.4f;
            streaks.velocityScale = .18f;
            streaks.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ZeroSector/Resources/Doppelganger/PulseTrail.mat");
            route.breachSparks = sparks.gameObject;
            Plate(breach, "Field emitter status panel", new Vector3(-.34f, -.78f, 0), new Vector3(.05f, .30f, 2.4f), f.Dark);
            Sign(breach, "АВАРИЙНОЕ ПОЛЕ", new Vector3(-.371f, -.78f, 0), WarmWhite, .037f, f.Sign, 90);
        }

        static void BuildCargo(BoardingRoute route, Transform modules, Transform room, ShipFinish f)
        {
            Collision(room, "Wide load-bearing deck", new Vector3(0, -.24f, 5), new Vector3(22, .48f, 22));
            Collision(room, "Cargo hold ceiling", new Vector3(0, 6.53f, 5), new Vector3(22, .38f, 22));
            Collision(room, "Aft hold bulkhead", new Vector3(0, 3.25f, 16), new Vector3(22, 6.5f, .36f));
            Collision(room, "Header over transfer lock", new Vector3(0, 4.47f, -6), new Vector3(2.75f, 4, .3f));
            Plate(room, "Cargo hold aft pressure hull", new Vector3(0, 3.21f, 16.25f), new Vector3(22.4f, 6.5f, .12f), f.Dark);
            Plate(room, "Opaque bulkhead over pressure gate", new Vector3(0, 4.47f, -5.73f), new Vector3(3.52f, 4, .18f), f.Dark);
            for (int side = -1; side <= 1; side += 2)
            {
                Collision(room, "Hold side bulkhead", new Vector3(side * 11, 3.25f, 5), new Vector3(.28f, 6.5f, 22));
                Collision(room, "Forward blast bulkhead", new Vector3(side * 6.19f, 3.25f, -6), new Vector3(9.65f, 6.5f, .36f));
                Plate(room, "Cargo pressure hull / longitudinal", new Vector3(side * 11.27f, 3.21f, 5), new Vector3(.12f, 6.5f, 22.34f), f.Dark);
                Plate(room, "Armored wings beside the pressure gate", new Vector3(side * 6.19f, 3.21f, -5.73f), new Vector3(9.65f, 6.5f, .12f), f.Dark);
                Plate(room, "Sidewall intermediate belt", new Vector3(side * 10.75f, 3.02f, 5), new Vector3(.18f, .28f, 22.1f), f.Alloy);
                Model(modules, Kit + "Walls/Wall_Simple_02_Long", new Vector3(side * 6.2f, 3.18f, -6), new Vector3(9.67f, 6.36f, .42f));
                Model(modules, Kit + "Walls/Wall_Simple_03_Long", new Vector3(side * 5.5f, 3.18f, 16), new Vector3(11, 6.36f, .42f));
                for (int i = 0; i < 4; i++)
                {
                    float z = -3.25f + 5.5f * i;
                    Model(modules, Kit + "Walls/Wall_Gear_02_Half", new Vector3(side * 11, 1.42f, z), new Vector3(5.52f, 2.84f, .46f), 90);
                    Model(modules, Kit + "Walls/Wall_Simple_04_Long", new Vector3(side * 11, 4.48f, z), new Vector3(5.52f, 3.28f, .42f), 90);
                    Plate(room, "Heavy crane runway", new Vector3(side * 7.65f, 5.08f, z), new Vector3(.44f, .38f, 5.53f), f.Dark);
                    Plate(room, "Cargo roadway dashed marking", new Vector3(side * 1.32f, .026f, z), new Vector3(.095f, .024f, 3.6f), f.Ice);
                }
            }
            for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                var tile = new Vector3(-8.25f + 5.5f * i, -.18f, -3.25f + 5.5f * j);
                Model(modules, Kit + "Floors/Floor_Squared_02_6x6", tile, new Vector3(5.51f, .36f, 5.51f));
                Model(modules, Kit + "Roofs/Roof_Squared_02_6x6", tile + Vector3.up * 6.74f, new Vector3(5.52f, .36f, 5.52f));
            }
            Model(modules, Kit + "Doors/DoorWay_01_Large", new Vector3(0, 1.62f, -6.04f), new Vector3(3.5f, 3.26f, .45f));
            CargoStack(modules, room, new Vector3(-4.1f, 0, -.8f), 78, true, "07", f);
            CargoStack(modules, room, new Vector3(4.9f, 0, 1.8f), 0, false, "12", f);
            CargoStack(modules, room, new Vector3(-4.7f, 0, 7.3f), -8, false, "19", f);
            CargoStack(modules, room, new Vector3(5.6f, 0, 10.4f), -72, true, "28", f);
            CargoCrane(room, modules, f);

            Sign(room, "C–04   /   ГРУЗОВОЙ ОТСЕК", new Vector3(0, 4.12f, 15.62f), White, .085f, f.Sign);
            Sign(room, "ЖУРНАЛ   ›", new Vector3(6.4f, 2.7f, 9.4f), Cold, .05f, f.Sign);
            Plate(room, "Lift alcove crown", new Vector3(0, 3.55f, 15.23f), new Vector3(4.8f, .28f, .58f), f.Alloy);
            for (int side = -1; side <= 1; side += 2)
            {
                Plate(room, "Lift alcove vertical guide", new Vector3(side * 2.27f, 1.75f, 15.23f), new Vector3(.24f, 3.50f, .58f), f.Alloy);
                Strut(room, "Angled amber wayfinding at lift", new Vector3(side * 2.17f, 2.91f, 14.91f),
                    new Vector3(side * 1.78f, 3.44f, 14.91f), .054f, f.Amber);
            }
            route.boarderSpawns = new[]
            {
                Node(room, "Boarder / central aisle", new Vector3(.65f, .05f, 6.1f)),
                Node(room, "Boarder / port storage aisle", new Vector3(-8.1f, .05f, 9.8f))
            };
            foreach (var spawn in route.boarderSpawns) spawn.localRotation = Quaternion.Euler(0, 180, 0);
            route.cargoDisplay = Terminal(room, BoardingRoute.ManifestId, "Скопировать журнал корабля-нарушителя",
                new Vector3(7.95f, 0, 6.55f), "БОРТОВОЙ ЖУРНАЛ  /  C–04", White, f, -13);
            var lift = new Vector3(0, 0, 14.24f);
            Model(modules, Kit + "Doors/DoorWay_01_Large", lift + new Vector3(0, 1.52f, 1.2f), new Vector3(4.1f, 3.04f, .55f));
            Model(modules, Kit + "Doors/Door_Vert_01", lift + new Vector3(0, 1.35f, 1.23f), new Vector3(2.8f, 2.7f, .26f));
            Collision(room, "Lift doors", lift + new Vector3(0, 1.35f, 1.25f), new Vector3(2.8f, 2.7f, .26f));
            route.liftDisplay = Terminal(room, BoardingRoute.TransitId, "Вызвать лифт в инженерный отсек",
                lift + new Vector3(-2.06f, 0, .1f), "ЛИФТ  /  E–09", White, f, 14);
            Supply(room, "ammo_boarding", "Взять патроны из грузового ящика", new Vector3(-8.9f, 0, 1.58f), "БОЕПРИПАСЫ", f);
            Supply(room, "health_boarding", "Взять аптечку", new Vector3(8.93f, 0, -2.9f), "АПТЕЧКА", f);
            Lamp(room, "Loading lights / bow", new Vector3(0, 4.9f, -1.7f), Cold, 9.6f, 12.5f);
            Lamp(room, "Loading lights / central gantry", new Vector3(-5.8f, 4.05f, 4.6f), Cold, 7.7f, 10);
            Lamp(room, "Search pool / cargo control", new Vector3(7.75f, 3.2f, 5.95f), WarmWhite, 6.6f, 7);
            Lamp(room, "Lifts / emergency warm exit", new Vector3(0, 3.3f, 13.4f), WarmWhite, 6.2f, 9);
            Fixture(room, new Vector3(-2.55f, 6.31f, -1.7f), 1.7f, f, false);
            Fixture(room, new Vector3(2.55f, 6.31f, -1.7f), 1.7f, f, false);
            Fixture(room, new Vector3(-5.8f, 4.94f, 3.81f), 1.15f, f, false);
            Fixture(room, new Vector3(7.75f, 4.91f, 5.95f), 1.14f, f, true);
            Fixture(room, new Vector3(0, 3.42f, 15.1f), 1.28f, f, true);
        }

        static void CargoStack(Transform modules, Transform room, Vector3 floor, float yaw, bool tall, string label, ShipFinish f)
        {
            float height = tall ? 2.1f : 1.13f;
            var rotation = Quaternion.Euler(0, yaw, 0);
            Model(modules, Kit + "Props/Crate_01", floor + Vector3.up * height * .5f, new Vector3(3.6f, height, 1.9f), yaw);
            Collision(room, "Cargo cover " + label, floor + Vector3.up * height * .5f, new Vector3(3.55f, height, 1.86f), yaw);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 point = floor + rotation * new Vector3(side * 1.56f, height * .64f, -.995f);
                Plate(room, "Tie-down warning tag", point, new Vector3(.20f, .16f, .025f), f.Amber, new Vector3(0, yaw, 0));
            }
            Sign(room, label, floor + rotation * new Vector3(0, height * .68f, -.973f), WarmWhite, .047f, f.Sign, yaw);
        }

        static void CargoCrane(Transform room, Transform modules, ShipFinish f)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 7.62f;
                Plate(room, "Bridge crane motor housing", new Vector3(x, 4.99f, 3.95f), new Vector3(.88f, .78f, 1.68f), f.Alloy);
                Plate(room, "Machinery status marker", new Vector3(x, 5.04f, 3.09f), new Vector3(.47f, .085f, .033f), f.Amber);
            }
            for (int track = -1; track <= 1; track += 2)
            {
                Plate(room, "Suspended double-girder crane", new Vector3(0, 5.24f, 3.95f + track * .47f), new Vector3(15.4f, .52f, .27f), f.Alloy);
                Plate(room, "Girder safety strip", new Vector3(0, 5.02f, 3.95f + track * .61f), new Vector3(15.35f, .042f, .036f), f.Amber);
            }
            Plate(room, "Crane traverse trolley", new Vector3(-4.67f, 4.85f, 3.95f), new Vector3(2.24f, .41f, 1.43f), f.Dark);
            for (int side = -1; side <= 1; side += 2)
                Strut(room, "Hoist cable", new Vector3(-4.67f + side * .83f, 4.72f, 3.95f),
                    new Vector3(-4.67f + side * 1.22f, 3.75f, 3.95f), .045f, f.Dark);
            Model(modules, Kit + "Props/Crate_01", new Vector3(-4.67f, 3.13f, 3.95f), new Vector3(2.58f, 1.24f, 1.6f));
        }

        static void Fixture(Transform room, Vector3 point, float width, ShipFinish f, bool warm)
        {
            Plate(room, "Recessed work-light casing", point, new Vector3(width + .16f, .12f, .31f), f.Dark);
            for (int side = -1; side <= 1; side += 2)
                Plate(room, "Visible LED diffuser", point + new Vector3(0, -.067f, side * .068f),
                    new Vector3(width, .027f, .063f), warm ? f.Amber : f.Ice);
        }

        static TextMesh Terminal(Transform parent, string id, string title, Vector3 floor, string tag, Color color, ShipFinish f, float yaw)
        {
            var terminal = Node(parent, tag, floor);
            terminal.localRotation = Quaternion.Euler(0, yaw, 0);
            var target = Node(terminal, id, new Vector3(0, 1.1f, -.21f)).gameObject.AddComponent<CampaignInteractable>();
            target.id = id;
            target.title = title;
            target.range = 3.55f;
            FpsArenaGraphics.ConvertMaterials(Model(target.transform, Styled + "Decorative elements/computer_station",
                new Vector3(0, -.42f, .19f), new Vector3(1.46f, 1.36f, .83f), 180), MaterialLibrary);
            Collision(target.transform, "Terminal body", new Vector3(0, -.42f, .19f), new Vector3(1.43f, 1.36f, .8f));
            Plate(terminal, "Physical terminal display backing", new Vector3(0, 1.57f, -.32f), new Vector3(1.76f, .46f, .07f), f.Dark);
            Plate(terminal, "Terminal illuminated header", new Vector3(0, 1.786f, -.359f), new Vector3(1.73f, .024f, .017f), f.Ice);
            Sign(terminal, tag, new Vector3(0, 1.647f, -.365f), color, .031f, f.Sign);
            return Sign(terminal, "ДОСТУПЕН РУЧНОЙ РЕЖИМ", new Vector3(0, 1.49f, -.365f), color, .022f, f.Sign);
        }

        static void Supply(Transform parent, string id, string title, Vector3 floor, string tag, ShipFinish f)
        {
            var supply = Node(parent, id, floor + Vector3.up * .52f).gameObject.AddComponent<CampaignInteractable>();
            supply.id = id;
            supply.title = title;
            supply.range = 3;
            FpsArenaGraphics.ConvertMaterials(Model(supply.transform, Kit + "Props/Crate_01",
                new Vector3(0, -.21f, 0), new Vector3(.86f, .62f, .58f)), MaterialLibrary);
            Sign(supply.transform, tag, new Vector3(0, .24f, -.23f), White, .024f, f.Sign);
            Plate(supply.transform, "Supply locator", new Vector3(0, -.21f, -.296f), new Vector3(.42f, .058f, .014f), f.Ice);
        }

        static GameObject Model(Transform parent, string path, Vector3 center, Vector3 size, float yaw = 0)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab");
            if (!asset) throw new FileNotFoundException("Imported boarding module missing", path);
            var mount = Node(parent, Path.GetFileName(path), Vector3.zero);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, mount);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (var light in model.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour) UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (var animator in model.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
            foreach (var transform in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            foreach (var lod in model.GetComponentsInChildren<LODGroup>(true)) BridgeVisualQuality.UseHighestDetail(lod);
            var renderers = model.GetComponentsInChildren<Renderer>().Where(renderer => renderer.enabled).ToArray();
            if (renderers.Length == 0) throw new InvalidDataException("Boarding module has no visible renderer: " + path);
            var bounds = LocalBounds(mount, renderers[0]);
            foreach (var renderer in renderers) bounds.Encapsulate(LocalBounds(mount, renderer));
            Vector3 scale = new(size.x / Mathf.Max(.02f, bounds.size.x), size.y / Mathf.Max(.02f, bounds.size.y), size.z / Mathf.Max(.02f, bounds.size.z));
            model.transform.localScale = Vector3.Scale(model.transform.localScale, scale);
            model.transform.localPosition = -Vector3.Scale(bounds.center, scale);
            mount.SetLocalPositionAndRotation(center, Quaternion.Euler(0, yaw, 0));
            return mount.gameObject;
        }

        static Bounds LocalBounds(Transform frame, Renderer renderer)
        {
            Matrix4x4 toLocal = frame.worldToLocalMatrix * renderer.localToWorldMatrix;
            var bounds = renderer.localBounds;
            var extents = bounds.extents;
            var size = Abs(toLocal.MultiplyVector(Vector3.right * extents.x)) +
                Abs(toLocal.MultiplyVector(Vector3.up * extents.y)) +
                Abs(toLocal.MultiplyVector(Vector3.forward * extents.z));
            return new Bounds(toLocal.MultiplyPoint3x4(bounds.center), size * 2);
        }

        static Vector3 Abs(Vector3 vector) => new(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));

        static Transform Node(Transform parent, string name, Vector3 position)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false);
            result.localPosition = position;
            return result;
        }

        static void Collision(Transform parent, string name, Vector3 position, Vector3 size, float yaw = 0)
        {
            var node = Node(parent, name, position);
            node.localRotation = Quaternion.Euler(0, yaw, 0);
            node.gameObject.AddComponent<BoxCollider>().size = size;
        }

        static Transform Plate(Transform parent, string name, Vector3 center, Vector3 size, Material material, Vector3 euler = default)
        {
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = name;
            UnityEngine.Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.GetComponent<MeshRenderer>().sharedMaterial = material;
            plate.transform.SetParent(parent, false);
            plate.transform.SetLocalPositionAndRotation(center, Quaternion.Euler(euler));
            plate.transform.localScale = size;
            return plate.transform;
        }

        static void Strut(Transform parent, string name, Vector3 start, Vector3 end, float width, Material material)
        {
            var direction = end - start;
            var beam = Plate(parent, name, (start + end) * .5f, new Vector3(width, width, direction.magnitude), material);
            beam.localRotation = Quaternion.LookRotation(direction);
        }

        static TextMesh Sign(Transform parent, string label, Vector3 position, Color color, float size, Material material, float yaw = 0)
        {
            var node = Node(parent, label, position);
            node.localRotation = Quaternion.Euler(0, yaw, 0);
            var text = node.gameObject.AddComponent<TextMesh>();
            text.text = label;
            text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Doppelganger/Interface/GolosText-Bold.ttf");
            text.fontSize = 64;
            text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            node.GetComponent<MeshRenderer>().sharedMaterial = material;
            node.gameObject.AddComponent<CampaignWorldSign>();
            return text;
        }

        static Light Lamp(Transform parent, string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = Node(parent, name, position).gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        sealed class ShipFinish
        {
            public readonly Material Dark = Material("Anodized graphite", new Color(.075f, .092f, .12f), .53f);
            public readonly Material Alloy = Material("Worked hull alloy", new Color(.19f, .24f, .30f), .61f);
            public readonly Material Ice = Material("Ice-blue deck guides", new Color(.08f, .29f, .38f), .14f, new Color(.13f, .8f, 1.24f));
            public readonly Material Amber = Material("Amber emergency strips", new Color(.47f, .19f, .044f), .26f, new Color(1.7f, .48f, .075f));
            public readonly Material Sign = AssetDatabase.LoadAssetAtPath<Material>(MaterialLibrary + "/Materials/WorldSign.mat");

            static Material Material(string name, Color color, float metallic, Color emission = default)
            {
                string path = Output + "/Materials/" + name + ".mat";
                var result = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (result) return result;
                result = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                result.SetColor("_BaseColor", color);
                result.SetFloat("_Metallic", metallic);
                result.SetFloat("_Smoothness", .37f);
                if (emission.maxColorComponent > 0)
                {
                    result.EnableKeyword("_EMISSION");
                    result.SetColor("_EmissionColor", emission);
                }
                AssetDatabase.CreateAsset(result, path);
                return result;
            }
        }
    }
}
