using System.Collections.Generic;
using UnityEngine;

namespace ZeroSector.Campaign
{
    public sealed class CampaignEnemy : MonoBehaviour
    {
        public enum BossPhase { Hunt, Aim, Burst, Recovery, Mirror }
        public const float BossMaxHealth=235;
        static readonly Color GuardPulse=new(1,.55f,.19f), MirrorPulse=new(1,.22f,.4f), WarningColor=new(1,.65f,.2f);
        public CampaignGame game;
        public CharacterController body;
        public bool boss;
        public int number;
        public float health,shield,heat,sinceHit=10;
        public bool Dead => health<=0;
        public int Shots { get; private set; }
        public int Hits { get; private set; }
        public int Crouches { get; private set; }
        public int AutonomousShots { get; private set; }
        public int ReflectedShots { get; private set; }
        public BossPhase Phase { get; private set; }
        public string Intent { get; private set; }="ДЕЖУРСТВО";
        public bool Crouched => stance!=null && stance.IsCrouching;
        public Vector3 Velocity { get; private set; }
        public bool IsMirroring => boss && !Dead && Phase==BossPhase.Mirror;
        public bool AcceptsMirrorShot => IsMirroring && phaseTime>PlayerMirrorTimeline.Delay;
        public bool MirrorShotPending => IsMirroring && game.Mirror.PeekShot(out _);
        public float MirrorShotProgress => MirrorShotPending && game.Mirror.PeekShot(out var shot) ?
            Mathf.Clamp01((float)((game.Mirror.Now-shot.FiredAt)/PlayerMirrorTimeline.Delay)) : 0;
        public float WarningProgress => Phase==BossPhase.Aim ? 1-Mathf.Clamp01(phaseTime/aimDuration) : MirrorShotProgress;
        public bool IsWarning => Phase==BossPhase.Aim || MirrorShotPending;
        public string BossCue => Dead ? "ОТКЛЮЧЁН" : Phase switch
        {
            BossPhase.Aim => "НАВОДИТСЯ · УЙДИ С ЛИНИИ",
            BossPhase.Burst => "ОЧЕРЕДЬ",
            BossPhase.Recovery => game.CoreExposed ? "ОТКРЫТ ДЛЯ КОНТРАТАКИ" : "ПОЛЕ АКТИВНО",
            BossPhase.Mirror => MirrorShotPending ? "ОТРАЖЕНИЕ · УЙДИ С ЛИНИИ" : "ОТРАЖЕНИЕ · ЗАДЕРЖКА 0,5 С",
            _ => game.CoreExposed ? Intent : "ПОЛЕ АКТИВНО"
        };

        CommanderCrouch stance;
        RobotPresentation visual;
        Light beacon;
        LineRenderer warningRay;
        AudioSource weaponAudio;
        AudioClip pulseSound;
        Vector3 home, patrolPoint, lastSeen, flankPoint;
        float decision, repath, cooldown, react, damagePose, flankCooldown, patrolPause;
        float phaseTime, aimDuration, mirrorCooldown=5.5f, nextBossAttack, hiddenTime;
        int burstRemaining, pathIndex;
        bool hasContact, patrollingOut, flanking, crouchReplay;
        bool exposed, lockAim;
        int bossBursts;
        Vector3 lockedTarget, driveVelocity;
        readonly AdamCombatRead combatRead=new();
        List<Vector3> path=new();
        PlayerMirrorTimeline.Pose replayPose;
        readonly System.Random random=new();

        public void Initialize(CampaignGame owner,bool mirror,int id)
        {
            game=owner; boss=mirror; number=id;
            body=GetComponent<CharacterController>();
            if(!body) body=gameObject.AddComponent<CharacterController>();
            body.height=1.85f; body.radius=.36f; body.center=Vector3.up*.925f; body.stepOffset=.25f;
            body.skinWidth=.02f; body.minMoveDistance=0;
            stance=new CommanderCrouch(body,null,boss ? 1.34f : 1.05f);
            visual=GetComponentInChildren<RobotPresentation>();
            health=boss ? BossMaxHealth : 65;
            shield=boss ? 150 : 25;
            react=1.1f+number*.19f;
            home=transform.position;
            patrolPoint=home+transform.forward*2.5f;
            Intent=boss ? "ОЖИДАНИЕ СИГНАЛА" : "ПАТРУЛЬ";
            weaponAudio=gameObject.AddComponent<AudioSource>();
            weaponAudio.playOnAwake=false; weaponAudio.spatialBlend=1; weaponAudio.minDistance=3; weaponAudio.maxDistance=30;
            pulseSound=game.presentation.weapons.Find(slot=>slot.id=="UnityFpsSample.Terraformer")?.shotSound;

            if(!boss) return;
            beacon=new GameObject("ADAM / identification light",typeof(Light)).GetComponent<Light>();
            beacon.transform.SetParent(transform,false);
            beacon.transform.localPosition=new Vector3(0,1.75f,.3f);
            beacon.type=LightType.Point; beacon.range=3.4f; beacon.intensity=1.8f;
            beacon.color=new Color(.59f,.30f,1); beacon.shadows=LightShadows.None;
            var beam=Resources.Load<Material>("Doppelganger/PulseTrail");
            if(!beam) return;
            warningRay=new GameObject("ADAM / delayed shot sight line",typeof(LineRenderer)).GetComponent<LineRenderer>();
            warningRay.transform.SetParent(transform,false);
            warningRay.sharedMaterial=beam; warningRay.useWorldSpace=true; warningRay.positionCount=2;
            warningRay.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            warningRay.receiveShadows=false; warningRay.enabled=false;
        }

        public bool VisibleTo(Vector3 observer)
        {
            Vector3 destination=transform.TransformPoint(body.center);
            if(Physics.Linecast(observer,destination,out var hit,~0,QueryTriggerInteraction.Ignore))
                return hit.collider==body || hit.transform.IsChildOf(transform);
            return true;
        }

        public void Damage(float amount)
        {
            if(Dead) return;
            damagePose=.3f; sinceHit=0;
            if(boss && !game.CoreExposed)
            {
                game.Notify("Защитное поле поглощает огонь. Отключи оба контура охлаждения.",3);
                return;
            }
            if(boss)
            {
                shield=0;
                if(Phase==BossPhase.Recovery) amount*=1.18f;
            }
            float blocked=Mathf.Min(shield,amount);
            shield-=blocked;
            health=Mathf.Max(0,health-(amount-blocked));
            if(!Dead) return;

            body.enabled=false;
            if(beacon) beacon.enabled=false;
            if(weaponAudio) weaponAudio.Stop();
            var pickup=gameObject.AddComponent<CampaignInteractable>();
            pickup.id="loot"+number; pickup.title="Забрать боеприпасы и ремонтный комплект"; pickup.range=3;
            game.EnemyKilled(this);
        }

        public void Tick(float dt)
        {
            if(Dead)
            {
                Velocity=Vector3.zero;
                if(visual) { visual.SetAimTarget(Vector3.zero,false); visual.Tick(Vector3.zero,false,false,false,true); }
                return;
            }
            sinceHit+=dt; damagePose-=dt; react-=dt;
            heat=Mathf.Max(0,heat-dt*22);
            if(boss) TickBoss(dt);
            else TickGuard(dt);
        }

        public void ObservePlayerShot()
        {
            if(boss && !Dead && Vector3.Distance(game.player.transform.position,transform.position)<20)
                combatRead.RegisterShot();
        }

        void TickBoss(float dt)
        {
            Vector3 target=game.player.transform.TransformPoint(game.player.center);
            Vector3 toward=Vector3.ProjectOnPlane(target-transform.position,Vector3.up).normalized;
            Vector3 side=Vector3.Cross(Vector3.up,toward);
            float distance=FlatDistance(transform.position,target);
            bool visible=distance<31 && HasSight(transform.position+Vector3.up*(Crouched ? .8f : 1.52f),target);
            combatRead.Observe(dt,visible,game.PlayerVelocity,side,game.PlayerCrouched);
            hiddenTime=visible ? 0 : hiddenTime+dt;
            if(visible) { lastSeen=game.player.transform.position; hasContact=true; }
            else if(hasContact && hiddenTime>5) { hasContact=false; flanking=false; repath=0; }
            phaseTime-=dt; mirrorCooldown-=dt; nextBossAttack-=dt; repath-=dt; flankCooldown-=dt;
            if(game.CoreExposed && !exposed)
            {
                exposed=true; shield=0;
                EnterRecovery(.85f);
            }

            if(IsMirroring)
            {
                if(phaseTime>0) { TickMirror(dt); return; }
                game.Mirror.ClearPending();
                EnterRecovery(1.0f);
            }

            bool enraged=exposed && health<BossMaxHealth*.44f;
            bool shoot=false;
            switch(Phase)
            {
                case BossPhase.Recovery:
                    if(phaseTime<=0) { Phase=BossPhase.Hunt; repath=0; }
                    break;
                case BossPhase.Aim:
                    if(!lockAim)
                    {
                        if(!visible) { EnterRecovery(.6f); break; }
                        lockedTarget=combatRead.AimAt(target);
                        lockAim=phaseTime<=.36f;
                    }
                    if(phaseTime<=0) { Phase=BossPhase.Burst; phaseTime=0; }
                    break;
                case BossPhase.Burst:
                    if(phaseTime>0) break;
                    shoot=true;
                    FirePulse(lockedTarget,exposed ? (enraged ? 10 : 8) : 6,MirrorPulse);
                    AutonomousShots++;
                    if(--burstRemaining<=0)
                    {
                        bossBursts++;
                        EnterRecovery(enraged ? .85f : 1.15f);
                    }
                    else phaseTime=enraged ? .19f : .24f;
                    break;
                case BossPhase.Hunt:
                    if(visible && exposed && distance<22 && mirrorCooldown<=0 && (combatRead.RepeatedFire || bossBursts>=2))
                    { BeginMirror(); TickMirror(dt); return; }
                    if(visible && !flanking && distance<23 && react<=0 && nextBossAttack<=0)
                    {
                        if(combatRead.HoldingCover && flankCooldown<=0 && distance>6 && exposed) StartFlank(toward,side);
                        else
                        {
                            Phase=BossPhase.Aim; lockedTarget=combatRead.AimAt(target); lockAim=false;
                            phaseTime=aimDuration=enraged ? .64f : .89f;
                            burstRemaining=enraged ? 3 : 2;
                        }
                    }
                    break;
            }

            Vector3 goal=transform.position;
            float speed=0;
            if(Phase==BossPhase.Hunt)
            {
                if(flanking && (FlatDistance(transform.position,flankPoint)<.75f || flankCooldown<2.5f))
                { flanking=false; repath=0; }
                if(flanking) { goal=flankPoint; speed=3.8f; Intent="ОБХОД"; }
                else if(visible && distance<4.8f)
                { goal=transform.position-toward*3; speed=2.1f; Intent="ОТХОД"; }
                else if(visible && distance>11 || hasContact && !visible && FlatDistance(transform.position,lastSeen)>.9f)
                { goal=lastSeen; speed=3.65f; Intent=visible ? "СБЛИЖЕНИЕ" : "ПОИСК ПО СЛЕДУ"; }
                else if(!visible && (hiddenTime>3 || !hasContact))
                {
                    goal=patrollingOut ? patrolPoint : home;
                    if(FlatDistance(transform.position,goal)<.65f)
                    { patrollingOut=!patrollingOut; goal=patrollingOut ? patrolPoint : home; repath=0; }
                    speed=2.2f; Intent="ПАТРУЛЬ";
                }
                else Intent=combatRead.PredictingStrafe ? "ЧИТАЕТ ТВОЙ ШАГ" : "ВЫЖИДАЕТ";
            }

            stance.Tick(false,dt);
            Vector3 travel=speed>0 ? FollowPath(goal)*speed : Vector3.zero;
            driveVelocity=Vector3.MoveTowards(driveVelocity,travel,dt*(speed>0 ? 12 : 19));
            bool aiming=Phase==BossPhase.Aim || Phase==BossPhase.Burst || shoot;
            Vector3 aim=aiming ? lockedTarget : target;
            Vector3 heading=aiming || visible && speed<=2.2f ? Vector3.ProjectOnPlane(aim-transform.position,Vector3.up) : driveVelocity;
            TurnToward(heading,dt,aiming ? 300 : 225);
            MoveBody((driveVelocity+Vector3.down*3)*dt,dt);
            if(visual)
            {
                visual.SetAimTarget(aim,aiming || visible && speed<=2.2f);
                visual.Tick(Velocity,Crouched,shoot,damagePose>0,false);
            }
        }

        void StartFlank(Vector3 toward,Vector3 side)
        {
            flankPoint=transform.position+side*(4.7f*combatRead.FlankSide)+toward*2.4f;
            flanking=true; flankCooldown=4.5f; repath=0;
        }

        void EnterRecovery(float duration)
        {
            Phase=BossPhase.Recovery; phaseTime=duration; nextBossAttack=duration+.38f;
            flanking=false;
        }

        void BeginMirror()
        {
            game.Mirror.ClearPending();
            Phase=BossPhase.Mirror; phaseTime=4.2f; mirrorCooldown=13.5f;
            bossBursts=0; crouchReplay=false; replayPose=default; driveVelocity=Vector3.zero;
            game.Notify("ADAM / ОТРАЖЕНИЕ. Сейчас он повторяет твои действия с задержкой.",3);
        }

        void TickMirror(float dt)
        {
            var mirror=game.Mirror;
            if(mirror==null) return;
            if(game.CoreExposed) shield=0;
            Vector3 displacement=Vector3.zero;
            bool playedPose=false;
            while(mirror.TakePose(out var pose))
            {
                replayPose=pose;
                displacement+=PlayerMirrorTimeline.Reflect(pose.Displacement);
                if(pose.Crouched && !crouchReplay) Crouches++;
                crouchReplay=pose.Crouched;
                playedPose=true;
            }
            stance.Tick(crouchReplay,dt);
            if(playedPose)
                TurnToward(PlayerMirrorTimeline.Reflect(replayPose.Facing),dt,460);
            if(replayPose.Grounded || !playedPose) displacement.y=Mathf.Max(0,displacement.y)-3*dt;
            MoveBody(displacement,dt);
            Intent=Crouched ? "ПРИСЕД" : Velocity.sqrMagnitude>.04f ? "ДВИЖЕНИЕ" : "СТОЙКА";

            bool fired=false;
            Vector3 aim=Muzzle()+PlayerMirrorTimeline.Reflect(replayPose.Look)*12;
            if(mirror.PeekShot(out var queued)) aim=queued.Target;
            while(mirror.TakeShot(out var shot))
            {
                aim=shot.Target;
                FirePulse(shot.Target,Mathf.Clamp(shot.Damage*.48f,5,12),MirrorPulse);
                ReflectedShots++;
                fired=true;
            }
            if(fired) Intent="ВЫСТРЕЛ";
            if(visual)
            {
                visual.SetAimTarget(aim,MirrorShotPending || fired || (playedPose && Velocity.magnitude<3.1f));
                visual.Tick(Velocity,Crouched,fired,damagePose>0,false,replayPose.Sprinting);
            }
        }

        void TickGuard(float dt)
        {
            cooldown-=dt; decision-=dt; repath-=dt; flankCooldown-=dt; patrolPause-=dt;
            if(sinceHit>4) shield=Mathf.Min(25,shield+dt*5);
            Vector3 target=game.player.transform.TransformPoint(game.player.center);
            Vector3 toPlayer=target-transform.position;
            float distance=Vector3.ProjectOnPlane(toPlayer,Vector3.up).magnitude;
            Vector3 toward=Vector3.ProjectOnPlane(toPlayer,Vector3.up).normalized;
            Vector3 observer=transform.position+Vector3.up*(Crouched ? .82f : 1.50f);
            bool visible=distance<25 && HasSight(observer,target);
            hiddenTime=visible ? 0 : hiddenTime+dt;
            if(visible)
            {
                lastSeen=game.player.transform.position;
                if(!hasContact)
                {
                    react=Mathf.Max(react,.28f+number%3*.12f);
                    repath=0;
                }
                hasContact=true;
            }
            else if(hasContact && (FlatDistance(transform.position,lastSeen)<1 || hiddenTime>4.5f))
            {
                hasContact=false;
                flanking=false;
                repath=0;
            }

            bool wantsCrouch=visible && health<23 && distance>=4 && distance<=12;
            stance.Tick(wantsCrouch,dt);
            if(Crouched && !crouchReplay) Crouches++;
            crouchReplay=Crouched;

            if(decision<=0)
            {
                decision=.5f+number*.07f;
                if(visible && number%2==1 && game.CommanderOrder!="ОТСТУПЛЕНИЕ" &&
                    flankCooldown<=0 && distance>6 && distance<16)
                {
                    var partner=game.Enemies.Find(other=>other && !other.Dead &&
                        other.number%2==0 && FlatDistance(other.transform.position,transform.position)<14);
                    if(partner)
                    {
                        Vector3 side=Vector3.Cross(Vector3.up,toward);
                        if(Vector3.Dot(side,transform.position-partner.transform.position)<0) side=-side;
                        if(!TryFlank(side,toward)) TryFlank(-side,toward);
                        flankCooldown=flanking ? 4.2f : 1.4f;
                    }
                }
            }
            if(flanking && (!visible || FlatDistance(transform.position,flankPoint)<.8f || flankCooldown<1.6f))
            {
                flanking=false; repath=0;
            }

            Vector3 goal=transform.position;
            float speed=0;
            if(visible)
            {
                if(distance<3.6f) { goal=transform.position-toward*2.7f; speed=1.75f; Intent="ОТХОД"; }
                else if(flanking) { goal=flankPoint; speed=3.9f; Intent="ОБХОД"; }
                else if(distance>12) { goal=lastSeen; speed=4.15f; Intent="СБЛИЖЕНИЕ"; }
                else Intent="ПРИЦЕЛ";
            }
            else if(hasContact) { goal=lastSeen; speed=3.35f; Intent="ПОИСК"; }
            else
            {
                goal=patrollingOut ? patrolPoint : home;
                if(FlatDistance(transform.position,goal)<.55f)
                {
                    patrollingOut=!patrollingOut; goal=patrollingOut ? patrolPoint : home; repath=0;
                    patrolPause=.45f+number%3*.22f;
                }
                speed=patrolPause>0 ? 0 : 1.55f; Intent="ПАТРУЛЬ";
            }
            bool holdingBurst=burstRemaining>0 && visible && distance<=13;
            if(holdingBurst || react>0) speed=0;
            Vector3 travel=speed>0 ? FollowPath(goal) : Vector3.zero;
            Vector3 desiredVelocity=travel*speed;
            if(speed>2 && travel.sqrMagnitude>.01f)
                desiredVelocity*=Mathf.Lerp(.27f,1,Mathf.Clamp01((Vector3.Dot(transform.forward,travel)+.3f)/1.3f));
            driveVelocity=Vector3.MoveTowards(driveVelocity,desiredVelocity,(speed==0 ? 15 : 11)*dt);
            Vector3 heading=visible && speed<=2 ? toward : travel;
            TurnToward(heading,dt,speed>2 ? 285 : 210);
            MoveBody((driveVelocity+Vector3.down*3)*dt,dt);

            bool aim=visible && speed<=2 && react<.45f && distance<17;
            bool canShootFacing=Vector3.Dot(transform.forward,toward)>.86f;
            bool shooting=aim && canShootFacing && cooldown<=0 && heat<80 && (!visual || visual.AimAmount>.56f);
            if(shooting)
            {
                if(burstRemaining==0) burstRemaining=2;
                float spread=distance*.018f;
                Vector3 error=new((float)(random.NextDouble()-.5)*spread,(float)(random.NextDouble()-.5)*spread,0);
                FirePulse(target+error,6,GuardPulse);
                cooldown=--burstRemaining>0 ? .19f : .85f+number*.14f+(float)random.NextDouble()*.25f;
                Intent="ОГОНЬ";
            }
            else if(!aim && burstRemaining>0)
            {
                burstRemaining=0;
                cooldown=Mathf.Max(cooldown,.25f);
            }
            if(visual)
            {
                visual.SetAimTarget(target,aim);
                visual.Tick(Velocity,Crouched,shooting,damagePose>0,false,speed>=3.8f && !aim);
            }
        }

        bool TryFlank(Vector3 side,Vector3 toward)
        {
            var candidate=transform.position+side*3.5f+toward*1.6f;
            var route=game.Navigation.Path(transform.position,candidate);
            if(route.Count<2 || FlatDistance(route[route.Count-1],candidate)>1.4f ||
                FlatDistance(route[route.Count-1],game.player.transform.position)<3.5f) return false;
            flankPoint=route[route.Count-1];
            path=route; pathIndex=0; repath=.55f;
            flanking=true;
            return true;
        }

        bool HasSight(Vector3 from,Vector3 target)
        {
            if(!Physics.Linecast(from,target,out var hit,~0,QueryTriggerInteraction.Ignore)) return true;
            return hit.collider==game.player;
        }

        Vector3 FollowPath(Vector3 goal)
        {
            if(repath<=0)
            {
                path=game.Navigation.Path(transform.position,goal); pathIndex=0; repath=.55f;
            }
            while(pathIndex<path.Count && FlatDistance(transform.position,path[pathIndex])<.55f) pathIndex++;
            Vector3 next=pathIndex<path.Count ? path[pathIndex] : goal;
            Vector3 direction=Vector3.ProjectOnPlane(next-transform.position,Vector3.up).normalized;
            if(pathIndex>=path.Count && Physics.SphereCast(transform.position+Vector3.up*.72f,.27f,direction,out _,.72f,~0,QueryTriggerInteraction.Ignore))
                return Vector3.zero;
            return direction;
        }

        static float FlatDistance(Vector3 from,Vector3 to) => Vector3.ProjectOnPlane(to-from,Vector3.up).magnitude;

        void TurnToward(Vector3 heading,float dt,float speed)
        {
            if(heading.sqrMagnitude>.01f)
                transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(heading),dt*speed);
        }

        void MoveBody(Vector3 displacement,float dt)
        {
            Vector3 previous=transform.position;
            body.Move(displacement);
            Velocity=Vector3.ProjectOnPlane(transform.position-previous,Vector3.up)/Mathf.Max(.001f,dt);
        }

        Vector3 Muzzle() => visual ? visual.MuzzlePosition : transform.position+Vector3.up*(Crouched ? .86f : 1.45f)+transform.forward*.46f;

        void FirePulse(Vector3 target,float damage,Color color)
        {
            Vector3 origin=Muzzle(), direction=(target-origin).normalized;
            if(direction.sqrMagnitude<.01f) direction=transform.forward;
            Vector3 end=origin+direction*40;
            bool hitsPlayer=false;
            bool hitSurface=Physics.Raycast(origin,direction,out var hit,40,~0,QueryTriggerInteraction.Ignore);
            if(hitSurface)
            {
                end=hit.point;
                hitsPlayer=hit.collider==game.player;
                if(hitsPlayer) Hits++;
            }
            end=game.ResolveIncomingShot(damage,origin,end,hitsPlayer);
            if(hitSurface && !hitsPlayer && (end-hit.point).sqrMagnitude<.000001f)
                game.MarkBulletHit(hit,.11f);
            game.Beam(origin,end,color);
            if(pulseSound) weaponAudio.PlayOneShot(pulseSound,boss ? .52f : .26f);
            Shots++; heat+=14;
        }

        void LateUpdate()
        {
            if(!boss || !warningRay) return;
            warningRay.enabled=false;
            if(!game || !game.Running || Dead || !IsWarning) return;
            Vector3 from=Muzzle(),end=lockedTarget;
            if(MirrorShotPending && game.Mirror.PeekShot(out var shot)) end=shot.Target;
            if(Physics.Linecast(from,end,out var hit,~0,QueryTriggerInteraction.Ignore)) end=hit.point;
            warningRay.enabled=true;
            warningRay.SetPosition(0,from); warningRay.SetPosition(1,end);
            warningRay.startWidth=.034f; warningRay.endWidth=.016f;
            Color color=WarningColor;
            color.a=Mathf.Lerp(.28f,.88f,WarningProgress);
            warningRay.startColor=warningRay.endColor=color;
        }
    }
}
