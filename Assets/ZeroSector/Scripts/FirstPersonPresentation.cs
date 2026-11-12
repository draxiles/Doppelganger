using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using System;
using System.Collections.Generic;

namespace ZeroSector
{
    // Owns presentation only. Movement, damage and the learned policy remain in the arena.
    public sealed class FirstPersonPresentation : MonoBehaviour
    {
        [Serializable] public sealed class WeaponSlot
        {
            public string id, title;
            public Transform root, muzzle;
            public Animator animator;
            public Animator secondaryAnimator;
            public AnimationClip idle, walk, run, fire, inspect, reload;
            public AnimationClip secondaryIdle, secondaryWalk, secondaryRun, secondaryFire, secondaryReload;
            public AudioClip shotSound;
            public Vector3 hipOffset, aimOffset;
            public float damage=12, interval=.32f, heat=13;
            public int magazineSize=30, reserveAmmo=120;
            public float reloadDuration=2.1f;
            public bool automatic=true;
        }
        public List<WeaponSlot> weapons = new();
        public int equippedIndex;
        public const string ServicePistolId="Campaign.P7";
        public string WeaponTitle => BridgeSidearmVisible ? "П-7 / служебный пистолет" :
            Equipped!=null ? Equipped.title : HasWeapon ? "ОРУЖИЕ" : "ОРУЖИЕ НЕ ПОДКЛЮЧЕНО";
        public WeaponSlot Equipped => weapons.Count>0 ? weapons[Mathf.Clamp(equippedIndex,0,weapons.Count-1)] : null;
        public Camera eyes;
        public CharacterController body;
        public Transform eyeHeight, weaponPivot, weaponModel, muzzle;
        public Animator weaponAnimator;
        public AnimationClip idleClip, walkClip, runClip, fireClip, inspectClip;
        public AudioClip shotSound, walkSound, runSound, reloadSound, switchSound, hitSound;
        public Vector3 hipOffset, aimOffset = new(-.12f,.035f,.06f);
        public float normalFov = 75, aimFov = 55;
        public bool IsSprinting { get; private set; }
        public bool ExternalCamera { get; set; }
        public bool IsInspecting => inspectTime > 0;
        public float AimAmount { get; private set; }
        public float ShotPulse { get; private set; }
        public float HitPulse { get; private set; }
        public float DamagePulse { get; private set; }
        public float Movement { get; private set; }
        public float ShotSpreadDegrees => shotSpread * Mathf.Lerp(.84f,1,Movement) *
            Mathf.Lerp(1,.43f,AimAmount) * (crouching ? .78f : 1);
        public bool CanFire => !BridgeSidearmVisible && !IsSprinting && !IsInspecting && !IsReloading && !IsSwitching && (Equipped==null || MagazineAmmo>0);
        public bool HasWeapon => weaponModel;
        public bool BridgeSidearmVisible => bridgeSidearm && bridgeSidearm.gameObject.activeInHierarchy;
        public bool IsReloading => reloadRemaining>0;
        public bool IsSwitching => switchRemaining>0;
        public int MagazineAmmo => CurrentAmmo?.magazine ?? 0;
        public int ReserveAmmo => CurrentAmmo?.reserve ?? 0;
        public int MagazineCapacity => Equipped==null ? 0 : Mathf.Max(1,Equipped.magazineSize);
        public float ReloadProgress => IsReloading ? 1-Mathf.Clamp01(reloadRemaining/reloadLength) : 0;

        sealed class AmmoState { public int magazine, reserve; }
        readonly Dictionary<WeaponSlot,AmmoState> ammunition = new();
        AmmoState CurrentAmmo
        {
            get
            {
                var slot=Equipped; if(slot==null) return null;
                if(!ammunition.TryGetValue(slot,out var state))
                { state=new AmmoState {magazine=Mathf.Max(1,slot.magazineSize),reserve=Mathf.Max(0,slot.reserveAmmo)}; ammunition.Add(slot,state); }
                return state;
            }
        }
        float reloadRemaining, reloadLength, switchRemaining;
        int pendingWeapon=-1;
        bool switchCommitted;
        const float SwitchDuration=.54f, InspectDuration=2.25f;
        const float SidearmDrawDuration=.40f;
        BridgeSidearm bridgeSidearm, combatSidearm;
        float sidearmDrawRemaining;

        readonly struct SpreadProfile
        {
            public readonly float growth, limit, delay, recovery;
            public SpreadProfile(float growth,float limit,float delay,float recovery)
            { this.growth=growth; this.limit=limit; this.delay=delay; this.recovery=recovery; }
        }
        static readonly SpreadProfile RifleSpread=new(.95f,3.2f,.16f,4.3f);
        static readonly SpreadProfile HeavySpread=new(1.8f,4.2f,.20f,3.8f);
        static readonly SpreadProfile SidearmSpread=new(.86f,1.75f,.095f,5.5f);
        SpreadProfile spreadProfile=RifleSpread;
        float shotSpread,spreadRecoveryDelay;

        float pitch, clock, inspectTime, recoil, weaponKick, fireTime, stepTime;
        Vector2 headViewOffset;
        bool active, crouching;
        Vector2 look;
        Vector3 sway;
        Light flash;
        AudioSource audioSource;
        SpentCasingEffects casingEffects;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[6];
        readonly bool[] clipValid = new bool[6];
        readonly float[] weights = new float[6];
        AnimationMixerPlayable secondaryMixer;
        readonly AnimationClipPlayable[] secondaryClips=new AnimationClipPlayable[5];
        readonly bool[] secondaryValid=new bool[5];

        public void Initialize(CharacterController controller, Camera camera)
        {
            body = controller; eyes = camera;
            if(!eyeHeight)
            {
                eyeHeight = new GameObject("Eye height / crouch").transform;
                eyeHeight.SetParent(camera.transform.parent,false);
                eyeHeight.localPosition = camera.transform.localPosition;
                camera.transform.SetParent(eyeHeight,false);
                camera.transform.localPosition = Vector3.zero;
            }
            if(!weaponPivot)
            {
                weaponPivot = new GameObject("First person weapon / procedural motion").transform;
                weaponPivot.SetParent(camera.transform,false);
            }
            camera.nearClipPlane = .035f;
            audioSource = camera.GetComponent<AudioSource>();
            if(!audioSource) audioSource = camera.gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false; audioSource.spatialBlend = 0;
            var lightObject = new GameObject("Muzzle flash light",typeof(Light));
            lightObject.transform.SetParent(weaponPivot,false);
            lightObject.transform.localPosition = new Vector3(.22f,-.18f,.65f);
            flash = lightObject.GetComponent<Light>(); flash.type = LightType.Point;
            flash.color = new Color(1,.65f,.3f); flash.range = 3; flash.shadows = LightShadows.None;
            flash.enabled = false;
            if(weapons.Count>0) Equip(equippedIndex);
            else ConfigureAnimations();
        }

        public void Equip(int index)
        {
            switchRemaining=0; pendingWeapon=-1;
            ApplyEquipped(index);
        }

        public void InstallCombatSidearm()
        {
            if(!weaponPivot || combatSidearm) return;
            combatSidearm=BridgeSidearm.Create(weaponPivot,true);
            if(!combatSidearm) return;
            combatSidearm.gameObject.SetActive(false);
            weapons.Add(new WeaponSlot
            {
                id=ServicePistolId, title="П-7 / служебный пистолет",
                root=combatSidearm.transform, muzzle=combatSidearm.Muzzle, shotSound=combatSidearm.ShotSound,
                hipOffset=new Vector3(.19f,-.164f,.42f), aimOffset=new Vector3(-.19f,.096f,.045f),
                damage=29, interval=.33f, heat=5, magazineSize=12, reserveAmmo=48, reloadDuration=1.46f, automatic=false
            });
            if(weapons.Count==1) Equip(0);
        }

        void ApplyEquipped(int index)
        {
            if(weapons.Count==0) return;
            HideBridgeSidearm();
            equippedIndex=((index%weapons.Count)+weapons.Count)%weapons.Count;
            for(int i=0;i<weapons.Count;i++) if(weapons[i].root) weapons[i].root.gameObject.SetActive(i==equippedIndex);
            var slot=weapons[equippedIndex];
            spreadProfile=!slot.automatic ? SidearmSpread : slot.interval>=.45f ? HeavySpread : RifleSpread;
            shotSpread=spreadRecoveryDelay=0;
            weaponModel=slot.root; muzzle=slot.muzzle; weaponAnimator=slot.animator;
            idleClip=slot.idle; walkClip=slot.walk; runClip=slot.run; fireClip=slot.fire; inspectClip=slot.inspect;
            shotSound=slot.shotSound; hipOffset=slot.hipOffset; aimOffset=slot.aimOffset;
            ResetView();
            if(Application.isPlaying) ConfigureAnimations();
            else if(weaponPivot) { weaponPivot.localPosition=hipOffset; weaponPivot.localRotation=Quaternion.identity; }
        }

        // Lower the current viewmodel, replace it below the screen, then raise the next one.
        // Editor tools deliberately keep using immediate Equip instead.
        public bool RequestEquip(int index)
        {
            if(weapons.Count<2 || IsSwitching) return false;
            index=((index%weapons.Count)+weapons.Count)%weapons.Count;
            if(index==equippedIndex) return false;
            CancelReload(); inspectTime=fireTime=0; IsSprinting=false;
            pendingWeapon=index; switchCommitted=false; switchRemaining=SwitchDuration;
            if(switchSound && audioSource) audioSource.PlayOneShot(switchSound,.4f);
            return true;
        }

        public bool BeginReload()
        {
            if(BridgeSidearmVisible) return false;
            var state=CurrentAmmo;
            if(!HasWeapon || state==null || IsSwitching || IsReloading || state.reserve<=0 || state.magazine>=MagazineCapacity) return false;
            inspectTime=fireTime=0; IsSprinting=false;
            var slot=Equipped;
            reloadLength=Mathf.Max(.3f,slot.reload ? slot.reload.length : slot.reloadDuration);
            reloadRemaining=reloadLength;
            if(reloadSound && audioSource) audioSource.PlayOneShot(reloadSound,.45f);
            if(clipValid[5]) { clips[5].SetTime(0); clips[5].SetSpeed(1); }
            if(secondaryValid[4]) { secondaryClips[4].SetTime(0); secondaryClips[4].SetSpeed(1); }
            return true;
        }

        void CancelReload() { reloadRemaining=0; }

        public bool TryConsumeRound()
        {
            if(!CanFire) return false;
            var state=CurrentAmmo; if(state==null || state.magazine<=0) return false;
            state.magazine--; return true;
        }

        public void RefillAmmo()
        {
            CancelReload();
            shotSpread=spreadRecoveryDelay=0;
            foreach(var slot in weapons)
            {
                if(!ammunition.TryGetValue(slot,out var state)) ammunition.Add(slot,state=new AmmoState());
                state.magazine=Mathf.Max(1,slot.magazineSize); state.reserve=Mathf.Max(0,slot.reserveAmmo);
            }
        }

        public void SetHolstered(bool value)
        {
            if(value) { HideBridgeSidearm(); ResetView(); }
            if(weaponPivot) weaponPivot.gameObject.SetActive(!value);
        }

        public bool ShowBridgeSidearm()
        {
            if(!weaponPivot || !weaponPivot.gameObject.activeInHierarchy) return false;
            if(BridgeSidearmVisible) return true;
            if(!bridgeSidearm) bridgeSidearm=BridgeSidearm.Create(weaponPivot);
            if(!bridgeSidearm) return false;
            ResetView();
            switchRemaining=0;
            pendingWeapon=-1;
            sidearmDrawRemaining=SidearmDrawDuration;
            if(weaponModel) weaponModel.gameObject.SetActive(false);
            bridgeSidearm.gameObject.SetActive(true);
            if(switchSound && audioSource) audioSource.PlayOneShot(switchSound,.4f);
            return true;
        }

        public void HideBridgeSidearm()
        {
            if(!bridgeSidearm || !bridgeSidearm.gameObject.activeSelf) return;
            bridgeSidearm.gameObject.SetActive(false);
            sidearmDrawRemaining=0;
            if(weaponModel) weaponModel.gameObject.SetActive(true);
        }

        public void CaptureAmmo(out int[] magazines,out int[] reserves)
        {
            magazines=new int[weapons.Count]; reserves=new int[weapons.Count];
            for(int i=0;i<weapons.Count;i++)
            {
                var slot=weapons[i];
                magazines[i]=ammunition.TryGetValue(slot,out var state) ? state.magazine : slot.magazineSize;
                reserves[i]=state!=null ? state.reserve : slot.reserveAmmo;
            }
        }
        public void RestoreAmmo(int[] magazines,int[] reserves,int slotIndex)
        {
            if(magazines==null || reserves==null) return;
            int savedCount=Mathf.Min(weapons.Count,Mathf.Min(magazines.Length,reserves.Length));
            for(int i=0;i<savedCount;i++)
            {
                var slot=weapons[i];
                ammunition[slot]=new AmmoState { magazine=Mathf.Clamp(magazines[i],0,slot.magazineSize), reserve=Mathf.Clamp(reserves[i],0,slot.reserveAmmo) };
            }
            if(savedCount>0) Equip(Mathf.Clamp(slotIndex,0,savedCount-1));
        }

        public void ConfigureAnimations()
        {
            if(graph.IsValid()) graph.Destroy();
            for(int i=0;i<6;i++) { clipValid[i]=false; weights[i]=0; }
            for(int i=0;i<5;i++) secondaryValid[i]=false;
            // Without a known rest clip, preserve the prefab's authored controller/pose.
            if(!weaponAnimator || !idleClip) return;
            weaponAnimator.applyRootMotion = false;
            // Imported clips refer to the sample game's event receiver. Our combat and audio
            // are driven by OnShot/SetInput instead of those unavailable sample components.
            weaponAnimator.fireEvents = false;
            weaponAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Doppelganger weapon animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph,6);
            var output = AnimationPlayableOutput.Create(graph,"Arms and weapon",weaponAnimator);
            output.SetSourcePlayable(mixer);
            var sources = new[]{idleClip,walkClip,runClip,fireClip,inspectClip,Equipped?.reload};
            for(int i=0;i<sources.Length;i++)
            {
                if(!sources[i]) continue;
                clips[i] = AnimationClipPlayable.Create(graph,sources[i]);
                clips[i].SetApplyFootIK(false); clips[i].SetApplyPlayableIK(false);
                graph.Connect(clips[i],0,mixer,i); clipValid[i]=true;
            }
            weights[0]=1; mixer.SetInputWeight(0,1);
            var slot=Equipped;
            if(slot!=null && slot.secondaryAnimator && slot.secondaryIdle)
            {
                slot.secondaryAnimator.applyRootMotion=false;
                slot.secondaryAnimator.fireEvents=false;
                secondaryMixer=AnimationMixerPlayable.Create(graph,5);
                AnimationPlayableOutput.Create(graph,"Weapon mechanisms",slot.secondaryAnimator).SetSourcePlayable(secondaryMixer);
                var secondarySources=new[]{slot.secondaryIdle,slot.secondaryWalk,slot.secondaryRun,slot.secondaryFire,slot.secondaryReload};
                for(int i=0;i<5;i++)
                {
                    if(!secondarySources[i]) continue;
                    secondaryClips[i]=AnimationClipPlayable.Create(graph,secondarySources[i]);
                    graph.Connect(secondaryClips[i],0,secondaryMixer,i); secondaryValid[i]=true;
                }
                secondaryMixer.SetInputWeight(0,1);
            }
            graph.Play();
            graph.Evaluate(0);
        }

        public void SetInput(float lookPitch, Vector2 mouseDelta, float movement, bool sprint,
            bool crouch, bool aim, bool inspectPressed, bool running)
        {
            pitch = lookPitch; look = mouseDelta; active = running;
            Movement = running ? Mathf.Clamp01(movement) : 0;
            crouching = crouch;
            IsSprinting = running && sprint && Movement > .15f && !crouch && !aim && !IsReloading && !IsSwitching;
            if((aim || IsSprinting) && IsInspecting) inspectTime = 0;
            if(inspectPressed && running && HasWeapon && !IsSprinting && !aim && !IsReloading && !IsSwitching)
            {
                inspectTime = inspectClip ? inspectClip.length : InspectDuration;
                if(clipValid[4]) clips[4].SetTime(0);
            }
            float target = running && aim && !IsInspecting && !IsSprinting && !IsReloading && !IsSwitching ? 1 : 0;
            AimAmount = Mathf.Lerp(AimAmount,target,1-Mathf.Exp(-14*Time.deltaTime));
        }

        public void OnShot(bool hit)
        {
            inspectTime = 0;
            ShotPulse = 1; if(hit) HitPulse = 1;
            bool pistol=Equipped?.id==ServicePistolId;
            bool heavy=Equipped is { automatic:true, interval:>=.45f };
            recoil = Mathf.Min(6,recoil + Mathf.Lerp(1.1f,.65f,AimAmount)*(heavy ? 1.45f : pistol ? 1.25f : 1));
            weaponKick = Mathf.Min(.1f,weaponKick+(heavy ? .064f : pistol ? .058f : .045f));
            shotSpread=Mathf.Min(spreadProfile.limit,shotSpread+spreadProfile.growth);
            spreadRecoveryDelay=spreadProfile.delay;
            fireTime = fireClip ? fireClip.length : .1f;
            if(clipValid[3]) clips[3].SetTime(0);
            if(secondaryValid[3]) secondaryClips[3].SetTime(0);
            EjectCasing(pistol,heavy);
            if(shotSound && audioSource) audioSource.PlayOneShot(shotSound,.5f);
            if(hit && hitSound && audioSource) audioSource.PlayOneShot(hitSound,.35f);
        }

        void EjectCasing(bool pistol,bool heavy)
        {
            if(!Application.isPlaying || !eyes || !weaponModel || !weaponModel.gameObject.activeInHierarchy ||
                BridgeSidearmVisible || ExternalCamera || Equipped==null) return;

            // The long-gun receiver follows its own animated rig. The P-7 port
            // sits just above the right side of the slide on its authored mesh.
            Transform receiver=Equipped.secondaryAnimator ? Equipped.secondaryAnimator.transform : weaponModel;
            Vector3 port=pistol ? new(.030f,.044f,.022f) : heavy ? new(.157f,.10f,.33f) : new(.074f,.068f,.15f);
            Vector3 origin=receiver.TransformPoint(port);
            if(Vector3.Dot(origin-eyes.transform.position,eyes.transform.forward)<eyes.nearClipPlane+.06f)
                origin=eyes.transform.TransformPoint(.16f,-.09f,.26f);
            if(casingEffects==null)
            {
                var renderer=receiver.GetComponentInChildren<Renderer>();
                casingEffects=SpentCasingEffects.Create(renderer ? renderer.sharedMaterial : null);
            }

            Vector3 inheritedVelocity=body ? Vector3.ProjectOnPlane(body.velocity,Vector3.up)*.42f : Vector3.zero;
            Vector3 size=pistol ? new(.013f,.028f,.013f) : heavy ? new(.024f,.050f,.024f) : new(.016f,.039f,.016f);
            casingEffects?.Eject(origin,eyes.transform,inheritedVelocity,size);
        }

        public void OnDamage() { DamagePulse = 1; }
        public void SetHeadViewOffset(Vector2 degrees) { headViewOffset=degrees; }

        public void ApplyLook(float value)
        { pitch=value; eyes.transform.localRotation=LookRotation(0); }

        Quaternion LookRotation(float roll) =>
            Quaternion.Euler(Mathf.Clamp(pitch+headViewOffset.y-recoil,-85,85),headViewOffset.x,roll);
        public Vector3 MuzzlePosition => muzzle ? muzzle.position : eyes.transform.TransformPoint(.24f,-.2f,.65f);

        public Vector3 ShotDirection()
        {
            float spread=ShotSpreadDegrees;
            if(spread<=0) return eyes.transform.forward;
            Vector2 offset=UnityEngine.Random.insideUnitCircle*Mathf.Tan(spread*Mathf.Deg2Rad);
            return eyes.transform.TransformDirection(new Vector3(offset.x,offset.y,1)).normalized;
        }

        public void ResetView()
        {
            CancelReload();
            recoil = weaponKick = inspectTime = fireTime = 0;
            ShotPulse = HitPulse = DamagePulse = AimAmount = 0;
            clock = stepTime = 0;
        }

        void LateUpdate()
        {
            if(!eyes || !weaponPivot) return;
            float dt = Mathf.Min(Time.deltaTime,.05f);
            // Presentation pauses together with gameplay; no background inspection or footsteps.
            if(active)
            {
                casingEffects?.Tick(dt);
                float recoveryTime=Mathf.Max(0,dt-spreadRecoveryDelay);
                spreadRecoveryDelay=Mathf.Max(0,spreadRecoveryDelay-dt);
                shotSpread=Mathf.Max(0,shotSpread-spreadProfile.recovery*recoveryTime);
                if(IsSwitching)
                {
                    switchRemaining=Mathf.Max(0,switchRemaining-dt);
                    if(!switchCommitted && switchRemaining<=SwitchDuration*.5f)
                    { switchCommitted=true; ApplyEquipped(pendingWeapon); }
                    if(switchRemaining<=0) pendingWeapon=-1;
                }
                if(IsReloading)
                {
                    reloadRemaining=Mathf.Max(0,reloadRemaining-dt);
                    if(reloadRemaining<=0)
                    {
                        var state=CurrentAmmo;
                        int transfer=Mathf.Min(MagazineCapacity-state.magazine,state.reserve);
                        state.magazine+=transfer; state.reserve-=transfer;
                    }
                }
                clock += dt * (IsSprinting ? 13 : crouching ? 6 : 9);
                sidearmDrawRemaining = Mathf.Max(0,sidearmDrawRemaining-dt);
                inspectTime = Mathf.Max(0,inspectTime-dt);
                fireTime = Mathf.Max(0,fireTime-dt);
                if(walkSound && Movement > .15f && body.isGrounded)
                {
                    stepTime -= dt;
                    if(stepTime <= 0)
                    { stepTime = IsSprinting ? .29f : crouching ? .65f : .46f; audioSource.PlayOneShot(IsSprinting && runSound ? runSound : walkSound,crouching ? .12f : .28f); }
                }
            }
            float motion = active ? Movement*(1-AimAmount*.8f) : 0;
            if(!ExternalCamera)
            {
                eyes.transform.localPosition = new Vector3(Mathf.Sin(clock)*.012f,Mathf.Sin(clock*2)*.016f,0)*motion;
                eyes.transform.localRotation = LookRotation(-Mathf.Sin(clock)*motion*.18f);
                eyes.fieldOfView = Mathf.Lerp(eyes.fieldOfView,Mathf.Lerp(IsSprinting ? normalFov+5 : normalFov,aimFov,AimAmount),1-Mathf.Exp(-10*dt));
            }
            recoil = Mathf.Lerp(recoil,0,1-Mathf.Exp(-9*dt));
            weaponKick = Mathf.Lerp(weaponKick,0,1-Mathf.Exp(-17*dt));
            ShotPulse = Mathf.MoveTowards(ShotPulse,0,dt*10);
            HitPulse = Mathf.MoveTowards(HitPulse,0,dt*6);
            DamagePulse = Mathf.MoveTowards(DamagePulse,0,dt*2.4f);
            Vector3 desiredSway = active ? new Vector3(-look.y*.32f,look.x*.36f,-look.x*.2f)*Mathf.Lerp(1,.35f,AimAmount) : Vector3.zero;
            sway = Vector3.Lerp(sway,Vector3.ClampMagnitude(desiredSway,3),1-Mathf.Exp(-12*dt));
            Vector3 bob = new Vector3(Mathf.Sin(clock)*.012f,Mathf.Cos(clock*2)*.01f,0)*motion;
            Vector3 position = Vector3.Lerp(hipOffset,hipOffset+aimOffset,AimAmount)+bob+Vector3.back*weaponKick;
            Vector3 rotation = sway+new Vector3(-weaponKick*65,0,0);
            if(BridgeSidearmVisible)
            {
                float draw=1-Mathf.SmoothStep(0,1,1-sidearmDrawRemaining/SidearmDrawDuration);
                position=Vector3.Lerp(new Vector3(.215f,-.190f,.46f),new Vector3(.02f,-.135f,.49f),AimAmount)+bob
                    +new Vector3(.045f,-.32f,-.13f)*draw;
                rotation=sway+new Vector3(34,-12,-17)*draw;
            }
            if(IsSprinting) { position += new Vector3(.03f,-.09f,-.07f); rotation += new Vector3(12,-14,-12); }
            // Fallback rotates the actual model; a supplied inspection clip takes precedence.
            if(IsInspecting && !clipValid[4])
            {
                float t=Mathf.Clamp01(1-inspectTime/InspectDuration);
                // Deliberate raise -> hold the side in view -> return. The complete arm/weapon
                // assembly moves together so grips cannot detach and cropped upper arms stay low.
                float envelope=t<.3f ? Mathf.SmoothStep(0,1,t/.3f) : t>.73f ? Mathf.SmoothStep(1,0,(t-.73f)/.27f) : 1;
                float examination=Mathf.Sin(Mathf.Clamp01((t-.3f)/.43f)*Mathf.PI);
                position += new Vector3(-.025f,-.065f,.025f)*envelope;
                rotation += new Vector3(-5,13+examination*7,-23)*envelope;
            }
            if(IsReloading && !clipValid[5])
            { float progress=ReloadProgress; float amount=Mathf.Sin(progress*Mathf.PI); position+=new Vector3(-.025f,-.13f,-.06f)*amount; rotation+=new Vector3(12,-8,-12)*amount; }
            if(IsSwitching)
            {
                float t=1-switchRemaining/SwitchDuration;
                float lowered=t<.5f ? Mathf.SmoothStep(0,1,t*2) : Mathf.SmoothStep(1,0,(t-.5f)*2);
                position+=new Vector3(.03f,-.48f,-.15f)*lowered; rotation+=new Vector3(28,-8,-12)*lowered;
            }
            weaponPivot.localPosition = position;
            weaponPivot.localRotation = Quaternion.Euler(rotation);
            if(combatSidearm && combatSidearm.gameObject.activeInHierarchy)
                combatSidearm.AnimateCombat(ShotPulse,ReloadProgress);
            if(flash)
            {
                flash.enabled = HasWeapon && ShotPulse > .7f;
                flash.intensity = ShotPulse*4;
                if(muzzle) flash.transform.position = muzzle.position;
            }
            UpdateAnimation(dt);
        }

        void UpdateAnimation(float dt)
        {
            if(!graph.IsValid()) return;
            int selected = IsReloading && clipValid[5] ? 5 : IsInspecting && clipValid[4] ? 4 : fireTime > 0 && clipValid[3] ? 3 :
                IsSprinting && clipValid[2] ? 2 : Movement > .15f && clipValid[1] ? 1 : 0;
            float sum=0;
            for(int i=0;i<6;i++)
            {
                weights[i] = Mathf.Lerp(weights[i],selected==i && clipValid[i] ? 1 : 0,1-Mathf.Exp(-16*dt));
                sum += weights[i];
                if(clipValid[i] && i<3)
                {
                    AnimationClip clip = i==0 ? idleClip : i==1 ? walkClip : runClip;
                    if(clip.length>.01f && clips[i].GetTime()>=clip.length) clips[i].SetTime(clips[i].GetTime()%clip.length);
                    clips[i].SetSpeed(active ? 1 : 0);
                }
                else if(clipValid[i]) clips[i].SetSpeed(active ? 1 : 0);
            }
            for(int i=0;i<6;i++) mixer.SetInputWeight(i,sum>.0001f ? weights[i]/sum : 0);
            if(secondaryMixer.IsValid())
            {
                int secondaryIndex=selected==5 ? 4 : selected==4 ? 0 : selected;
                int selectedSecondary=secondaryIndex<5 && secondaryValid[secondaryIndex] ? secondaryIndex : 0;
                for(int i=0;i<5;i++)
                {
                    secondaryMixer.SetInputWeight(i,i==selectedSecondary ? 1 : 0);
                    if(!secondaryValid[i]) continue;
                    var slot=Equipped;
                    var source=i==0 ? slot.secondaryIdle : i==1 ? slot.secondaryWalk : i==2 ? slot.secondaryRun : i==3 ? slot.secondaryFire : slot.secondaryReload;
                    if(i<3 && source.length>.01f && secondaryClips[i].GetTime()>=source.length) secondaryClips[i].SetTime(secondaryClips[i].GetTime()%source.length);
                    secondaryClips[i].SetSpeed(active ? 1 : 0);
                }
            }
        }

        void OnDestroy()
        {
            if(graph.IsValid()) graph.Destroy();
            casingEffects?.Dispose();
        }
    }
}
