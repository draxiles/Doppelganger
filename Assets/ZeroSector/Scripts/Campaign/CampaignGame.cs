using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ZeroSector.Campaign
{
    public sealed class CampaignGame : MonoBehaviour
    {
        public int chapter;
        public Camera eyes;
        public CharacterController player;
        public FirstPersonPresentation presentation;
        public CampaignHud hud;
        public GameObject enemyPrefab;
        public Transform[] enemySpawns=Array.Empty<Transform>();
        public Vector2 navigationMin=new(-24,-24),navigationMax=new(24,24);
        public CampaignNavigation Navigation { get; private set; }
        public readonly List<CampaignEnemy> Enemies=new();
        public MultimodalCommandInput Devices { get; private set; }
        public GameCommandController Commands { get; private set; }
        public CommanderEnergyShield ActiveShield { get; private set; }
        public CampaignCargo Cargo { get; private set; }
        public CampaignRadio Radio { get; private set; }
        public CampaignFlight Flight { get; private set; }
        public BoardingRoute Boarding { get; private set; }
        public bool OnBoardingRoute => Boarding && !CampaignSession.Has(BoardingRoute.TransitId);
        public PlayerMirrorTimeline Mirror { get; private set; }
        public Vector3 PlayerVelocity { get; private set; }
        public bool PlayerCrouched => crouch!=null && crouch.IsCrouching;
        public bool PrologueActive => prologue && prologue.Active;
        public bool Outside { get; private set; }
        public bool AwaitingEarth { get; private set; }
        public float Oxygen { get; private set; }=100;
        public bool Carrying => Cargo && Cargo.Held;
        const string ExteriorResource="Doppelganger/Exterior";
        static readonly Vector3 ExteriorOrigin=new(140,0,0);
        GameObject exterior;
        Transform dishPivot;
        Quaternion dishStart;
        Vector3 safePosition;
        bool introPlayed;
        Transform menuPlanet;
        Vector3 menuEyePosition;
        Quaternion menuEyeRotation;
        bool menuView;
        public Vector3 LastIncomingImpact { get; private set; }
        public bool MainMenu { get; private set; }=true;
        public bool Paused { get; private set; }
        public bool Dead { get; private set; }
        public bool Victory { get; private set; }
        public bool TerminalOpen { get; private set; }
        public bool BriefingOpen { get; private set; }
        public bool BriefingStartsOperation { get; private set; }
        public bool Running => !MainMenu && !Paused && !Dead && !Victory && !TerminalOpen && !transitioning && !PrologueActive && !BriefingOpen;
        public bool CoreExposed => CampaignSession.Has("coolantA") && CampaignSession.Has("coolantB");
        public float Health { get; private set; }=100;
        public float Shield { get; private set; }=60;
        public float Heat { get; private set; }
        public bool WeaponOverheated => Heat>=88;
        public float AimSpreadDegrees => presentation ? presentation.ShotSpreadDegrees : 0;
        public float Stamina { get; private set; }=100;
        public int Energy { get; private set; }
        public string CommanderOrder { get; private set; }="ПОДАВЛЕНИЕ";
        public string TacticalOrder { get; private set; }="ОБОРОНА";
        public string Notice { get; private set; }="";
        public string Subtitle { get; private set; }="";
        public string Prompt { get; private set; }="";
        public float ScreenFade { get; private set; }=1;
        public bool ShowDiagnostics { get; set; }
        public CampaignEnemy Boss => Enemies.FirstOrDefault(e=>e && e.boss);
        public int Alive => Enemies.Count(e=>e && !e.Dead);
        public string ChapterTitle => OnBoardingRoute
            ? (player && player.transform.position.z>=-6 ? "02 / ГРУЗОВАЯ ПАЛУБА" : "02 / ПРОБИТЫЙ ШЛЮЗ")
            : chapter==0 ? "01 / ЧУЖОЙ ГОЛОС" : chapter==1 ? "02 / ПАМЯТЬ КОРАБЛЯ" : "03 / ДВОЙНИК";
        public string Objective
        {
            get
            {
                if(chapter==0) return !CampaignSession.Has("log") ? "Прочитать журнал у места командира" :
                    !CampaignSession.Has("power") ? (Carrying ? "Установить аккумулятор в аварийный щиток" : "Взять аварийный аккумулятор со стойки") :
                    !CampaignSession.Has("nav") ? "Скорректировать ориентацию корабля на пульте" : "Спуститься к повреждённому шлюзу";
                if(OnBoardingRoute)
                {
                    if(!CampaignSession.Has(BoardingRoute.SealId)) return "Закрыть внешнюю створку и восстановить давление";
                    if(Alive>0) return $"Грузовая палуба · нейтрализовать абордажников  {Alive}";
                    return !CampaignSession.Has(BoardingRoute.ManifestId)
                        ? "Считать журнал абордажа с грузового терминала"
                        : "Вызвать служебный подъёмник в инженерный отсек";
                }
                if(chapter==1) return !CampaignSession.Has("relayA") || !CampaignSession.Has("relayB") ?
                    $"Восстановить два реле питания  {(CampaignSession.Has("relayA") ? 1 : 0)+(CampaignSession.Has("relayB") ? 1 : 0)} / 2\nОхранных роботов осталось: {Alive}" : Alive>0 ?
                    $"Нейтрализовать охрану: осталось {Alive}" : "Питание восстановлено. Найти выход к ядру";
                if(Outside) return AwaitingEarth ? "Сигнал передан. Дождаться ответа Земли" :
                    !CampaignSession.Has("antennaAlign") ? "Навести внешнюю антенну на Землю" : "Передать сигнал бедствия с передатчика";
                return !CoreExposed ? "Отключить оба контура охлаждения, чтобы снять поле Двойника" :
                Alive>0 ? "Победить Двойника. Следи за линией наведения" :
                    !CampaignSession.Has("core") ? "Снять блокировку связи с терминала ядра" : "Выйти через шлюз к внешней антенне";
            }
        }
        public static readonly string[] ScenePaths={"Assets/DoppelgangerGenerated/Campaign/01_CommandBridge.unity","Assets/DoppelgangerGenerated/Campaign/02_Engineering.unity","Assets/DoppelgangerGenerated/Campaign/03_DoppelgangerCore.unity"};
        static bool traveling, nextSceneStartsPrologue;
        CommanderCrouch crouch;
        RobotPresentation playerAvatar;
        CampaignPrologue prologue;
        Vector2 frameHeadOffset;
        CampaignInteractable[] interactions;
        CampaignInteractable focused;
        float pitch,vertical,cooldown,sinceHit=10,sampleClock,commanderClock,noticeUntil,subtitleUntil,travelClock;
        float boardingNavigationRefresh;
        int destination,hitCombo;
        bool transitioning,lastCrouch,firedSinceSample;
        Vector3 movement,spawn,engineeringEntry;
        Quaternion spawnRotation,engineeringRotation;
        Material beamMaterial;
        CampaignBulletMarks bulletMarks;
        AudioSource ambience;
        bool started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTravel() { traveling=false; nextSceneStartsPrologue=false; }

        void Start()
        {
            Application.targetFrameRate=60;
            if(!player || !eyes) { Debug.LogError("Campaign scene is missing player/camera."); enabled=false; return; }
            spawn=player.transform.position; spawnRotation=player.transform.rotation;
            safePosition=spawn;
            if(!presentation) presentation=GetComponent<FirstPersonPresentation>();
            presentation.Initialize(player,eyes); presentation.InstallCombatSidearm();
            crouch=new CommanderCrouch(player,presentation.eyeHeight);
            playerAvatar=CommanderAvatar.Attach(player.transform,true);
            if(chapter==2) Mirror=new PlayerMirrorTimeline();
            EnsureCommands();
            ActiveShield=GetComponent<CommanderEnergyShield>();
            if(!ActiveShield) ActiveShield=gameObject.AddComponent<CommanderEnergyShield>();
            ActiveShield.Initialize(this);
            Devices=GetComponent<MultimodalCommandInput>(); if(!Devices) Devices=gameObject.AddComponent<MultimodalCommandInput>();
            if(!hud) hud=GetComponent<CampaignHud>(); if(!hud) hud=gameObject.AddComponent<CampaignHud>(); hud.Initialize(this);
            Radio=gameObject.AddComponent<CampaignRadio>();
            // A Resources material carries its shader into the player build. Shader.Find alone does not.
            var pulseTemplate=Resources.Load<Material>("Doppelganger/PulseTrail");
            if(pulseTemplate && pulseTemplate.shader) beamMaterial=new Material(pulseTemplate);
            else Debug.LogWarning("Pulse trail material is missing; cosmetic trails are disabled.");
            if(chapter>0)
            {
                bulletMarks=gameObject.AddComponent<CampaignBulletMarks>();
                bulletMarks.Initialize(eyes);
            }
            var data=CampaignSession.Data;
            bool sameChapter=data.chapter==chapter;
            bool restoreCheckpoint=sameChapter && data.hasCheckpoint;
            data.chapter=chapter;
            Victory=traveling && sameChapter && data.finished;
            InitializeBoarding(restoreCheckpoint);
            InitializeMissionProps(restoreCheckpoint && data.snapshot);
            foreach(var door in FindObjectsByType<CampaignDoor>()) if(CampaignSession.Has(door.unlockId)) door.RestoreOpen();
            Navigation=new CampaignNavigation(navigationMin,navigationMax);
            Physics.SyncTransforms();
            if(restoreCheckpoint)
            {
                Vector3 position=data.checkpoint;
                if(!IsSafeCheckpoint(position)) position=Outside ? ExteriorOrigin+new Vector3(0,.04f,-5) : spawn;
                if(data.snapshot)
                {
                    Health=Mathf.Clamp(data.health,1,100); Shield=Mathf.Clamp(data.shield,0,60); Energy=Mathf.Clamp(data.energy,0,2);
                    pitch=Mathf.Clamp(data.pitch,-78,78);
                    presentation.RestoreAmmo(data.magazines,data.reserves,data.equipped);
                }
                PlacePlayer(position,data.snapshot ? Quaternion.Euler(0,data.yaw,0) : spawnRotation);
                presentation.ApplyLook(pitch);
            }
            else
            {
                data.hasCheckpoint=false;
                if(OnBoardingRoute)
                {
                    int pistol=presentation.weapons.FindIndex(slot=>slot.id==FirstPersonPresentation.ServicePistolId);
                    if(pistol>=0) presentation.Equip(pistol);
                }
            }
            CreateEnemies();
            RestoreSalvage(sameChapter);
            RefreshInteractions();
            foreach(var target in interactions)
                if(IsSupply(target.id) && CampaignSession.Has(target.id)) target.gameObject.SetActive(false);
            SetupAmbience();
            SetupReflection();
            gameObject.AddComponent<CampaignEnvironmentArt>().Initialize(this,interactions);
            bool playPrologue=chapter==0 && nextSceneStartsPrologue && traveling;
            nextSceneStartsPrologue=false;
            MainMenu=!traveling; traveling=false;
            if(MainMenu && chapter==0) OpenMenuView();
            if(playPrologue)
            {
                presentation.SetHolstered(true);
                if(ambience) ambience.Pause();
                prologue=CampaignPrologue.Begin(eyes,FinishPrologue);
                if(!PrologueActive) FinishPrologue();
            }
            SetCapture(Running);
            if(!MainMenu && !Victory && !PrologueActive && !BriefingOpen) BeginStory();
            started=true;
            Debug.Log($"Doppelganger campaign ready: chapter={chapter}; weapons={presentation.weapons.Count}; pulseTrail={beamMaterial!=null}");
        }

        void FinishPrologue()
        {
            prologue=null;
            ScreenFade=1;
            if(ambience) ambience.UnPause();
            BriefingStartsOperation=true;
            OpenBriefing();
        }

        void InitializeBoarding(bool restoreCheckpoint)
        {
            if(chapter!=1) return;
            engineeringEntry=spawn; engineeringRotation=spawnRotation;
            Boarding=BoardingRoute.Create(transform);
            if(!Boarding) return;
            navigationMax.x=Mathf.Max(navigationMax.x,70);
            navigationMin.y=Mathf.Min(navigationMin.y,-25);
            navigationMax.y=Mathf.Max(navigationMax.y,25);
            // A checkpoint made before the boarding route was added remains on the old deck.
            bool oldEngineering=restoreCheckpoint && CampaignSession.Data.checkpoint.x<25 &&
                !CampaignSession.Has(BoardingRoute.SealId);
            if(oldEngineering)
            {
                CampaignSession.Mark(BoardingRoute.SealId);
                CampaignSession.Mark(BoardingRoute.ManifestId);
                CampaignSession.Mark(BoardingRoute.TransitId);
            }
            if(!OnBoardingRoute) return;
            spawn=Boarding.Entry.position; spawnRotation=Boarding.Entry.rotation;
            PlacePlayer(spawn,spawnRotation);
        }

        void InitializeMissionProps(bool restoring)
        {
            if(chapter==0)
            {
                Flight=new CampaignFlight(this);
                var prefab=Resources.Load<GameObject>("Doppelganger/EmergencyBattery");
                if(prefab)
                {
                    Cargo=Instantiate(prefab,new Vector3(-7.3f,.71f,-4.1f),Quaternion.Euler(0,24,0)).GetComponent<CampaignCargo>();
                    Cargo.Initialize(this,restoring && CampaignSession.Data.cargoHeld,CampaignSession.Has("power"));
                    var dock=Resources.Load<GameObject>("Doppelganger/BatteryDock");
                    if(dock) Instantiate(dock,new Vector3(-7.3f,0,-4.1f),Quaternion.identity);
                }
            }
            if(chapter!=2) return;
            var exteriorPrefab=Resources.Load<GameObject>(ExteriorResource);
            if(!exteriorPrefab) return;
            exterior=Instantiate(exteriorPrefab,ExteriorOrigin,Quaternion.identity);
            dishPivot=exterior.GetComponentsInChildren<Transform>(true).FirstOrDefault(item=>item.name=="DishPivot");
            if(dishPivot) dishStart=dishPivot.localRotation;
            CreateAnchor("eva","Открыть внешний шлюз",new Vector3(0,1.25f,-12.8f));
            CreateAnchor("evaReturn","Вернуться внутрь и пополнить кислород",ExteriorOrigin+new Vector3(0,1.25f,-6.65f),exterior.transform);
            SetExterior(restoring && CampaignSession.Data.outside && CampaignSession.Has("core"));
            if(Outside) Oxygen=Mathf.Clamp(CampaignSession.Data.oxygen,12,100);
        }

        static void CreateAnchor(string id,string title,Vector3 position,Transform parent=null)
        {
            var node=new GameObject("DG.Interactable."+id,typeof(CampaignInteractable));
            if(parent) node.transform.SetParent(parent,false);
            node.transform.position=position;
            var target=node.GetComponent<CampaignInteractable>(); target.id=id; target.title=title;
        }

        void RestoreSalvage(bool sameChapter)
        {
            if(!sameChapter || chapter!=1) return;
            int count=enemySpawns.Length+(Boarding ? Boarding.BoarderSpawns.Length : 0);
            for(int i=0;i<count;i++)
            {
                var origin=i<enemySpawns.Length ? enemySpawns[i] : Boarding.BoarderSpawns[i-enemySpawns.Length];
                if(!origin || !CampaignSession.Has("enemy"+i) || CampaignSession.Has("loot"+i)) continue;
                var saved=CampaignSession.Data.salvage?.FirstOrDefault(item=>item!=null && item.number==i);
                var position=saved!=null && CampaignSession.Finite(saved.position) ? saved.position : origin.position+Vector3.up*.42f;
                var prefab=Resources.Load<GameObject>("Doppelganger/Salvage");
                if(prefab)
                {
                    var drop=Instantiate(prefab,position,Quaternion.identity).GetComponent<CampaignInteractable>();
                    drop.id="loot"+i; drop.title="Забрать припасы охраны";
                }
            }
        }
        void RefreshInteractions() { interactions=FindObjectsByType<CampaignInteractable>(); }
        static bool IsSupply(string id) => id.StartsWith("ammo",StringComparison.Ordinal) || id.StartsWith("health",StringComparison.Ordinal);
        void OpenMenuView()
        {
            if(menuView) return;
            menuPlanet=GameObject.Find("Earth / satellite surface, clouds and atmosphere")?.transform;
            if(!menuPlanet) return;
            menuView=true; menuEyePosition=eyes.transform.localPosition; menuEyeRotation=eyes.transform.localRotation;
            presentation.SetHolstered(true); presentation.ExternalCamera=true; UpdateMenuView();
        }
        void UpdateMenuView()
        {
            if(!menuView || !menuPlanet) return;
            float t=Time.unscaledTime;
            var position=new Vector3(Mathf.Sin(t*.12f)*7,12+Mathf.Sin(t*.09f)*2.3f,24);
            var towards=Quaternion.LookRotation(menuPlanet.position-position,Vector3.up);
            eyes.transform.SetPositionAndRotation(position,towards*Quaternion.Euler(-1,-19,Mathf.Sin(t*.14f)*.8f));
        }
        void CloseMenuView()
        {
            if(!menuView) return;
            menuView=false; eyes.transform.localPosition=menuEyePosition; eyes.transform.localRotation=menuEyeRotation;
            presentation.ExternalCamera=false; presentation.SetHolstered(Carrying || Outside);
        }

        void CreateEnemies()
        {
            if(chapter==0) return;
            if(!enemyPrefab) { Notify("Модель ADAM не назначена в сцене. Нужна сборка эпизодов."); return; }
            var spawns=OnBoardingRoute ? Boarding.BoarderSpawns : enemySpawns;
            for(int i=0;i<spawns.Length;i++)
            {
                int id=OnBoardingRoute ? enemySpawns.Length+i : i;
                if(!spawns[i] || CampaignSession.Has("enemy"+id)) continue;
                var root=new GameObject(chapter==2 ? "ADAM / ДВОЙНИК" : OnBoardingRoute ? "ШТУРМОВАЯ ГРУППА "+(i+1) : "ADAM / ОХРАНА "+(i+1));
                root.transform.SetPositionAndRotation(spawns[i].position,spawns[i].rotation);
                if(chapter!=2 || !CommanderAvatar.Attach(root.transform,false)) Instantiate(enemyPrefab,root.transform,false);
                var enemy=root.AddComponent<CampaignEnemy>(); enemy.Initialize(this,chapter==2,id); Enemies.Add(enemy);
            }
        }
        void BeginStory()
        {
            if(introPlayed) return;
            introPlayed=true;
            if(chapter==0 && !CampaignSession.Has("log")) Radio.Play("bridge_intro");
            else if(OnBoardingRoute) Notify("Абордажный катер пробил шлюз. П-7 готов. X — достать пистолет.",7);
            else if(chapter==1 && !CampaignSession.Has("relayA") && !CampaignSession.Has("relayB")) Radio.Play("engineering_intro");
            else if(chapter==2 && !CampaignSession.Has("coolantA") && !CampaignSession.Has("coolantB")) Radio.Play("core_intro");
            if(Outside) Radio.Play("eva_outside");
            if(chapter==0)
            {
                presentation.SetHolstered(true);
                Notify("E — действие   ·   F8 — камера   ·   F7 — «Гера, проверь связь»",6);
            }
        }

        void Update()
        {
            if(!started || PrologueActive) return;
            float dt=Mathf.Min(Time.deltaTime,.05f);
            if(transitioning)
            {
                Commands.Clear();
                travelClock-=Time.unscaledDeltaTime; ScreenFade=1-Mathf.Clamp01(travelClock/(destination<0 ? .65f : 1));
                if(travelClock<=0)
                {
                    if(destination==-3) CompleteBoardingTransfer();
                    else if(destination<0) CompleteAirlock(destination==-1);
                    else { traveling=true; SceneManager.LoadScene(ScenePaths[destination]); }
                }
                return;
            }
            ScreenFade=Mathf.MoveTowards(ScreenFade,0,Time.unscaledDeltaTime*1.8f);
            frameHeadOffset=Running ? Devices.HeadViewOffset : Vector2.zero;
            presentation.SetHeadViewOffset(frameHeadOffset);
            if(MainMenu) UpdateMenuView();
            if(Time.unscaledTime>noticeUntil) Notice="";
            if(Time.unscaledTime>subtitleUntil) Subtitle="";
            var keys=Keyboard.current; var mouse=Mouse.current;
            if(BriefingOpen)
            {
                Radio.Tick(0);
                Commands.Clear();
                presentation.SetInput(pitch,Vector2.zero,0,false,crouch.IsCrouching,false,false,false);
                if(keys!=null && (keys.escapeKey.wasPressedThisFrame || keys.enterKey.wasPressedThisFrame ||
                    keys.numpadEnterKey.wasPressedThisFrame || keys.f1Key.wasPressedThisFrame))
                    CloseBriefing();
                return;
            }
            if(keys!=null && keys.f1Key.wasPressedThisFrame && OpenBriefing()) return;
            Radio.Tick(!MainMenu && !Paused ? dt : 0);
            if(AwaitingEarth && !Radio.IsPlaying && !Paused) FinishCampaign();
            if(keys!=null)
            {
                if(keys.escapeKey.wasPressedThisFrame)
                {
                    if(TerminalOpen) CancelTerminal();
                    else if(!MainMenu && !Dead && !Victory) SetPaused(!Paused);
                }
                if(keys.f7Key.wasPressedThisFrame) Devices.ToggleVoice();
                if(keys.f8Key.wasPressedThisFrame) Devices.ToggleCamera();
                if(keys.hKey.wasPressedThisFrame) ShowDiagnostics=!ShowDiagnostics;
                if(keys.f5Key.wasPressedThisFrame && Running) SaveCheckpoint();
                if(keys.f6Key.wasPressedThisFrame) ExportReport();
                if(keys.tabKey.wasPressedThisFrame && !MainMenu && !Paused) Radio.Skip();
            }
            if(Flight!=null && Flight.Active)
            {
                Vector3 input=keys==null ? Vector3.zero : new Vector3(
                    Axis(keys.wKey.isPressed,keys.sKey.isPressed),Axis(keys.dKey.isPressed,keys.aKey.isPressed),
                    Axis(keys.eKey.isPressed,keys.qKey.isPressed));
                Flight.Tick(input,dt);
                if(keys!=null && keys.spaceKey.wasPressedThisFrame) CloseTerminal(Energy);
            }
            if(!Running)
            {
                Commands.Clear();
                presentation.SetInput(pitch,Vector2.zero,0,false,crouch.IsCrouching,false,false,false); return;
            }
            CampaignSession.Data.seconds+=dt;
            var echo=Boss;
            bool recording=echo && !echo.Dead && !Outside;
            if(recording) Mirror.Advance(Time.deltaTime);
            Vector3 previousPlayerPosition=player.transform.position;
            int previousShots=CampaignSession.Data.shots;
            ApplyPlayerInput(dt,keys,mouse);
            if(!Running) { Commands.Clear(); return; }
            Vector3 playerStep=player.transform.position-previousPlayerPosition;
            PlayerVelocity=Vector3.ProjectOnPlane(playerStep,Vector3.up)/Mathf.Max(.001f,dt);
            if(recording && echo.IsMirroring)
            {
                Vector3 facing=Vector3.ProjectOnPlane(eyes.transform.forward,Vector3.up).normalized;
                Mirror.RecordPose(new PlayerMirrorTimeline.Pose(playerStep,facing,eyes.transform.forward,
                    crouch.IsCrouching,player.isGrounded,presentation.IsSprinting));
            }
            if(playerAvatar)
            {
                playerAvatar.SetAimTarget(eyes.transform.position+eyes.transform.forward*12,!Outside && !presentation.IsSprinting);
                playerAvatar.Tick(playerStep/Mathf.Max(.001f,dt),crouch.IsCrouching,CampaignSession.Data.shots>previousShots,false,false,presentation.IsSprinting);
            }
            if(player.isGrounded && player.transform.position.y>=-.5f) safePosition=player.transform.position;
            if(Cargo) Cargo.Tick(dt);
            if(Outside) TickExterior(dt);
            if(boardingNavigationRefresh>0 && (boardingNavigationRefresh-=dt)<=0)
                Navigation=new CampaignNavigation(navigationMin,navigationMax);
            commanderClock-=dt;
            if(commanderClock<=0)
            {
                commanderClock=2;
                // Independent utility commander directs security based on state and player habits.
                CommanderOrder=Enemies.Any(e=>e && !e.Dead && e.health<22) ? "ОТСТУПЛЕНИЕ" :
                    movement.sqrMagnitude<.1f || CampaignSession.Data.crouches>3 ? "ОБХОД" : "ПОДАВЛЕНИЕ";
            }
            foreach(var enemy in Enemies) if(enemy) enemy.Tick(dt);
            sampleClock-=dt;
            if(chapter==1 && sampleClock<=0)
            {
                sampleClock=.2f;
                var enemy=ClosestEnemy();
                if(enemy && Vector3.Distance(enemy.transform.position,player.transform.position)<30)
                    CampaignSession.Learn(Observe(enemy,true),Classify(movement,enemy.transform.position-player.transform.position),firedSinceSample,Energy,crouch.IsCrouching);
                firedSinceSample=false;
            }
            if(player.transform.position.y<-8)
            {
                if(Outside) EmergencyReel();
                else DamagePlayer(1000);
            }
        }

        static int Axis(bool positive,bool negative) => (positive ? 1 : 0)-(negative ? 1 : 0);

        public bool CheckBridgeLink()
        {
            if(chapter!=0 || MainMenu || Paused || Dead || Victory || transitioning || !Radio) return false;
            var status=CampaignSession.Has("power") ? CampaignRadio.LinkStatus.RelaysOffline : CampaignRadio.LinkStatus.NoPower;
            return Radio.CheckLink(status);
        }

        public bool DrawPistol()
        {
            if(!Running || Carrying || Outside || !presentation || presentation.IsSwitching) return false;
            if(chapter>0)
            {
                int index=presentation.weapons.FindIndex(slot=>slot.id==FirstPersonPresentation.ServicePistolId);
                return index>=0 && (index==presentation.equippedIndex || presentation.RequestEquip(index));
            }
            presentation.SetHolstered(false);
            if(!presentation.ShowBridgeSidearm()) return false;
            SetEnergy(0);
            Notify("Пистолет извлечён. На мостике включён предохранитель.",3);
            return true;
        }

        public bool DrawRifle()
        {
            if(!Running || Carrying || Outside || !presentation || presentation.IsSwitching) return false;
            int rifle=presentation.weapons.FindIndex(slot=>slot.id=="UnityFpsSample.Terraformer");
            if(rifle<0) return false;
            presentation.HideBridgeSidearm();
            presentation.SetHolstered(false);
            presentation.RequestEquip(rifle);
            if(chapter==0)
            {
                SetEnergy(0);
                Notify("Автомат извлечён. На мостике включён предохранитель.",3);
            }
            return true;
        }

        // Keyboard, speech and camera all converge here, preserving grounded jump,
        // collision, stamina, headroom, fire rate and the real weapon animation states.
        void ApplyPlayerInput(float dt,Keyboard keys,Mouse mouse)
        {
            SetCapture(true);
            float sensitivity=.075f*Mathf.Lerp(1,.67f,presentation.AimAmount);
            Vector2 look=mouse!=null ? mouse.delta.ReadValue()*sensitivity : Vector2.zero;
            player.transform.Rotate(0,look.x,0); pitch=Mathf.Clamp(pitch-look.y,-78,78); presentation.ApplyLook(pitch);
            crouch.Tick((keys!=null && CommanderCrouch.ReadInput(keys)) || Commands.WantsCrouch,dt);
            if(crouch.IsCrouching && !lastCrouch) CampaignSession.Data.crouches++;
            lastCrouch=crouch.IsCrouching;
            // Always drain one-shot requests even when a physical key arrived in the
            // same frame, so the same action cannot leak into the following frame.
            bool commandWeapon=Commands.Consume("weapon"),commandShield=Commands.Consume("shield"),commandSwitch=Commands.Consume("switch"),commandReload=Commands.Consume("reload");
            bool commandInspect=Commands.Consume("inspect"),commandJump=Commands.Consume("jump"),commandInteract=Commands.Consume("interact");
            bool handsFree=!Carrying && !Outside;
            float wheel=mouse!=null ? mouse.scroll.ReadValue().y : 0;
            if(chapter==0 && handsFree)
            {
                if(commandShield || (keys!=null && keys.digit2Key.wasPressedThisFrame)) presentation.SetHolstered(true);
                else if(commandWeapon || commandSwitch || Mathf.Abs(wheel)>.1f ||
                    (keys!=null && (keys.digit1Key.wasPressedThisFrame || keys.qKey.wasPressedThisFrame)))
                {
                    presentation.HideBridgeSidearm();
                    presentation.SetHolstered(false);
                }
            }
            if(handsFree && ((keys!=null && keys.digit1Key.wasPressedThisFrame) || commandWeapon)) SetEnergy(0);
            if(handsFree && ((keys!=null && keys.digit2Key.wasPressedThisFrame) || commandShield)) SetEnergy(1);
            if(handsFree && keys!=null && keys.digit3Key.wasPressedThisFrame) SetEnergy(2);
            if(handsFree && keys!=null && keys.xKey.wasPressedThisFrame) DrawPistol();
            else if(handsFree && ((keys!=null && keys.qKey.wasPressedThisFrame) || commandSwitch)) presentation.RequestEquip(presentation.equippedIndex+1);
            else if(handsFree && Mathf.Abs(wheel)>.1f) presentation.RequestEquip(presentation.equippedIndex+(wheel>0 ? -1 : 1));
            if(handsFree && ((keys!=null && keys.rKey.wasPressedThisFrame) || commandReload)) presentation.BeginReload();
            if(Carrying && keys!=null && keys.gKey.wasPressedThisFrame && !Cargo.Drop()) Notify("Отойди от стены, чтобы положить аккумулятор.",2.5f);
            float x=keys!=null ? (keys.dKey.isPressed ? 1 : 0)-(keys.aKey.isPressed ? 1 : 0) : 0;
            float z=keys!=null ? (keys.wKey.isPressed ? 1 : 0)-(keys.sKey.isPressed ? 1 : 0) : 0;
            bool physicalMovement=keys!=null && (keys.wKey.isPressed || keys.sKey.isPressed || keys.aKey.isPressed || keys.dKey.isPressed);
            if(physicalMovement) Commands.StopMotion();
            else { Vector2 remote=Commands.Movement; x=remote.x; z=remote.y; }
            movement=Quaternion.Euler(0,frameHeadOffset.x,0)*player.transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(x,0,z),1));
            bool aim=handsFree && ((mouse!=null && mouse.rightButton.isPressed) || Commands.WantsAim);
            bool physicalFire=mouse!=null && mouse.leftButton.isPressed;
            bool remoteFire=Commands.Consume("fire");
            bool trigger=handsFree && chapter>0 && (physicalFire || remoteFire);
            if((remoteFire || (mouse!=null && mouse.leftButton.wasPressedThisFrame)) && (!handsFree || chapter==0))
                Notify(Outside ? "Снаружи оружие закреплено. Используй инструменты антенны." :
                    Carrying ? "Руки заняты аккумулятором." : "На мостике оружие на предохранителе.",2.4f);
            bool sprint=!Carrying && !Outside && ((keys!=null && keys.leftShiftKey.isPressed) || Commands.WantsSprint) && z>0 && Stamina>1 && !crouch.IsCrouching && !aim && !trigger && !presentation.IsReloading;
            Stamina=Mathf.Clamp(Stamina+dt*(sprint && movement.sqrMagnitude>.1f ? -22 : 16),0,100);
            bool inspect=handsFree && ((keys!=null && keys.fKey.wasPressedThisFrame) || commandInspect);
            presentation.SetInput(pitch,look,movement.magnitude,sprint,crouch.IsCrouching,aim,inspect,true);
            float speed=(Energy==2 ? 5.6f : 4.2f)*(Outside ? .76f : Carrying ? .8f : 1)*(crouch.IsCrouching ? .5f : sprint ? 1.55f : aim ? .65f : 1);
            if(player.isGrounded && vertical<0) vertical=-2;
            bool jump=(keys!=null && keys.spaceKey.wasPressedThisFrame) || commandJump;
            if(jump && player.isGrounded && !crouch.IsCrouching && !Carrying) vertical=Outside ? 3.3f : 5.2f;
            vertical-=(Outside ? 7.6f : 18)*dt; player.Move((movement*speed+Vector3.up*vertical)*dt);
            cooldown-=dt; sinceHit+=dt; Heat=Mathf.Max(0,Heat-dt*(Energy==0 ? 19 : 26));
            if(sinceHit>3) Shield=Mathf.Min(60,Shield+dt*(Energy==1 ? 15 : 5));
            bool triggerEdge=remoteFire || (mouse!=null && mouse.leftButton.wasPressedThisFrame);
            if(triggerEdge && presentation.IsInspecting) presentation.ResetView();
            if(trigger && presentation.MagazineAmmo==0 && !presentation.IsReloading && !presentation.IsSwitching)
                presentation.BeginReload();
            if(trigger && presentation.Equipped!=null && presentation.CanFire && cooldown<=0 && !WeaponOverheated && chapter>0 &&
                (presentation.Equipped.automatic || triggerEdge)) Fire();
            FocusInteraction();
            if(((keys!=null && keys.eKey.wasPressedThisFrame) || commandInteract) && focused) Interact(focused);
        }

        void Fire()
        {
            if(!presentation.TryConsumeRound()) return;
            var weapon=presentation.Equipped;
            var echo=Boss;
            if(echo && !echo.Dead)
            {
                echo.ObservePlayerShot();
                if(echo.AcceptsMirrorShot) Mirror.RecordShot(player.transform.TransformPoint(player.center),weapon.damage);
            }
            cooldown=weapon.interval*(Energy==0 ? .7f : 1); Heat+=weapon.heat; CampaignSession.Data.shots++; firedSinceSample=true;
            Vector3 direction=presentation.ShotDirection();
            Vector3 end=eyes.transform.position+direction*90; bool hitEnemy=false;
            bool pistol=weapon.id==FirstPersonPresentation.ServicePistolId;
            if(Physics.Raycast(eyes.transform.position,direction,out var hit,90,~0,QueryTriggerInteraction.Ignore))
            {
                end=hit.point; var target=hit.collider.GetComponentInParent<CampaignEnemy>();
                if(target && !target.Dead)
                {
                    hitEnemy=true; CampaignSession.Data.hits++; hitCombo++;
                    float damage=weapon.damage*(hit.point.y-target.transform.position.y>1.45f && !target.Crouched ? 1.5f : 1);
                    float healthBefore=target.health;
                    target.Damage(damage);
                    if(hud) hud.RegisterWeaponImpact(target.Dead,target.health>=healthBefore);
                }
                if(!target) MarkBulletHit(hit,pistol ? .12f : weapon.interval>=.45f ? .19f : .145f);
                Impact(hit.point,hit.normal,hitEnemy);
            }
            presentation.OnShot(hitEnemy);
            Color pulse=CampaignSession.Data.alternateColor ? new Color(1,.5f,.15f) : pistol ? new Color(.88f,.81f,.63f) : Color.cyan;
            Beam(presentation.MuzzlePosition,end,pulse);
        }
        public void DamagePlayer(float amount)
        {
            if(!Running || Dead) return;
            ApplyPlayerDamage(Mathf.Max(0,amount));
        }
        public void DamagePlayer(float amount,Vector3 source,Vector3 hitPoint)
        {
            ResolveIncomingShot(amount,source,hitPoint,true);
        }
        public Vector3 ResolveIncomingShot(float amount,Vector3 source,Vector3 hitPoint,bool hitsPlayer)
        {
            LastIncomingImpact=hitPoint;
            if(!Running || Dead || amount<=0) return hitPoint;
            float absorbed=ActiveShield ? ActiveShield.TryAbsorb(amount,source,hitPoint,out _) : 0;
            if(absorbed>0)
            {
                sinceHit=0;
                Shield=Mathf.Max(0,Shield-absorbed); LastIncomingImpact=ActiveShield.LastImpact;
                if(Shield<=0) Notify("Щит разряжен. Укройся на три секунды для восстановления.",4);
            }
            if(hitsPlayer && amount>absorbed) ApplyPlayerDamage(amount-absorbed);
            return LastIncomingImpact;
        }
        void ApplyPlayerDamage(float amount)
        {
            sinceHit=0; Health=Mathf.Max(0,Health-amount); if(presentation) presentation.OnDamage();
            if(Health<=0) { Dead=true; SetCapture(false); Say("СИСТЕМА","Сигнал командира потерян. Можно вернуться к контрольной точке.",60); }
        }
        public void EnemyKilled(CampaignEnemy enemy)
        {
            CampaignSession.Mark("enemy"+enemy.number); CampaignSession.Data.kills++; CampaignSession.Data.credits+=25;
            if(enemy.boss) Radio.Play("adam_down",true);
            else if(OnBoardingRoute)
                Notify(Alive>0 ? $"Абордажник отключён. В грузовом отсеке осталось: {Alive}." :
                    "Грузовая палуба чиста. Считай журнал C–04.",5);
            else
            {
                Notify($"Охрана нейтрализована. Осталось: {Mathf.Max(0,Alive)}. На корпусе есть припасы.");
                if(Alive==0) Radio.Play("engineering_clear");
            }
            RefreshInteractions();
        }
        public float[] Observe(CampaignEnemy enemy,bool human)
        {
            Vector3 origin=human ? player.transform.position : enemy.transform.position,target=human ? enemy.transform.position : player.transform.position;
            Vector3 forward=target-origin; forward.y=0; float distance=forward.magnitude; forward=forward.normalized;
            Vector3 side=Vector3.Cross(Vector3.up,forward);
            var state=new float[12]; state[0]=Mathf.Clamp01(distance/30);
            state[1]=human ? Health/100 : enemy.health/(enemy.boss ? CampaignEnemy.BossMaxHealth : 65);
            state[2]=human ? Shield/60 : enemy.shield/(enemy.boss ? 150 : 25); state[3]=human ? Heat/100 : enemy.heat/100;
            state[4]=human ? enemy.health/(enemy.boss ? CampaignEnemy.BossMaxHealth : 65) : Health/100;
            state[5]=human ? enemy.shield/(enemy.boss ? 150 : 25) : Shield/60;
            var directions=new[]{forward,-forward,-side,side};
            for(int i=0;i<4;i++) state[6+i]=Physics.Raycast(origin+Vector3.up*.7f,directions[i],out var hit,3,~0,QueryTriggerInteraction.Ignore) ? Mathf.Clamp01(hit.distance/3) : 1;
            state[10]=Mathf.Clamp01(1-(human ? sinceHit : enemy.sinceHit)/2);
            state[11]=enemy.VisibleTo(eyes.transform.position) ? 1 : 0; return state;
        }
        static int Classify(Vector3 direction,Vector3 toward)
        {
            if(direction.sqrMagnitude<.04f) return 0;
            toward.y=0; toward.Normalize(); var side=Vector3.Cross(Vector3.up,toward);
            float forward=Vector3.Dot(direction,toward),right=Vector3.Dot(direction,side);
            return Mathf.Abs(forward)>Mathf.Abs(right) ? (forward>0 ? 1 : 2) : right>0 ? 4 : 3;
        }
        CampaignEnemy ClosestEnemy() => Enemies.Where(e=>e && !e.Dead).OrderBy(e=>(e.transform.position-player.transform.position).sqrMagnitude).FirstOrDefault();

        void FocusInteraction()
        {
            focused=null; float best=-1;
            foreach(var target in interactions)
            {
                if(!target || !target.gameObject.activeInHierarchy || (target.id==CampaignCargo.Id && Carrying) ||
                    (CampaignSession.Has(target.id) && target.id!="nav" && target.id!="log")) continue;
                if(target.id=="eva" && !CampaignSession.Has("core")) continue;
                Vector3 direction=target.FocusPoint-eyes.transform.position; float distance=direction.magnitude;
                if(distance>target.range || distance<.01f) continue;
                float dot=Vector3.Dot(eyes.transform.forward,direction/distance); if(dot<.65f || dot<best) continue;
                if(Physics.Raycast(eyes.transform.position,direction/distance,out var hit,Mathf.Max(0,distance-.25f),~0,QueryTriggerInteraction.Ignore) &&
                    !hit.transform.IsChildOf(target.transform) && !target.transform.IsChildOf(hit.transform)) continue;
                focused=target; best=dot;
            }
            Prompt=focused ? "E  /  "+focused.title : "";
        }
        public CampaignInteractable NextObjective()
        {
            if(interactions==null || AwaitingEarth || Victory) return null;
            string id=chapter==0 ? (!CampaignSession.Has("log") ? "log" : !CampaignSession.Has("power") ? (Carrying || !Cargo ? "power" : CampaignCargo.Id) : !CampaignSession.Has("nav") ? "nav" : "exit") :
                OnBoardingRoute ? (!CampaignSession.Has(BoardingRoute.SealId) ? BoardingRoute.SealId : Alive>0 ? null :
                    !CampaignSession.Has(BoardingRoute.ManifestId) ? BoardingRoute.ManifestId : BoardingRoute.TransitId) :
                chapter==1 ? (!CampaignSession.Has("relayA") ? "relayA" : !CampaignSession.Has("relayB") ? "relayB" : Alive>0 ? null : "exit") :
                Outside ? (!CampaignSession.Has("antennaAlign") ? "antennaAlign" : "antennaSend") :
                !CampaignSession.Has("coolantA") ? "coolantA" : !CampaignSession.Has("coolantB") ? "coolantB" :
                Alive>0 ? null : CampaignSession.Has("core") ? "eva" : "core";
            return id==null ? null : interactions.FirstOrDefault(i=>i && i.gameObject.activeInHierarchy && i.id==id);
        }
        public void Interact(CampaignInteractable target)
        {
            if(!target || !Running) return;
            string id=target.id;
            if(IsSupply(id) || id.StartsWith("loot",StringComparison.Ordinal))
            {
                if(CampaignSession.Has(id)) return;
                bool healthOnly=id.StartsWith("health",StringComparison.Ordinal),ammoOnly=id.StartsWith("ammo",StringComparison.Ordinal);
                CampaignSession.Mark(id);
                if(!healthOnly) presentation.RefillAmmo();
                if(!ammoOnly) { Health=Mathf.Min(100,Health+35); Shield=60; }
                if(IsSupply(id) || !target.GetComponent<CampaignEnemy>()) target.gameObject.SetActive(false);
                Notify(healthOnly ? "Медкомплект: здоровье и щит восстановлены." : ammoOnly ? "Боекомплект пополнен." : "Припасы охраны: боезапас и ремонт костюма.");
                SaveCheckpoint(false); return;
            }
            if(chapter==0)
            {
                if(id=="log") { CampaignSession.Mark(id); Radio.Play("bridge_log",true); }
                else if(id==CampaignCargo.Id && Cargo)
                {
                    if(!CampaignSession.Has("log")) { Notify("Сначала проверь журнал командира."); return; }
                    if(Carrying || CampaignSession.Has("power")) return;
                    Cargo.PickUp(); Radio.Play("fuse_pickup",true);
                }
                else if(id=="power")
                {
                    if(!CampaignSession.Has("log")) { Notify("Сначала проверь журнал командира."); return; }
                    if(CampaignSession.Has(id)) return;
                    if(Cargo && !Carrying) { Notify("Гнездо пустое. Нужен аварийный аккумулятор."); return; }
                    if(Cargo) Cargo.Install();
                    CampaignSession.Mark(id); Radio.Play("bridge_power",true);
                    Notify("Питание восстановлено. Навигационный пульт отвечает.");
                }
                else if(id=="nav")
                {
                    if(!CampaignSession.Has("power")) { Notify("Терминалу нужно аварийное питание."); return; }
                    if(CampaignSession.Has("nav")) { Notify("Ориентация скорректирована. Инженерный шлюз открыт."); return; }
                    TerminalOpen=true; Flight.Begin(); SetCapture(false);
                    Radio.Play("flight_intro",true);
                    return;
                }
                else if(id=="exit")
                {
                    if(!CampaignSession.Has("nav")) { Notify("Уточни маршрут на навигационном терминале."); return; }
                    Travel(1); return;
                }
            }
            else if(OnBoardingRoute)
            {
                if(id==BoardingRoute.SealId)
                {
                    if(!CampaignSession.Mark(id)) return;
                    boardingNavigationRefresh=2.1f;
                    Notify("Давление восстановлено. За переборкой обнаружена штурмовая группа.",6);
                }
                else if(id==BoardingRoute.ManifestId)
                {
                    if(!CampaignSession.Has(BoardingRoute.SealId) || Alive>0) { Notify("Сначала зачисти грузовую палубу."); return; }
                    if(!CampaignSession.Mark(id)) return;
                    Notify("Журнал скопирован. Роботов доставил катер, который пробил внешний шлюз. Подъёмник доступен.",7);
                }
                else if(id==BoardingRoute.TransitId)
                {
                    if(Alive>0 || !CampaignSession.Has(BoardingRoute.ManifestId)) { Notify("Зачисти грузовую палубу и считай журнал абордажа."); return; }
                    destination=-3; transitioning=true; travelClock=.65f; SetCapture(false);
                    return;
                }
                else return;
                Boarding.Refresh();
            }
            else if(chapter==1)
            {
                if(id=="relayA" || id=="relayB")
                {
                    if(CampaignSession.Has(id)) return;
                    CampaignSession.Mark(id); Health=Mathf.Min(100,Health+25); Shield=60;
                    Radio.Play(CampaignSession.Has("relayA") && CampaignSession.Has("relayB") ? "relay_second" : "relay_first",true);
                }
                else if(id=="exit")
                {
                    if(!CampaignSession.Has("relayA") || !CampaignSession.Has("relayB")) { Notify("Восстанови оба реле питания."); return; }
                    if(Alive>0) { Notify("Шлюз заблокирован системой охраны. Нейтрализуй роботов."); return; }
                    Travel(2); return;
                }
            }
            else
            {
                if(id=="coolantA" || id=="coolantB")
                {
                    if(!CampaignSession.Mark(id)) return;
                    Notify(CoreExposed ? "Защитное поле Двойника отключено. Теперь он уязвим." : "Один контур отключён. Найди второй на другой стороне ядра.",5);
                    Radio.Play(CoreExposed ? "coolant_second" : "coolant_first",true);
                    Shield=60;
                }
                else if(id=="core")
                {
                    if(!CoreExposed) { Notify("Сначала отключи оба контура охлаждения."); return; }
                    if(Alive>0) { Notify("Двойник удерживает управление ядром."); return; }
                    if(!CampaignSession.Mark(id)) return;
                    Radio.Play("eva_unlocked",true);
                }
                else if(id=="eva" && !Outside && CampaignSession.Has("core")) { BeginAirlock(true); return; }
                else if(id=="evaReturn" && Outside && !AwaitingEarth) { BeginAirlock(false); return; }
                else if(id=="antennaAlign" && Outside)
                {
                    if(!CampaignSession.Mark(id)) return;
                    Radio.Play("antenna_aligned",true);
                    Notify("Захват несущей. Передатчик готов.");
                }
                else if(id=="antennaSend" && Outside)
                {
                    if(!CampaignSession.Has("antennaAlign")) { Notify("Сначала наведи антенну на Землю."); return; }
                    if(AwaitingEarth) return;
                    AwaitingEarth=true; Radio.Play("ending",true);
                    Notify("SOS отправлен. Ожидание ответа…");
                    return;
                }
            }
            foreach(var door in FindObjectsByType<CampaignDoor>()) if(door.unlockId==id) door.Open();
            SaveCheckpoint(false);
        }
        public void SetEnergy(int index)
        {
            Energy=Mathf.Clamp(index,0,2); Notify("Костюм: "+new[]{"УСИЛИТЕЛЬ ОРУЖИЯ","ЛИЧНЫЙ ЩИТ","СЕРВОПРИВОДЫ"}[Energy],3);
        }
        public GameCommandController EnsureCommands() => Commands ??=new GameCommandController(this);
        public void IssueTacticalCommand(string command)
        {
            TacticalOrder=command.ToLowerInvariant() switch { "attack"=>"АТАКА","defend"=>"ОБОРОНА","regroup"=>"ПЕРЕГРУППИРОВКА",_=>command.ToUpperInvariant() };
            // All input methods use the same gameplay consequences.
            SetEnergy(TacticalOrder=="АТАКА" ? 0 : TacticalOrder=="ОБОРОНА" ? 1 : 2);
        }
        public void CloseTerminal(int energy)
        {
            if(!TerminalOpen || Flight==null || !Flight.Ready) return;
            Flight.End(); TerminalOpen=false; SetCapture(Running);
            SetEnergy(energy); CampaignSession.Mark("nav"); Radio.Play("bridge_route",true); SaveCheckpoint(false);
            Notify("Курс стабилен. Открыт проход к повреждённому шлюзу.");
        }
        public void CancelTerminal() { Flight?.End(); TerminalOpen=false; SetCapture(Running); }
        public bool OpenBriefing()
        {
            if(TerminalOpen || Dead || Victory || transitioning || PrologueActive) return false;
            BriefingOpen=true;
            Commands.Clear();
            Radio.Tick(0);
            SetCapture(false);
            return true;
        }
        public void CloseBriefing()
        {
            if(!BriefingOpen) return;
            BriefingOpen=false;
            SetCapture(Running);
            if(!BriefingStartsOperation) return;
            BriefingStartsOperation=false;
            BeginStory();
        }
        public void SetPaused(bool value) { Paused=value; if(value) Commands?.Clear(); SetCapture(Running); }
        void SetCapture(bool value) { Cursor.lockState=value ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible=!value; }
        public void Notify(string message,float seconds=5) { Notice=message; noticeUntil=Time.unscaledTime+seconds; }
        void Say(string speaker,string message,float seconds) { Subtitle=speaker+"  /  "+message; subtitleUntil=Time.unscaledTime+seconds; }
        public void SaveCheckpoint(bool announce=true)
        {
            if(transitioning || Dead || !player || !presentation) return;
            var data=CampaignSession.Data;
            data.chapter=chapter; data.hasCheckpoint=true; data.snapshot=true;
            var position=player.transform.position;
            data.checkpoint=IsSafeCheckpoint(position) ? position : IsSafeCheckpoint(safePosition) ? safePosition :
                Outside ? ExteriorOrigin+new Vector3(0,.04f,-5) : spawn;
            data.yaw=player.transform.eulerAngles.y; data.pitch=pitch;
            data.health=Health; data.shield=Shield; data.energy=Energy; data.equipped=presentation.equippedIndex;
            data.cargoHeld=Carrying; data.outside=Outside; data.oxygen=Oxygen;
            presentation.CaptureAmmo(out data.magazines,out data.reserves);
            data.salvage=chapter==1 && interactions!=null ? interactions.Where(item=>item && item.id.StartsWith("loot",StringComparison.Ordinal) &&
                    !CampaignSession.Has(item.id) && int.TryParse(item.id.Substring(4),out _))
                .Select(item=>new CampaignSession.Salvage{number=int.Parse(item.id.Substring(4)),position=item.FocusPoint}).ToArray()
                : Array.Empty<CampaignSession.Salvage>();
            CampaignSession.Save();
            if(announce) Notify(CampaignSession.Error ?? "Контрольная точка сохранена.");
        }
        bool IsSafeCheckpoint(Vector3 position)
        {
            if(!CampaignSession.Finite(position) || position.y<-.45f || position.y>5) return false;
            if(Outside)
            {
                Vector3 local=position-ExteriorOrigin;
                if(Mathf.Abs(local.x)>9 || local.z< -10 || local.z>15) return false;
            }
            else if(position.x<navigationMin.x || position.x>navigationMax.x || position.z<navigationMin.y || position.z>navigationMax.y) return false;
            if(!Physics.Raycast(position+Vector3.up*.14f,Vector3.down,.52f,~0,QueryTriggerInteraction.Ignore)) return false;
            foreach(var collider in Physics.OverlapCapsule(position+Vector3.up*.40f,position+Vector3.up*1.42f,.32f,~0,QueryTriggerInteraction.Ignore))
                if(collider!=player && !collider.transform.IsChildOf(player.transform)) return false;
            return true;
        }
        public void StartNewGame()
        {
            CampaignSession.NewGame(); destination=0; nextSceneStartsPrologue=true; traveling=true; SceneManager.LoadScene(ScenePaths[0]);
        }
        public void ContinueGame()
        {
            if(!CampaignSession.Load()) { Notify(CampaignSession.Error); return; }
            traveling=true; SceneManager.LoadScene(ScenePaths[CampaignSession.Data.chapter]);
        }
        public void StartCurrentEpisode()
        {
            CloseMenuView(); MainMenu=false; Paused=false; SetCapture(Running); if(!Victory) BeginStory();
        }
        public void RestartCheckpoint()
        {
            if(CampaignSession.HasSave && CampaignSession.Load(chapter))
            { traveling=true; SceneManager.LoadScene(ScenePaths[chapter]); return; }
            CampaignSession.Flags.RemoveWhere(flag=>flag.StartsWith(chapter+"/")); CampaignSession.Data.hasCheckpoint=false;
            CampaignSession.Data.snapshot=false; CampaignSession.Data.finished=false;
            traveling=true; SceneManager.LoadScene(ScenePaths[chapter]);
        }
        public void BackToMenu()
        {
            if(!Dead && !Victory) SaveCheckpoint(false);
            MainMenu=true; Paused=false; Commands?.Clear(); SetCapture(false);
            if(chapter!=0) { traveling=false; SceneManager.LoadScene(ScenePaths[0]); }
            else OpenMenuView();
        }
        public void QuitGame()
        {
            if(!MainMenu && !Dead) SaveCheckpoint(false);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
        void Travel(int next)
        {
            CampaignSession.Data.chapter=next; CampaignSession.Data.hasCheckpoint=false; CampaignSession.Data.snapshot=false;
            CampaignSession.Data.salvage=Array.Empty<CampaignSession.Salvage>(); CampaignSession.Save();
            transitioning=true; destination=next; travelClock=1; SetCapture(false);
        }
        void SetExterior(bool outside)
        {
            Outside=outside && exterior;
            if(exterior) exterior.SetActive(Outside);
            presentation.SetHolstered(Outside);
            if(Outside) Energy=0;
        }

        void CompleteBoardingTransfer()
        {
            CampaignSession.Mark(BoardingRoute.TransitId);
            spawn=engineeringEntry; spawnRotation=engineeringRotation;
            PlacePlayer(spawn,spawnRotation);
            safePosition=spawn; pitch=0; presentation.ApplyLook(pitch);
            Enemies.Clear();
            Boarding.gameObject.SetActive(false);
            CreateEnemies();
            RefreshInteractions(); transitioning=false; SetCapture(Running);
            Radio.Play("engineering_intro",true);
            SaveCheckpoint(false);
        }

        void BeginAirlock(bool outward)
        {
            if(!exterior) { Notify("Внешний шлюз недоступен."); return; }
            destination=outward ? -1 : -2; transitioning=true; travelClock=.65f;
            SetCapture(false); Notify(outward ? "Разгерметизация шлюза. Магнитные ботинки включены." : "Шлюз герметичен. Заправка кислородом.",3);
        }
        void CompleteAirlock(bool outward)
        {
            SetExterior(outward); Oxygen=100;
            PlacePlayer(Outside ? ExteriorOrigin+new Vector3(0,.04f,-5) : new Vector3(0,.04f,-11.3f),
                Outside ? Quaternion.identity : Quaternion.Euler(0,180,0));
            safePosition=player.transform.position; pitch=0; presentation.ApplyLook(pitch);
            RefreshInteractions(); transitioning=false; SetCapture(Running);
            Radio.Play(outward ? "eva_outside" : "eva_unlocked",true);
            SaveCheckpoint(false);
        }
        void TickExterior(float dt)
        {
            if(dishPivot)
            {
                var target=CampaignSession.Has("antennaAlign") ? Quaternion.LookRotation(new Vector3(88,-53,227)-dishPivot.localPosition) : dishStart;
                dishPivot.localRotation=Quaternion.RotateTowards(dishPivot.localRotation,target,dt*24);
            }
            if(AwaitingEarth) return;
            Oxygen=Mathf.Max(0,Oxygen-dt*.8f);
            if(Oxygen<=0) EmergencyReel();
        }
        void EmergencyReel()
        {
            if(AwaitingEarth)
            {
                PlacePlayer(ExteriorOrigin+new Vector3(0,.04f,-5),Quaternion.identity);
                return;
            }
            CompleteAirlock(false);
            Notify("Страховочный трос вернул тебя в шлюз. Кислород пополнен.",5);
        }
        void FinishCampaign()
        {
            AwaitingEarth=false; Victory=true; CampaignSession.Mark("antennaSend");
            CampaignSession.Data.finished=true; CampaignSession.Data.wins++; CampaignSession.Data.credits+=200;
            Commands.Clear(); SaveCheckpoint(false); SetCapture(false);
        }
        void PlacePlayer(Vector3 position,Quaternion rotation)
        {
            player.enabled=false; player.transform.SetPositionAndRotation(position,rotation); player.enabled=true; vertical=0;
        }
        public void ExportReport()
        {
            try
            {
                string folder=Path.GetDirectoryName(CampaignSession.SavePath); Directory.CreateDirectory(folder);
                var rows=new List<string>{"DOPPELGANGER / CAMPAIGN REPORT",DateTime.Now.ToString("s"),"Chapter: "+chapter,
                    "Samples: "+CampaignSession.Data.samples,"Examples after damage: "+CampaignSession.Data.underFire,"Weight updates: "+CampaignSession.Policy.Updates,
                    "Player shots/hits/crouches: "+CampaignSession.Data.shots+" / "+CampaignSession.Data.hits+" / "+CampaignSession.Data.crouches,
                    "Engineering: combat telemetry and training samples.",
                    "Core: adaptive pursuit, strafe prediction, and a temporary 0.5-second reflection ability."};
                foreach(var enemy in Enemies) if(enemy) rows.Add((enemy.boss ? "Adaptive Doppelganger" : "Utility security")+": shots="+enemy.Shots+", hits="+enemy.Hits+", crouches="+enemy.Crouches+", independent="+enemy.AutonomousShots+", reflected="+enemy.ReflectedShots+", intent="+enemy.Intent);
                File.WriteAllLines(Path.Combine(folder,"campaign-report.txt"),rows); File.WriteAllText(Path.Combine(folder,"campaign-policy.json"),JsonUtility.ToJson(CampaignSession.Policy.Export(),true));
                Notify("Отчёт сохранён: "+folder,10); Debug.Log("Campaign report: "+folder);
            }
            catch(Exception exception) { Notify("Ошибка отчёта: "+exception.Message); }
        }
        public void UnlockColor()
        {
            if(CampaignSession.Data.alternateColor) return;
            if(CampaignSession.Data.credits<200) { Notify("Нужно 200 сигнатур. Они выдаются за охрану и завершение эпизода."); return; }
            CampaignSession.Data.credits-=200; CampaignSession.Data.alternateColor=true; CampaignSession.Save(); Notify("Открыт янтарный цвет импульсного следа.");
        }
        public void Beam(Vector3 start,Vector3 end,Color color)
        {
            if(!beamMaterial) return;
            var go=new GameObject("Pulse trail",typeof(LineRenderer)); var line=go.GetComponent<LineRenderer>(); line.sharedMaterial=beamMaterial;
            line.positionCount=2; line.startWidth=.024f; line.endWidth=.006f; line.startColor=line.endColor=color;
            line.SetPositions(new[]{start,end}); Destroy(go,.065f);
        }
        public void MarkBulletHit(RaycastHit hit,float diameter)
        {
            if(bulletMarks) bulletMarks.Place(hit,diameter);
        }
        void Impact(Vector3 point,Vector3 normal,bool enemy)
        {
            var go=new GameObject("Impact spark",typeof(Light)); go.transform.position=point+normal*.03f;
            var light=go.GetComponent<Light>(); light.range=1.6f; light.intensity=enemy ? 4 : 2; light.color=enemy ? new Color(1,.35f,.1f) : Color.cyan; Destroy(go,.07f);
        }
        void SetupAmbience()
        {
            ambience=gameObject.AddComponent<AudioSource>(); ambience.loop=true; ambience.spatialBlend=0; ambience.volume=.07f;
            const int rate=22050; var values=new float[rate*3];
            for(int i=0;i<values.Length;i++) { float t=i/(float)rate; values[i]=Mathf.Sin(t*Mathf.PI*2*44)*.2f+Mathf.Sin(t*Mathf.PI*2*67)*.08f+Mathf.Sin(t*Mathf.PI*2*110)*.025f; }
            var clip=AudioClip.Create("Reactor roomtone",values.Length,1,rate,false); clip.SetData(values,0); ambience.clip=clip; ambience.Play();
        }
        void SetupReflection()
        {
            var probe=new GameObject("Local ship reflections",typeof(ReflectionProbe)).GetComponent<ReflectionProbe>();
            probe.transform.SetParent(transform,false); probe.transform.position=new Vector3(0,2.5f,chapter==0 ? 4 : chapter==1 ? -4 : -6);
            probe.mode=UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode=UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode=UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size=new Vector3(navigationMax.x-navigationMin.x,9,navigationMax.y-navigationMin.y+15);
            probe.boxProjection=true; probe.resolution=128; probe.intensity=1.15f; probe.nearClipPlane=.1f; probe.farClipPlane=65;
        }
        void OnDisable() { Commands?.Clear(); SetCapture(false); }
        void OnDestroy() { if(beamMaterial) Destroy(beamMaterial); if(ambience && ambience.clip) Destroy(ambience.clip); }
    }
}
