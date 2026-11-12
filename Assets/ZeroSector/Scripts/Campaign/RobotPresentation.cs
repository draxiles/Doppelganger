using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ZeroSector.Campaign
{
    /// <summary>Animation and weapon presentation for the guard chassis and the commander's double.</summary>
    public sealed class RobotPresentation : MonoBehaviour
    {
        public Animator animator;
        public AnimationClip idleClip, walkClip;
        public AnimationClip runClip, strafeRightClip, strafeLeftClip, backpedalClip, sprintClip, aimClip, fireClip;
        public Transform model, hips, leftUpperLeg, leftLowerLeg, leftFoot;
        public Transform rightUpperLeg, rightLowerLeg, rightFoot, rightHand, muzzle;
        public Transform spine, chest, neck, head, rightUpperArm, rightLowerArm;
        public Transform leftUpperArm, leftLowerArm, leftHand, foregrip, guardCannon;
        public bool twoHandedWeapon;
        public float walkMetersPerSecond = 1.55f;
        public float standingHeight = 1.8f;
        [Tooltip("Measured sole clearance after retargeting Guard idle to ADAM.")]
        public float soleHeightCorrection = .04f;
        public float PlanarSpeed { get; private set; }
        public float LocomotionWeight => speedWeight;
        public float RunAmount => runWeight;
        public float SprintAmount => sprintWeight;
        public float CrouchAmount => crouchWeight;
        public float AimAmount => aimWeight;
        public float ShotPoseAmount => layers.IsValid() && fire.IsValid() ? layers.GetInputWeight(2) : 0;
        public bool IsAnimating => graph.IsValid() && animator && walkClip && idleClip;
        public Vector3 MuzzlePosition => muzzle ? muzzle.position : transform.position + transform.up * 1.4f;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationLayerMixerPlayable layers;
        AvatarMask upperBody;
        AnimationClipPlayable[] locomotion;
        AnimationClipPlayable aim, fire;
        AnimationClip[] locomotionClips;
        const int Idle=0, Walk=1, Run=2, StrafeRight=3, StrafeLeft=4, Backpedal=5, Sprint=6;
        Vector3 velocity, modelPosition, aimTarget, previousForward;
        Vector3 previousLeftFoot, previousRightFoot, plantedFoot, cannonRest;
        Quaternion plantedRotation;
        Quaternion modelRotation;
        Quaternion headAimOffset=Quaternion.identity;
        Quaternion leftFlatFoot=Quaternion.identity,rightFlatFoot=Quaternion.identity;
        Vector3 leftRestFoot,rightRestFoot,leftSolePoint,rightSolePoint;
        readonly RaycastHit[] deckHits=new RaycastHit[8];
        float crouchWeight, speedWeight, runWeight, sprintWeight, hitPulse, firePulse, deathWeight, aimWeight;
        float fireElapsed=1, turnRate, plantTime;
        double gaitPhase, idleTime;
        Vector2 gaitDirection=Vector2.up;
        float lastTick;
        bool crouching, dead, aiming, wasDamaged, sprinting, wasStepping, plantLeft, cinematicBrace;
        float braceAmount;
        CampaignEnemy owner;
        Light muzzleLight;
        Renderer[] renderers;
        MaterialPropertyBlock block;

        void Awake()
        {
            block=new MaterialPropertyBlock();
            if (!model) model = transform;
            modelPosition = model.localPosition-Vector3.up*soleHeightCorrection; modelRotation = model.localRotation;
            model.localPosition=modelPosition;
            renderers = GetComponentsInChildren<Renderer>(true);
            if (muzzle) muzzleLight = muzzle.GetComponent<Light>();
            if (guardCannon) cannonRest = guardCannon.localPosition;
            previousForward = transform.forward;
            BindHumanoidBones();
            if(head) headAimOffset=Quaternion.Inverse(Quaternion.LookRotation(head.InverseTransformDirection(transform.forward),head.InverseTransformDirection(transform.up)));
            if (animator && walkClip)
            {
                LoadCombatClips();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.fireEvents = false;
                animator.runtimeAnimatorController = null;
                graph = PlayableGraph.Create("Combatant / locomotion and upper body");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "ADAM body", animator);
                locomotionClips=new[]{idleClip ? idleClip : walkClip, walkClip, runClip, strafeRightClip, strafeLeftClip, backpedalClip, sprintClip};
                locomotion=new AnimationClipPlayable[locomotionClips.Length];
                mixer=AnimationMixerPlayable.Create(graph,locomotionClips.Length);
                for(int i=0;i<locomotion.Length;i++)
                {
                    if(!locomotionClips[i]) continue;
                    locomotion[i]=MakePlayable(locomotionClips[i],true);
                    graph.Connect(locomotion[i],0,mixer,i);
                }
                mixer.SetInputWeight(Idle,1);
                if(animator.isHuman && (aimClip || fireClip))
                {
                    upperBody=new AvatarMask();
                    for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++) upperBody.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
                    foreach(var part in new[]{AvatarMaskBodyPart.Body,AvatarMaskBodyPart.Head,AvatarMaskBodyPart.LeftArm,
                        AvatarMaskBodyPart.RightArm,AvatarMaskBodyPart.LeftFingers,AvatarMaskBodyPart.RightFingers})
                        upperBody.SetHumanoidBodyPartActive(part,true);
                    layers=AnimationLayerMixerPlayable.Create(graph,3);
                    graph.Connect(mixer,0,layers,0); layers.SetInputWeight(0,1);
                    if(aimClip && twoHandedWeapon)
                    {
                        aim=MakePlayable(aimClip,false); graph.Connect(aim,0,layers,1);
                        layers.SetLayerMaskFromAvatarMask(1,upperBody);
                    }
                    if(fireClip)
                    {
                        fire=MakePlayable(fireClip,false); graph.Connect(fire,0,layers,2);
                        layers.SetLayerMaskFromAvatarMask(2,upperBody);
                    }
                    output.SetSourcePlayable(layers);
                }
                else output.SetSourcePlayable(mixer);
                graph.Play(); graph.Evaluate(0);
                model.SetLocalPositionAndRotation(modelPosition,modelRotation);
            }
            if(leftFoot) { leftRestFoot=transform.InverseTransformPoint(leftFoot.position); leftFlatFoot=Quaternion.Inverse(transform.rotation)*leftFoot.rotation; }
            if(rightFoot) { rightRestFoot=transform.InverseTransformPoint(rightFoot.position); rightFlatFoot=Quaternion.Inverse(transform.rotation)*rightFoot.rotation; }
            if(twoHandedWeapon && leftFoot && rightFoot) MeasureBootSoles();
        }

        void LoadCombatClips()
        {
            if(!animator.isHuman) return;
            AnimationClip Load(AnimationClip clip,string name) => clip ? clip : Resources.Load<AnimationClip>("Doppelganger/CombatMotion/"+name);
            idleClip=Load(idleClip,"Idle"); runClip=Load(runClip,"Run");
            strafeRightClip=Load(strafeRightClip,"StrafeRight"); strafeLeftClip=Load(strafeLeftClip,"StrafeLeft");
            backpedalClip=Load(backpedalClip,"Backpedal"); sprintClip=Load(sprintClip,"Sprint");
            aimClip=Load(aimClip,"Aim"); fireClip=Load(fireClip,"Fire");
        }

        AnimationClipPlayable MakePlayable(AnimationClip clip,bool feet)
        {
            var result=AnimationClipPlayable.Create(graph,clip);
            result.SetApplyFootIK(feet);
            result.SetSpeed(0);
            return result;
        }

        void BindHumanoidBones()
        {
            if(!animator || !animator.isHuman) return;
            Transform Bone(Transform assigned,HumanBodyBones id) => assigned ? assigned : animator.GetBoneTransform(id);
            hips=Bone(hips,HumanBodyBones.Hips); spine=Bone(spine,HumanBodyBones.Spine);
            chest=Bone(chest,HumanBodyBones.Chest); neck=Bone(neck,HumanBodyBones.Neck); head=Bone(head,HumanBodyBones.Head);
            leftUpperLeg=Bone(leftUpperLeg,HumanBodyBones.LeftUpperLeg); leftLowerLeg=Bone(leftLowerLeg,HumanBodyBones.LeftLowerLeg);
            rightUpperLeg=Bone(rightUpperLeg,HumanBodyBones.RightUpperLeg); rightLowerLeg=Bone(rightLowerLeg,HumanBodyBones.RightLowerLeg);
            leftFoot=Bone(leftFoot,HumanBodyBones.LeftFoot); rightFoot=Bone(rightFoot,HumanBodyBones.RightFoot);
            rightUpperArm=Bone(rightUpperArm,HumanBodyBones.RightUpperArm); rightLowerArm=Bone(rightLowerArm,HumanBodyBones.RightLowerArm);
            rightHand=Bone(rightHand,HumanBodyBones.RightHand);
            leftUpperArm=Bone(leftUpperArm,HumanBodyBones.LeftUpperArm); leftLowerArm=Bone(leftLowerArm,HumanBodyBones.LeftLowerArm);
            leftHand=Bone(leftHand,HumanBodyBones.LeftHand);
        }

        public void SetAimTarget(Vector3 worldPosition,bool aim)
        { aimTarget=worldPosition; aiming=aim; }

        /// <param name="velocity">Measured world displacement per second, after collision.</param>
        public void Tick(Vector3 velocity, bool crouch, bool shooting, bool damaged, bool dead, bool sprint=false)
        {
            this.velocity = Vector3.ProjectOnPlane(velocity,transform.up); crouching = crouch; this.dead = dead; sprinting=sprint;
            lastTick=Time.time;
            if (shooting) { firePulse = 1; fireElapsed=0; }
            if (damaged && !wasDamaged) hitPulse = 1;
            wasDamaged=damaged;
        }

        public void BraceForImpact(float amount)
        {
            cinematicBrace=true;
            braceAmount=Mathf.Clamp01(amount);
            Tick(Vector3.zero,false,false,false,false);
        }

        void LateUpdate()
        {
            if(!owner) owner=GetComponentInParent<CampaignEnemy>();
            // Pausing must also pause the pose rather than leave feet marching in place.
            if(owner && owner.game && !owner.game.Running) { if(muzzleLight) muzzleLight.enabled=false; return; }
            float dt = Mathf.Min(Time.deltaTime,.1f);
            if(Time.time-lastTick>.15f) velocity=Vector3.zero;
            hitPulse = Mathf.Max(0, hitPulse - dt / .28f); firePulse = Mathf.Max(0, firePulse - dt / .23f);
            crouchWeight = Mathf.MoveTowards(crouchWeight, crouching && !dead ? 1 : 0, dt * 3.8f);
            PlanarSpeed=velocity.magnitude;
            float blend=1-Mathf.Exp(-10*dt);
            float yawDelta=Vector3.SignedAngle(previousForward,transform.forward,transform.up);
            previousForward=transform.forward;
            turnRate=Mathf.Lerp(turnRate,dt>.0001f ? yawDelta/dt : 0,blend);
            speedWeight = Mathf.Lerp(speedWeight, dead ? 0 : Mathf.SmoothStep(0,1,Mathf.InverseLerp(.04f,.75f,PlanarSpeed)),blend);
            float targetRun=runClip && !dead ? Mathf.SmoothStep(0,1,Mathf.InverseLerp(2.15f,3.35f,PlanarSpeed))*(1-crouchWeight) : 0;
            runWeight=Mathf.Lerp(runWeight,targetRun,blend);
            Vector3 localVelocity=transform.InverseTransformDirection(velocity);
            float targetSprint=sprintClip && sprinting && !dead && localVelocity.z>Mathf.Abs(localVelocity.x) ? targetRun : 0;
            sprintWeight=Mathf.Lerp(sprintWeight,targetSprint,blend);
            aimWeight = Mathf.MoveTowards(aimWeight,aiming && !dead ? 1 : 0,dt * (aiming ? 4.5f : 3));
            deathWeight = Mathf.MoveTowards(deathWeight, dead ? 1 : 0, dt * 1.8f);
            if (graph.IsValid())
            {
                EvaluateLocomotion(dt,localVelocity);
            }
            if (model)
            {
                // Humanoid root translation must never pull the mesh away from its controller.
                model.localPosition = modelPosition + Vector3.down * (.34f * deathWeight);
                model.localRotation = modelRotation * Quaternion.Euler(0, 0, -82 * deathWeight);
            }
            if(deathWeight<.01f)
            {
                if(cinematicBrace) ApplyImpactBrace();
                else
                {
                    ApplyCrouch();
                    ApplyFootPlant(dt);
                    ApplyAimAndReaction();
                }
                if(twoHandedWeapon) GroundBoots(cinematicBrace || PlanarSpeed<.08f);
            }
            if (muzzleLight) muzzleLight.enabled = firePulse > .5f && !dead;
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                // Only an additive damage flash; original per-material textures stay assigned.
                if (hitPulse > 0)
                {
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_EmissionColor", new Color(1.8f, .12f, .04f)*hitPulse);
                    renderer.SetPropertyBlock(block);
                }
                else renderer.SetPropertyBlock(null);
            }
        }

        static double Wrap(double time,double length) => length>.001 ? time-System.Math.Floor(time/length)*length : 0;

        void EvaluateLocomotion(float dt,Vector3 localVelocity)
        {
            Vector2 direction=new(localVelocity.x,localVelocity.z);
            float turnStep=dead ? 0 : Mathf.SmoothStep(0,.48f,Mathf.InverseLerp(35,155,Mathf.Abs(turnRate)))*(1-speedWeight)*(1-crouchWeight);
            if(direction.sqrMagnitude>.025f) gaitDirection=Vector2.Lerp(gaitDirection,direction.normalized,1-Mathf.Exp(-13*dt));
            else if(turnStep>.001f) gaitDirection=Vector2.Lerp(gaitDirection,new Vector2(Mathf.Sign(turnRate),0),1-Mathf.Exp(-18*dt));
            float axes=Mathf.Abs(gaitDirection.x)+Mathf.Abs(gaitDirection.y);
            float sideways=axes>.01f ? Mathf.Abs(gaitDirection.x)/axes : 0;
            bool reverse=gaitDirection.y<-.10f;
            int strafe=gaitDirection.x>=0 ? StrafeRight : StrafeLeft;
            int advance=reverse ? Backpedal : Run;
            if(!locomotionClips[strafe]) sideways=0;
            if(!locomotionClips[advance]) advance=Run;
            // The directional source cycles are also used for controlled sidesteps and backing up.
            // Phase follows distance travelled, so retreat does not look like a forward walk or a run on the spot.
            float directionalStep=Mathf.Max(sideways,reverse && backpedalClip ? 1 : 0)*(1-.32f*crouchWeight);
            float weightRun=speedWeight*Mathf.Max(runWeight,directionalStep*.88f);
            float weightSprint=speedWeight*Mathf.Min(sprintWeight,runWeight);
            float remainingRun=weightRun-weightSprint;
            for(int i=0;i<locomotion.Length;i++) if(locomotion[i].IsValid()) mixer.SetInputWeight(i,0);
            float pivotWeight=runClip && locomotionClips[strafe] ? turnStep : 0;
            mixer.SetInputWeight(Idle,1-speedWeight-pivotWeight);
            mixer.SetInputWeight(Walk,runClip ? speedWeight-weightRun : speedWeight);
            if(runClip)
            {
                mixer.SetInputWeight(advance,remainingRun*(1-sideways));
                if(locomotionClips[strafe]) mixer.SetInputWeight(strafe,remainingRun*sideways+pivotWeight);
            }
            if(sprintClip) mixer.SetInputWeight(Sprint,weightSprint);

            if(!dead)
            {
                idleTime+=dt;
                float walkStride=walkClip.length*Mathf.Max(.2f,walkMetersPerSecond);
                float runStride=runClip ? runClip.length*3.55f : walkStride;
                float sprintStride=sprintClip ? sprintClip.length*5.8f : runStride;
                float stride=Mathf.Lerp(Mathf.Lerp(walkStride,runStride,Mathf.Max(runWeight,directionalStep)),sprintStride,sprintWeight);
                float travel=Mathf.Max(PlanarSpeed,turnStep*Mathf.Abs(turnRate)*Mathf.Deg2Rad*.42f);
                gaitPhase+=dt*travel/Mathf.Max(.12f,stride);
            }
            locomotion[Idle].SetTime(Wrap(idleTime,locomotionClips[Idle].length));
            locomotion[Walk].SetTime(Wrap(gaitPhase,1)*walkClip.length);
            for(int i=Run;i<locomotion.Length;i++)
                if(locomotion[i].IsValid()) locomotion[i].SetTime(Wrap(gaitPhase,1)*locomotionClips[i].length);

            if(layers.IsValid())
            {
                if(aim.IsValid()) layers.SetInputWeight(1,aimWeight*(1-sprintWeight*.85f));
                if(fire.IsValid())
                {
                    const float shotAnimationSeconds=.28f;
                    float progress=Mathf.Clamp01(fireElapsed/shotAnimationSeconds);
                    fire.SetTime(progress*fireClip.length);
                    float recoil=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.20f,1,progress));
                    layers.SetInputWeight(2,dead ? 0 : recoil*(twoHandedWeapon ? .9f : .72f));
                    fireElapsed+=dt;
                }
            }
            graph.Evaluate(0);
        }

        void ApplyFootPlant(float dt)
        {
            if(!leftFoot || !rightFoot) return;
            bool stepping=PlanarSpeed>.24f || Mathf.Abs(turnRate)>48;
            if(stepping) plantTime=0;
            else if(wasStepping)
            {
                plantLeft=Vector3.Dot(previousLeftFoot-previousRightFoot,transform.up)<0;
                Transform foot=plantLeft ? leftFoot : rightFoot;
                plantedFoot=plantLeft ? previousLeftFoot : previousRightFoot;
                plantedRotation=foot.rotation;
                plantTime=.24f;
            }
            wasStepping=stepping;
            if(plantTime>0)
            {
                Transform foot=plantLeft ? leftFoot : rightFoot;
                float weight=Mathf.SmoothStep(0,1,plantTime/.24f);
                Vector3 target=foot.position+Vector3.ClampMagnitude(plantedFoot-foot.position,.12f)*weight;
                SolveLeg(plantLeft ? leftUpperLeg : rightUpperLeg,plantLeft ? leftLowerLeg : rightLowerLeg,foot,
                    target,Quaternion.Slerp(foot.rotation,plantedRotation,weight*.6f));
                plantTime=Mathf.Max(0,plantTime-dt);
            }
            previousLeftFoot=leftFoot.position;
            previousRightFoot=rightFoot.position;
        }

        void ApplyCrouch()
        {
            if (hips && crouchWeight > .001f && deathWeight < .01f)
            {
                // Preserve animated foot targets and bend actual leg bones. No whole-body squash.
                Vector3 l = leftFoot ? leftFoot.position : Vector3.zero;
                Vector3 r = rightFoot ? rightFoot.position : Vector3.zero;
                Quaternion lRotation = leftFoot ? leftFoot.rotation : Quaternion.identity;
                Quaternion rRotation = rightFoot ? rightFoot.rotation : Quaternion.identity;
                // A crouched step is shorter and flatter than the upright film walk.
                // Preserve its lift while keeping toes from rolling through the deck.
                Vector3 ShortStep(Vector3 target,Vector3 rest)
                {
                    var point=transform.InverseTransformPoint(target);
                    point.x=Mathf.Lerp(point.x,rest.x,crouchWeight*.55f);
                    point.z=Mathf.Lerp(point.z,rest.z,crouchWeight*.55f);
                    return transform.TransformPoint(point);
                }
                l=ShortStep(l,leftRestFoot); r=ShortStep(r,rightRestFoot);
                lRotation=Quaternion.Slerp(lRotation,transform.rotation*leftFlatFoot,crouchWeight);
                rRotation=Quaternion.Slerp(rRotation,transform.rotation*rightFlatFoot,crouchWeight);
                // The commander's proportionally shorter legs need a shallower
                // crouch than the guard chassis to keep the thighs off the deck.
                float drop=standingHeight*(twoHandedWeapon ? .195f : .29f);
                hips.position -= transform.up * (drop * crouchWeight);
                hips.position -= transform.forward * (.08f*crouchWeight);
                SolveLeg(leftUpperLeg, leftLowerLeg, leftFoot, l, lRotation);
                SolveLeg(rightUpperLeg, rightLowerLeg, rightFoot, r, rRotation);
            }
        }

        void ApplyImpactBrace()
        {
            if(hips && leftFoot && rightFoot)
            {
                Vector3 left=leftFoot.position,right=rightFoot.position;
                Quaternion leftRotation=leftFoot.rotation,rightRotation=rightFoot.rotation;
                hips.position+=(-transform.up*.12f+transform.right*.06f+transform.forward*.095f)*braceAmount;
                SolveLeg(leftUpperLeg,leftLowerLeg,leftFoot,left,leftRotation);
                SolveLeg(rightUpperLeg,rightLowerLeg,rightFoot,right,rightRotation);
            }
            if(chest)
                chest.rotation=Quaternion.AngleAxis(-12*braceAmount,transform.forward)*
                    Quaternion.AngleAxis(21*braceAmount,transform.right)*chest.rotation;
            if(head) head.rotation=Quaternion.AngleAxis(-12*braceAmount,transform.right)*head.rotation;
            BraceArm(leftUpperArm,leftLowerArm,leftHand,-1);
            BraceArm(rightUpperArm,rightLowerArm,rightHand,1);
        }

        void BraceArm(Transform upper,Transform lower,Transform hand,float side)
        {
            if(!upper || !lower || !hand) return;
            float spread=.15f+braceAmount*(side>0 ? .18f : .08f);
            float reach=.17f+braceAmount*(side>0 ? .23f : .11f);
            float hang=.38f-braceAmount*.23f;
            Vector3 target=upper.position+transform.right*(side*spread)+transform.forward*reach-transform.up*hang;
            SolveLimb(upper,lower,hand,target,hand.rotation,-transform.up+transform.right*(side*.4f));
        }

        void MeasureBootSoles()
        {
            var points=new Vector3[2];
            points[0]=leftFoot.position-Vector3.up*.075f;
            points[1]=rightFoot.position-Vector3.up*.075f;
            var heights=new[]{float.PositiveInfinity,float.PositiveInfinity};
            var vertices=new System.Collections.Generic.List<Vector3>();
            var baked=new Mesh();
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones=skin.bones;
                if(Array.IndexOf(bones,leftFoot)<0 && Array.IndexOf(bones,rightFoot)<0) continue;
                skin.BakeMesh(baked);
                baked.GetVertices(vertices);
                foreach(Vector3 vertex in vertices)
                {
                    Vector3 point=skin.transform.TransformPoint(vertex);
                    float leftDistance=(point-leftFoot.position).sqrMagnitude,rightDistance=(point-rightFoot.position).sqrMagnitude;
                    int index=leftDistance<=rightDistance ? 0 : 1;
                    if(Mathf.Min(leftDistance,rightDistance)>.115f || point.y>=heights[index]) continue;
                    heights[index]=point.y;
                    points[index]=point;
                }
            }
            Destroy(baked);
            leftSolePoint=leftFoot.InverseTransformPoint(points[0]);
            rightSolePoint=rightFoot.InverseTransformPoint(points[1]);
        }

        void GroundBoots(bool holdContact)
        {
            GroundBoot(leftUpperLeg,leftLowerLeg,leftFoot,leftSolePoint,holdContact);
            GroundBoot(rightUpperLeg,rightLowerLeg,rightFoot,rightSolePoint,holdContact);
        }

        void GroundBoot(Transform upper,Transform lower,Transform foot,Vector3 solePoint,bool holdContact)
        {
            if(!foot) return;
            Vector3 sole=foot.TransformPoint(solePoint);
            Vector3 origin=new(sole.x,transform.position.y+.22f,sole.z);
            int count=Physics.RaycastNonAlloc(origin,Vector3.down,deckHits,.44f,~0,QueryTriggerInteraction.Ignore);
            float floor=float.NegativeInfinity;
            for(int i=0;i<count;i++)
            {
                var hit=deckHits[i];
                if(hit.normal.y<.65f || hit.collider is CharacterController || hit.transform.IsChildOf(transform)) continue;
                floor=Mathf.Max(floor,hit.point.y);
            }
            if(!float.IsFinite(floor)) return;
            float lift=floor+.014f-sole.y;
            if((!holdContact && lift<=0) || Mathf.Abs(lift)<.004f) return;
            SolveLeg(upper,lower,foot,foot.position+Vector3.up*Mathf.Clamp(lift,-.10f,.24f),foot.rotation);
        }

        void ApplyAimAndReaction()
        {
            Vector3 local=transform.InverseTransformDirection(aimTarget-(chest ? chest.position : transform.position+Vector3.up));
            float yaw=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-75,75);
            float pitch=Mathf.Clamp(-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,-45,45);
            if(chest)
            {
                chest.rotation=Quaternion.AngleAxis(yaw*.45f*aimWeight,transform.up)*chest.rotation;
                chest.rotation=Quaternion.AngleAxis(pitch*.3f*aimWeight+crouchWeight*8+hitPulse*9-firePulse*5,transform.right)*chest.rotation;
                float turnLean=Mathf.Clamp(-turnRate*.035f,-8,8)*speedWeight;
                chest.rotation=Quaternion.AngleAxis(turnLean+hitPulse*6,transform.forward)*chest.rotation;
            }
            if(neck) neck.rotation=Quaternion.AngleAxis(yaw*.2f*aimWeight,transform.up)*neck.rotation;
            Vector3 direction=Quaternion.AngleAxis(yaw,transform.up)*Quaternion.AngleAxis(pitch,transform.right)*transform.forward;
            if(head)
            {
                head.rotation=Quaternion.Slerp(head.rotation,Quaternion.LookRotation(direction,transform.up)*headAimOffset,aimWeight*.9f);
            }
            if(!rightUpperArm || !rightLowerArm || !rightHand) return;
            if(!twoHandedWeapon)
            {
                AimGuardCannon(direction);
                return;
            }
            if(aimWeight<.001f || !muzzle) return;
            Quaternion handRotation=rightHand.rotation;
            Vector3 shotDirection=aimTarget-muzzle.position;
            if(shotDirection.sqrMagnitude>.01f)
            {
                Quaternion align=Quaternion.FromToRotation(muzzle.forward,shotDirection.normalized);
                rightHand.rotation=Quaternion.Slerp(handRotation,align*handRotation,aimWeight*.85f);
            }
            if(foregrip && leftUpperArm && leftLowerArm && leftHand)
            {
                Quaternion leftUpper=leftUpperArm.rotation,leftLower=leftLowerArm.rotation,leftWrist=leftHand.rotation;
                SolveLimb(leftUpperArm,leftLowerArm,leftHand,foregrip.position,leftWrist,-transform.up-transform.right*.3f);
                leftUpperArm.rotation=Quaternion.Slerp(leftUpper,leftUpperArm.rotation,aimWeight);
                leftLowerArm.rotation=Quaternion.Slerp(leftLower,leftLowerArm.rotation,aimWeight);
            }
        }

        void AimGuardCannon(Vector3 direction)
        {
            float recoil=firePulse*firePulse;
            // Low-ready stance keeps the patrol weapon below the sightline. It is
            // released during the run so the real arm swing still carries momentum.
            float ready=guardCannon ? Mathf.Lerp(.62f,.12f,Mathf.Max(runWeight,sprintWeight)) : 0;
            float weight=Mathf.Max(aimWeight,Mathf.Max(ready,recoil*.85f));
            if(weight<.001f) return;
            Vector3 lowered=(transform.forward-transform.up*.8f).normalized;
            Vector3 aimDirection=Vector3.Slerp(lowered,direction,Mathf.Max(aimWeight,recoil*.75f));
            Quaternion upperRotation=rightUpperArm.rotation,lowerRotation=rightLowerArm.rotation,handRotation=rightHand.rotation;
            float reach=Vector3.Distance(rightUpperArm.position,rightLowerArm.position)+Vector3.Distance(rightLowerArm.position,rightHand.position);
            float extension=Mathf.Lerp(.67f,.92f,aimWeight)-recoil*.17f;
            Vector3 goal=rightUpperArm.position+aimDirection*reach*extension+transform.right*(.07f*(1-aimWeight));
            SolveLimb(rightUpperArm,rightLowerArm,rightHand,goal,handRotation,-transform.up+transform.right*.45f);
            rightUpperArm.rotation=Quaternion.Slerp(upperRotation,rightUpperArm.rotation,weight);
            rightLowerArm.rotation=Quaternion.Slerp(lowerRotation,rightLowerArm.rotation,weight);
            if(muzzle)
            {
                Vector3 targetDirection=aimTarget-muzzle.position;
                if(aimWeight>.85f && targetDirection.sqrMagnitude>.01f && Vector3.Angle(direction,targetDirection)<24)
                    aimDirection=targetDirection.normalized;
                aimDirection=Quaternion.AngleAxis(-recoil*9,Vector3.Cross(transform.up,aimDirection))*aimDirection;
                Quaternion mountOrientation=Quaternion.Inverse(rightHand.rotation)*muzzle.rotation;
                Quaternion solved=Quaternion.LookRotation(aimDirection,transform.up)*Quaternion.Inverse(mountOrientation);
                rightHand.rotation=Quaternion.Slerp(rightHand.rotation,solved,weight);
            }
            if(guardCannon)
            {
                Vector3 localBore=guardCannon.localRotation*Vector3.forward;
                guardCannon.localPosition=cannonRest-localBore*(recoil*.055f);
            }
        }

        void SolveLeg(Transform upper, Transform lower, Transform foot, Vector3 target, Quaternion footRotation)
        { SolveLimb(upper,lower,foot,target,footRotation,transform.forward); }

        void SolveLimb(Transform upper, Transform lower, Transform foot, Vector3 target, Quaternion footRotation,Vector3 bendDirection)
        {
            if (!upper || !lower || !foot) return;
            Vector3 origin = upper.position;
            float a = Vector3.Distance(origin, lower.position), b = Vector3.Distance(lower.position, foot.position);
            if (a < .001f || b < .001f) return;
            Vector3 delta = target - origin;
            if(delta.sqrMagnitude<.000001f) return;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            Vector3 direction = delta.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(bendDirection, direction).normalized;
            if (pole.sqrMagnitude < .01f) pole = transform.right;
            float along = (a * a + distance * distance - b * b) / (2 * distance);
            float bend = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            Vector3 knee = origin + direction * along + pole * bend;
            upper.rotation = Quaternion.FromToRotation(lower.position - origin, knee - origin) * upper.rotation;
            Vector3 endpoint=origin+direction*distance;
            lower.rotation = Quaternion.FromToRotation(foot.position - lower.position, endpoint - lower.position) * lower.rotation;
            foot.rotation = footRotation;
        }

        void OnDestroy()
        {
            if(graph.IsValid()) graph.Destroy();
            if(upperBody) Destroy(upperBody);
        }
    }
}
