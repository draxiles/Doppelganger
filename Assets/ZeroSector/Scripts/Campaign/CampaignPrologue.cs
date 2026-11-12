using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroSector.Campaign
{
    /// <summary>The new-game cinematic owns its camera and restores the gameplay camera on either exit.</summary>
    public sealed partial class CampaignPrologue : MonoBehaviour
    {
        public const string ResourcePath="Doppelganger/ImpactPrologue";
        public const float Duration=17.8f;
        public const float ImpactAt=11.15f;
        public const float BoardingAt=12.05f;
        public const float BridgeAt=14.20f;
        const float AttackerAt=7.85f;
        const float KeyArtEnd=3.65f;
        const float HullDetailAt=5.15f;
        const float RamViewAt=8.05f;
        const int OrbitMask=1<<31;
        static readonly Vector3 StageOrigin=new(0,0,-10000);
        static readonly Vector3 Strike=new(11.7f,1.9f,-9.9f);
        static readonly Vector3 Approach=new(38,-9,-28);
        static readonly Vector3 ApproachDirection=Approach.normalized;
        static readonly Vector3 EarthPosition=new(-434,-407,63);
        static readonly Vector4 EarthSun=new(.32f,.59f,.74f,0);
        static readonly Vector4 EarthGrade=new(.79f,.82f,.55f,.82f);
        static readonly int SunDirectionProperty=Shader.PropertyToID("_SunDirection");
        static readonly int OrbitalGradeProperty=Shader.PropertyToID("_OrbitalGrade");

        [SerializeField] Camera lens;
        [SerializeField] AudioListener listener;
        [SerializeField] AudioSource soundtrack;
        [SerializeField] Texture2D keyArt;
        [SerializeField] Transform orbit, globe, ship, attacker, boardingDeck, impactGlow, breach;
        [SerializeField] Transform[] hotPieces=Array.Empty<Transform>(), engines=Array.Empty<Transform>();
        [SerializeField] RobotPresentation[] boarders=Array.Empty<RobotPresentation>();
        [SerializeField] ParticleSystem sparks;
        [SerializeField] Light orbitalSun, flash, boardingAlarm, emergency;

        Camera playerCamera;
        AudioListener playerListener;
        Action onComplete;
        Vector3 bridgePosition;
        Quaternion bridgeRotation;
        Transform commander;
        RobotPresentation commanderMotion;
        Vector3 commanderMark;
        Quaternion commanderFacing;
        bool playerCameraWasEnabled, playerListenerWasEnabled, impactPlayed, commanderPlaced, orbitStaged;
        float seconds, skipTime=-1;
        GUIStyle title, heading, caption, right;
        public bool Active { get; private set; }
        public bool ImpactOccurred => impactPlayed;
        public bool BoardersVisible => boardingDeck && boardingDeck.gameObject.activeSelf && boarders.Length>0;
        public bool BoardingOccurred { get; private set; }
        public bool CommanderVisible => commander && commander.gameObject.activeSelf;
        public Camera Lens => lens;

        public void PreviewBridge(Camera gameplay,float at)
        {
            if(Active || !gameplay) return;
            playerCamera=gameplay;
            bridgePosition=gameplay.transform.position;
            bridgeRotation=gameplay.transform.rotation;
            SampleBridge(at);
        }

        public static CampaignPrologue Begin(Camera gameplay,Action completed)
        {
            var prefab=Resources.Load<GameObject>(ResourcePath);
            if(!prefab || !gameplay) return null;
            var instance=Instantiate(prefab,StageOrigin,Quaternion.identity);
            var scene=instance.GetComponent<CampaignPrologue>();
            if(!scene || !scene.lens || !scene.listener || !scene.soundtrack || !scene.soundtrack.clip || !scene.ship || !scene.orbit)
            {
                Destroy(instance);
                return null;
            }
            scene.Play(gameplay,completed);
            return scene;
        }

        void Play(Camera gameplay,Action completed)
        {
            playerCamera=gameplay;
            playerListener=gameplay.GetComponent<AudioListener>();
            playerCameraWasEnabled=gameplay.enabled;
            playerListenerWasEnabled=playerListener && playerListener.enabled;
            bridgePosition=gameplay.transform.position;
            bridgeRotation=gameplay.transform.rotation;
            onComplete=completed;
            gameplay.enabled=false;
            if(playerListener) playerListener.enabled=false;
            lens.enabled=true;
            listener.enabled=true;
            Active=true;
            SampleOrbit(0);
            soundtrack.Play();
        }

        public void RequestSkip()
        {
            if(Active && skipTime<0) skipTime=0;
        }

        void Update()
        {
            if(!Active) return;
            var keys=Keyboard.current;
            if(seconds>.30f && keys!=null && (keys.escapeKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame ||
                keys.enterKey.wasPressedThisFrame)) RequestSkip();

            float dt=Time.unscaledDeltaTime;
            if(skipTime>=0)
            {
                skipTime+=dt;
                soundtrack.volume=1-Mathf.Clamp01(skipTime/.28f);
                if(skipTime>=.28f) Finish();
                return;
            }
            seconds+=dt;
            if(seconds>=Duration) { Finish(); return; }
            if(seconds<BoardingAt) SampleOrbit(seconds);
            else if(seconds<BridgeAt && boardingDeck)
            {
                SampleBoarding(seconds);
                BoardingOccurred |= BoardersVisible;
            }
            else SampleBridge(seconds);
            if(!impactPlayed && seconds>=ImpactAt)
            {
                impactPlayed=true;
                if(sparks && seconds<BoardingAt)
                {
                    sparks.transform.position=ship.TransformPoint(Strike);
                    sparks.transform.rotation=Quaternion.LookRotation(new Vector3(1,.8f,-.25f));
                    sparks.Play(true);
                }
            }
        }

        public void SampleOrbit(float at)
        {
            orbit.gameObject.SetActive(true);
            EnableOrbitLighting();
            if(boardingDeck) boardingDeck.gameObject.SetActive(false);
            lens.cullingMask=OrbitMask;
            lens.clearFlags=CameraClearFlags.SolidColor;
            lens.backgroundColor=new Color(.004f,.009f,.024f);
            lens.nearClipPlane=.3f;
            lens.farClipPlane=4250;
            if(emergency) emergency.enabled=false;

            float aftermath=Mathf.Max(0,at-ImpactAt);
            float recoil=Smooth(Mathf.Clamp01(aftermath*1.4f));
            float impactKick=(1-Mathf.Exp(-aftermath*20))*Mathf.Exp(-aftermath*4.2f);
            ship.localPosition=new Vector3(-4.5f+at*.80f-recoil*1.8f-impactKick*1.0f,
                Mathf.Sin(at*.61f)*.12f+recoil*.42f+impactKick*.52f,-16+at*2.72f+recoil*.68f);
            ship.localRotation=Quaternion.Euler(-1-recoil*3-impactKick*2, -5+recoil*6,
                Mathf.Sin(at*.48f)*.9f+recoil*9+impactKick*5);

            Vector3 offset, focus;
            if(at<HullDetailAt)
            {
                float u=Smooth(Mathf.InverseLerp(2.45f,HullDetailAt,at));
                offset=Vector3.Lerp(new Vector3(84,16,34),new Vector3(80,16,33),u);
                focus=Vector3.Lerp(new Vector3(1,-.2f,-12.1f),new Vector3(1,.05f,-10.9f),u);
                lens.fieldOfView=Mathf.Lerp(42.5f,41.8f,u);
            }
            else if(at<RamViewAt)
            {
                float u=Smooth(Mathf.InverseLerp(HullDetailAt,RamViewAt,at));
                offset=Vector3.Lerp(new Vector3(43,7,22),new Vector3(37,7,20),u);
                focus=Vector3.Lerp(new Vector3(4,1,6),new Vector3(4,.75f,3),u);
                lens.fieldOfView=43;
            }
            else
            {
                // Stay on the transport's starboard profile through contact. The cutter crosses
                // the empty lower-left part of the frame, leaving the bridge and bow clear.
                float u=Smooth(Mathf.InverseLerp(RamViewAt,BoardingAt,at));
                offset=Vector3.Lerp(new Vector3(82,17,31),new Vector3(78,17,33),u);
                focus=Vector3.Lerp(new Vector3(1,.25f,-11.2f),new Vector3(1,1,-10.5f),u);
                lens.fieldOfView=Mathf.Lerp(43,42.5f,u);
            }
            StageOrbitArt();
            float shakeFalloff=Mathf.Exp(-aftermath*6f);
            Vector3 shake=at<ImpactAt ? Vector3.zero : Noise3(aftermath,24)*shakeFalloff*1.12f+
                new Vector3(.2f,-.3f,.55f)*Mathf.Sin(aftermath*30)*shakeFalloff;
            var cameraPoint=ship.position+offset+shake;
            var target=ship.position+focus;
            lens.transform.SetPositionAndRotation(cameraPoint,Quaternion.LookRotation(target-cameraPoint)*
                Quaternion.Euler(shake.y*1.6f,shake.x*.95f,Mathf.Sin(at*.75f)*.36f+shake.z*2.1f));

            Vector3 collision=ship.TransformPoint(Strike);
            float inbound=Mathf.Clamp01((ImpactAt-at)/(ImpactAt-AttackerAt));
            if(attacker)
            {
                attacker.gameObject.SetActive(at>=AttackerAt);
                var heading=ship.TransformDirection(ApproachDirection);
                // The tip of the breaching collar sits 10.4 metres ahead of the shuttle origin.
                var latch=heading*(10.4f+inbound*Approach.magnitude);
                var roll=Quaternion.AngleAxis(-11f*inbound+Mathf.Sin(aftermath*20)*1.8f*Mathf.Exp(-aftermath*3),Vector3.forward);
                attacker.SetPositionAndRotation(collision+latch,Quaternion.LookRotation(-heading,ship.up)*roll);
            }
            for(int i=0;i<hotPieces.Length;i++)
            {
                var piece=hotPieces[i];
                if(!piece) continue;
                float a=i*2.39996f;
                Vector3 debris=new(Mathf.Cos(a)*.67f+.85f,Mathf.Sin(a)*.79f+.5f,-.52f+.09f*(i%7));
                bool hit=at>=ImpactAt;
                float spread=aftermath*(8.5f+i%5*1.7f);
                piece.gameObject.SetActive(hit);
                piece.position=collision+debris*(spread+.37f);
                piece.localRotation=Quaternion.Euler(a*17+at*99,at*76+a*71,at*42);
            }
            if(flash)
            {
                flash.transform.position=collision;
                flash.enabled=at>=ImpactAt && aftermath<1.2f;
                flash.intensity=96*Mathf.Exp(-aftermath*8.8f)+10*Mathf.Exp(-aftermath*2.2f);
            }
            if(impactGlow)
            {
                impactGlow.position=collision+new Vector3(.6f,.5f,-.23f);
                impactGlow.rotation=lens.transform.rotation;
                impactGlow.gameObject.SetActive(at>=ImpactAt && aftermath<.71f);
                impactGlow.localScale=Vector3.one*(1.7f+aftermath*13.5f)*Mathf.Exp(-aftermath*2.9f);
            }
            if(breach) breach.gameObject.SetActive(at>=ImpactAt);
            for(int i=0;i<engines.Length;i++)
            {
                if(!engines[i]) continue;
                float flicker=1+Mathf.Sin(at*(i+7)*8)*.07f;
                if(at>=ImpactAt && i>=engines.Length/2) flicker*=Mathf.Exp(-aftermath*4)*(.5f+Mathf.Sin(at*90)*.45f);
                engines[i].localScale=new Vector3(1,1,Mathf.Max(.01f,flicker));
            }
        }

        void StageOrbitArt()
        {
            if(orbitStaged) return;
            orbitStaged=true;
            ship.localScale=Vector3.Scale(ship.localScale,new Vector3(1,1.13f,1));
            if(!globe) return;
            // A fixed sphere retains real parallax between the wide and detail cameras.
            globe.localPosition=EarthPosition;
            var block=new MaterialPropertyBlock();
            foreach(var layer in globe.GetComponentsInChildren<Renderer>(true))
            {
                layer.GetPropertyBlock(block);
                block.SetVector(SunDirectionProperty,EarthSun);
                block.SetVector(OrbitalGradeProperty,EarthGrade);
                layer.SetPropertyBlock(block);
            }
        }

        public void SampleBoarding(float at)
        {
            if(!boardingDeck) return;
            RestoreOrbitLighting();
            if(orbit.gameObject.activeSelf) orbit.gameObject.SetActive(false);
            boardingDeck.gameObject.SetActive(true);
            if(emergency) emergency.enabled=false;
            lens.cullingMask=OrbitMask;
            lens.clearFlags=CameraClearFlags.SolidColor;
            lens.backgroundColor=Color.black;
            lens.nearClipPlane=.07f;
            lens.farClipPlane=48;
            lens.fieldOfView=48;
            float local=Mathf.Clamp(at-BoardingAt,0,BridgeAt-BoardingAt);
            var position=new Vector3(.87f-local*.20f,1.25f+local*.07f,-4.25f-local*.36f);
            var focus=new Vector3(-.15f,1.40f,1.04f-local*.58f);
            var tremor=Noise3(local,14)*Mathf.Exp(-local*2.3f)*.06f;
            lens.transform.SetPositionAndRotation(boardingDeck.TransformPoint(position+tremor),
                boardingDeck.rotation*Quaternion.LookRotation(focus-position)*Quaternion.Euler(0,0,-4+local*.95f));
            if(boardingAlarm) boardingAlarm.intensity=3.8f+3.5f*Mathf.Pow(Mathf.Max(0,Mathf.Sin(local*9.5f)),4);
            for(int i=0;i<boarders.Length;i++)
            {
                var actor=boarders[i];
                if(!actor) continue;
                float walk=Mathf.Max(0,local-i*.48f);
                float direction=i%2==0 ? -1 : 1;
                var mark=new Vector3(direction*(.61f+walk*.19f),0,2.36f+i*.53f-walk*1.64f);
                var motion=new Vector3(direction*.19f,0,-1.64f);
                actor.transform.SetLocalPositionAndRotation(mark,Quaternion.LookRotation(motion));
                if(Application.isPlaying)
                {
                    actor.SetAimTarget(boardingDeck.TransformPoint(new Vector3(direction*1.8f,1.44f,-7)),true);
                    actor.Tick(walk>0 ? boardingDeck.TransformDirection(motion) : Vector3.zero,false,false,false,false);
                }
#if UNITY_EDITOR
                else if(actor.animator && actor.walkClip)
                    actor.walkClip.SampleAnimation(actor.animator.gameObject,Mathf.Repeat(walk+.15f,actor.walkClip.length));
#endif
            }
        }

        void SampleBridge(float at)
        {
            RestoreOrbitLighting();
            if(orbit.gameObject.activeSelf) orbit.gameObject.SetActive(false);
            if(boardingDeck) boardingDeck.gameObject.SetActive(false);
            lens.cullingMask=playerCamera ? playerCamera.cullingMask : -1;
            lens.nearClipPlane=.07f;
            lens.farClipPlane=playerCamera ? playerCamera.farClipPlane : 1100;
            lens.fieldOfView=66;
            float hit=at-BridgeAt;
            var shake=Noise3(hit,21)*Mathf.Exp(-hit*1.65f)*.20f;
            if(emergency)
            {
                emergency.enabled=true;
                emergency.transform.position=bridgePosition+bridgeRotation*(Vector3.forward*2.8f+Vector3.up*.65f);
                emergency.intensity=7+Mathf.Pow(Mathf.Max(0,Mathf.Sin(hit*8.4f+.4f)),3)*22;
            }
            if(Application.isPlaying && !commanderPlaced) PlaceCommander();
            if(commander && hit<1.45f)
            {
                float stumble=Smooth(Mathf.Clamp01((hit-.08f)/.73f));
                commanderMotion.BraceForImpact(stumble);
                var view=commanderMark+commanderFacing*new Vector3(1.92f,1.66f,3.02f-hit*.30f)+shake*.28f;
                lens.fieldOfView=43;
                lens.transform.SetPositionAndRotation(view,Quaternion.LookRotation(commanderMark+Vector3.up*(1.35f-.12f*stumble)-view)
                    *Quaternion.Euler(shake.y*5,0,-3*stumble+shake.z*7));
                return;
            }
            if(commander && commander.gameObject.activeSelf) commander.gameObject.SetActive(false);
            float fall=hit-(commander ? 1.45f : .35f);
            float collapse=Smooth(Mathf.Clamp01(fall/1.06f));
            var position=bridgePosition+bridgeRotation*new Vector3(-.27f*collapse,-1.09f*collapse,.50f*collapse)+shake;
            var rotation=bridgeRotation*Quaternion.Euler(4+32*collapse+shake.y*18,5-10*collapse+shake.x*20,-22*collapse+shake.z*21);
            lens.transform.SetPositionAndRotation(position,rotation);
        }

        void PlaceCommander()
        {
            commanderPlaced=true;
            if(!playerCamera) return;
            var owner=playerCamera.GetComponentInParent<CharacterController>();
            Vector3 feet=owner ? owner.transform.position : bridgePosition-Vector3.up*1.65f;
            var forward=Vector3.ProjectOnPlane(bridgeRotation*Vector3.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.5f) forward=Vector3.forward;
            commanderFacing=Quaternion.LookRotation(forward)*Quaternion.Euler(0,-13,0);
            commanderMark=feet+commanderFacing*new Vector3(-.12f,0,.36f);
            var actor=new GameObject("Алекс / пролог");
            actor.transform.SetParent(transform,false);
            actor.transform.SetPositionAndRotation(commanderMark,commanderFacing);
            commanderMotion=CommanderAvatar.Attach(actor.transform,false);
            if(!commanderMotion) { Destroy(actor); return; }
            commander=actor.transform;
            foreach(var item in commanderMotion.GetComponentsInChildren<Transform>(true))
                if(item.name.StartsWith("СКАТ /",StringComparison.Ordinal)) item.gameObject.SetActive(false);
        }

        static Vector3 Noise3(float time,float frequency) => new(
            Mathf.PerlinNoise(time*frequency,1.11f)*2-1,
            Mathf.PerlinNoise(time*frequency,3.31f)*2-1,
            Mathf.PerlinNoise(time*frequency,8.76f)*2-1);
        static float Smooth(float value) => value*value*(3-2*value);

        void OnGUI()
        {
            if(!Active) return;
            MakeStyles();
            Matrix4x4 previous=GUI.matrix;
            Color color=GUI.color;
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1920f,Screen.height/1080f,1));
            float impact=seconds-ImpactAt;
            if(keyArt && seconds<KeyArtEnd)
            {
                float travel=Mathf.Clamp01(seconds/KeyArtEnd);
                float push=Mathf.Lerp(0,38,travel);
                float dissolve=1-Smooth(Mathf.InverseLerp(2.83f,KeyArtEnd,seconds));
                GUI.color=new Color(1,1,1,dissolve);
                GUI.DrawTexture(new Rect(-push*.42f,-push*.4f,1920+push,1080+push*.56f),keyArt,ScaleMode.ScaleAndCrop,true);
                GUI.color=color;
            }
            if(impact>=0 && impact<.19f) Fill(new Rect(0,0,1920,1080),new Color(1,.58f,.32f,.42f*Mathf.Exp(-impact*24)));
            Fill(new Rect(0,0,1920,108),Color.black);
            Fill(new Rect(0,967,1920,113),Color.black);
            var accent=seconds<RamViewAt ? new Color(.54f,.8f,.92f) : new Color(1,.69f,.34f);
            Fill(new Rect(62,52,48,2),accent);
            GUI.Label(new Rect(124,35,700,40),seconds<RamViewAt ? "ДВОЙНИК   /   ОПЕРАЦИЯ «СИГНАЛ»" : "СИГНАЛ   /   БОРТОВОЙ РЕГИСТРАТОР",heading);
            GUI.Label(new Rect(1310,36,550,38),"ПРОЛОГ   /   2187",right);
            if(seconds>=.65f && seconds<HullDetailAt)
            {
                Fill(new Rect(66,784,515,104),new Color(.008f,.017f,.031f,.71f));
                Fill(new Rect(66,784,3,104),accent);
                GUI.Label(new Rect(88,790,480,60),"СИГНАЛ",title);
                GUI.Label(new Rect(90,850,460,31),"ОРБИТАЛЬНЫЙ ТРАНСПОРТ   /   РЕЙС 06",heading);
            }
            string line=seconds<HullDetailAt ? "НИЗКАЯ ОРБИТА ЗЕМЛИ" :
                seconds<RamViewAt ? "КУРС СТАБИЛЕН   /   ПЕРЕХОД К СТАНЦИИ" :
                seconds<ImpactAt ? "ШТУРМОВОЙ КАТЕР   /   ИДЁТ НА ТАРАН" :
                seconds<BoardingAt ? "ТАРАН ПО ПРАВОМУ БОРТУ   /   ВНЕШНЯЯ СВЯЗЬ ПОТЕРЯНА" :
                seconds<BridgeAt ? "ПРОБОИНА В ШЛЮЗЕ   /   ДЕСАНТ НА БОРТУ" : "АВАРИЙНОЕ ПИТАНИЕ   /   МОСТИК";
            Fill(new Rect(62,1007,20,2),accent);
            GUI.Label(new Rect(96,985,1200,55),line,caption);
            GUI.Label(new Rect(1390,985,470,55),"ESC ИЛИ ПРОБЕЛ   /   ПРОПУСТИТЬ",right);
            float fade=Mathf.Max(1-Mathf.Clamp01(seconds/.92f),Smooth(Mathf.Clamp01((seconds-BridgeAt-1.42f)/1.18f)));
            if(skipTime>=0) fade=Mathf.Max(fade,Mathf.Clamp01(skipTime/.28f));
            if(fade>0) Fill(new Rect(0,0,1920,1080),new Color(0,0,0,fade));
            GUI.matrix=previous;
            GUI.color=color;
        }

        void MakeStyles()
        {
            if(title!=null) return;
            var font=Resources.Load<Font>("Doppelganger/Interface/GolosText-Bold");
            if(!font) font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            title=Style(font,56,TextAnchor.MiddleLeft,new Color(.9f,.95f,1),FontStyle.Bold);
            heading=Style(font,22,TextAnchor.MiddleLeft,new Color(.69f,.82f,.93f));
            caption=Style(font,25,TextAnchor.MiddleLeft,new Color(.95f,.98f,1));
            right=Style(font,21,TextAnchor.MiddleRight,new Color(.65f,.77f,.88f));
        }

        static GUIStyle Style(Font font,int size,TextAnchor alignment,Color ink,FontStyle weight=FontStyle.Normal)
        {
            var result=new GUIStyle(GUI.skin.label){font=font,fontSize=size,fontStyle=weight,alignment=alignment};
            result.normal.textColor=ink;
            return result;
        }

        static void Fill(Rect rect,Color color)
        {
            Color old=GUI.color;
            GUI.color=color;
            GUI.DrawTexture(rect,Texture2D.whiteTexture);
            GUI.color=old;
        }

        void Finish()
        {
            if(!Active) return;
            var callback=onComplete;
            onComplete=null;
            Restore();
            Destroy(gameObject);
            callback?.Invoke();
        }

        void Restore()
        {
            RestoreOrbitLighting();
            if(!Active) return;
            Active=false;
            soundtrack.Stop();
            lens.enabled=false;
            listener.enabled=false;
            if(emergency) emergency.enabled=false;
            if(playerCamera) playerCamera.enabled=playerCameraWasEnabled;
            if(playerListener) playerListener.enabled=playerListenerWasEnabled;
        }

        void OnDisable() => Restore();
    }
}
