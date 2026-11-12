using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroSector.Campaign
{
    // Story speech owns the subtitle timeline. Short voice acknowledgments use
    // their own channel, so a pause command can speak while the story is paused.
    [DisallowMultipleComponent]
    public sealed class CampaignRadio : MonoBehaviour
    {
        enum Voice { Hera, Vetrov, Adam, Earth }
        public enum LinkStatus { NoPower, RelaysOffline, CoreLocked, AntennaUnaligned, Ready, Connected }

        readonly struct Transmission
        {
            public readonly Voice voice;
            public readonly string text;
            public readonly string clipName;
            public Transmission(Voice voice,string text,string clipName=null)
            { this.voice=voice; this.text=text; this.clipName=clipName; }
        }

        const string ClipFolder="Doppelganger/Radio/";
        static readonly string[] Names={"ГЕРА","КОМАНДИР АЛЕКС","ADAM","ЗЕМЛЯ"};
        const string EarthReplyText="Сигнал, Земля на связи. Вижу вашу траекторию. Курс держите, маяк оставьте включённым. Мы будем у вас через двенадцать минут.";
        static readonly Dictionary<LinkStatus,Transmission> LinkChecks=new()
        {
            [LinkStatus.NoPower]=new(Voice.Hera,"Внутренняя связь работает. Земля не отвечает: внешний передатчик обесточен. Командир, вы уже запрашивали связь с инженерной палубы.","HeraLinkNoPower"),
            [LinkStatus.RelaysOffline]=new(Voice.Hera,"Питание мостика восстановлено. Земля не отвечает: отключены реле передатчика. Командир, вы уже запрашивали связь с инженерной палубы.","HeraLinkRelays"),
            [LinkStatus.CoreLocked]=new(Voice.Hera,"Реле передатчика работают. Связь с Землёй заблокирована из ядра.","HeraLinkCoreLocked"),
            [LinkStatus.AntennaUnaligned]=new(Voice.Hera,"Передатчик включён. Наведите внешнюю антенну на Землю.","HeraLinkAntenna"),
            [LinkStatus.Ready]=new(Voice.Hera,"Антенна наведена. Внешний канал готов к передаче сигнала бедствия.","HeraLinkReady"),
            [LinkStatus.Connected]=new(Voice.Hera,"Земля на связи. Внешний канал открыт.","HeraLinkConnected")
        };
        static readonly Dictionary<string,Transmission> CommandReplies=new(StringComparer.Ordinal)
        {
            ["forward"]=new(Voice.Hera,"Движение по курсу.","HeraCommandForward"),
            ["backward"]=new(Voice.Hera,"Движение обратным ходом.","HeraCommandBackward"),
            ["left"]=new(Voice.Hera,"Смещение влево.","HeraCommandLeft"),
            ["right"]=new(Voice.Hera,"Смещение вправо.","HeraCommandRight"),
            ["stop"]=new(Voice.Hera,"Движение остановлено.","HeraCommandStop"),
            ["run"]=new(Voice.Hera,"Быстрый шаг, командир.","HeraCommandRun"),
            ["walk"]=new(Voice.Hera,"Переходим на шаг.","HeraCommandWalk"),
            ["jump"]=new(Voice.Hera,"Запрос на прыжок принят.","HeraCommandJump"),
            ["crouch"]=new(Voice.Hera,"Понижаю стойку.","HeraCommandCrouch"),
            ["stand"]=new(Voice.Hera,"Возвращаю высокую стойку.","HeraCommandStand"),
            ["reload"]=new(Voice.Hera,"Запрос на перезарядку принят.","HeraCommandReload"),
            ["inspect"]=new(Voice.Hera,"Запрос на осмотр принят.","HeraCommandInspect"),
            ["shield"]=new(Voice.Hera,"Энергия на защиту.","HeraCommandShield"),
            ["weapon"]=new(Voice.Hera,"Энергия на оружие.","HeraCommandWeapon"),
            ["pistol"]=new(Voice.Hera,"Выбран служебный пистолет.","HeraCommandPistol"),
            ["rifle"]=new(Voice.Hera,"Выбран автомат, командир.","HeraCommandRifle"),
            ["switch"]=new(Voice.Hera,"Запрос на смену оружия принят.","HeraCommandSwitch"),
            ["interact"]=new(Voice.Hera,"Запрос на взаимодействие принят.","HeraCommandInteract"),
            ["aim"]=new(Voice.Hera,"Режим прицеливания включён.","HeraCommandAim"),
            ["unaim"]=new(Voice.Hera,"Режим прицеливания снят.","HeraCommandUnaim"),
            ["fire"]=new(Voice.Hera,"Команду на огонь приняла.","HeraCommandFire"),
            ["pause"]=new(Voice.Hera,"Жду ваших указаний.","HeraCommandPause"),
            ["resume"]=new(Voice.Hera,"Возвращаемся к операции.","HeraCommandResume")
        };
        static readonly Dictionary<string,Transmission[]> Scripts=new(StringComparer.Ordinal)
        {
            ["bridge_intro"]=new Transmission[]
            {
                new(Voice.Hera,"Командир Алекс, это Гера, бортовой компьютер. Я на внутренней связи. «Сигнал» теряет высоту, Земля не отвечает.","HeraBridgeIntro"),
                new(Voice.Vetrov,"Слышу, ГЕРА. На мостике темно. Где аварийное питание?","VetrovBridgeIntro")
            },
            ["bridge_log"]=new Transmission[]
            {
                new(Voice.Hera,"Последняя запись: ADAM получил доступ к тренировочным данным экипажа. Потом мостик заперся.","HeraBridgeLog"),
                new(Voice.Vetrov,"Значит, он знает наши инструкции. Посмотрим, чему ещё он успел научиться.","VetrovBridgeLog")
            },
            ["fuse_pickup"]=new Transmission[]
            {
                new(Voice.Hera,"Ячейка цела. Отнесите её к аварийному щитку.","HeraBridgeBattery")
            },
            ["bridge_power"]=new Transmission[]
            {
                new(Voice.Hera,"Питание есть. Двигатели отвечают. Сначала поднимем орбиту: к навигационному пульту.","HeraBridgePower")
            },
            ["flight_intro"]=new Transmission[]
            {
                new(Voice.Hera,"Совместите метку курса с целью и дайте импульс коррекции. Я слежу за приборами.","HeraBridgeFlight"),
                new(Voice.Vetrov,"Принято. Выровняю «Сигнал».","VetrovFlightIntro")
            },
            ["bridge_route"]=new Transmission[]
            {
                new(Voice.Hera,"Траектория стабильна. Связь всё ещё заблокирована. В инженерном отсеке два реле передатчика.","HeraBridgeRoute"),
                new(Voice.Vetrov,"Включу реле. Покажи путь к шлюзу.","VetrovBridgeRoute")
            },
            ["engineering_intro"]=new Transmission[]
            {
                new(Voice.Hera,"Охрану кто-то перевёл в боевой режим. Если они откроют огонь, укройтесь или разверните щит.","HeraEngineeringIntro"),
                new(Voice.Adam,"Командир, я вижу, как вы двигаетесь. Продолжайте.","AdamEngineeringIntro"),
                new(Voice.Vetrov,"Гера, откуда у него доступ к моему костюму?","VetrovEngineeringIntro")
            },
            ["relay_first"]=new Transmission[]
            {
                new(Voice.Hera,"Одно реле в сети. По вашему допуску активирован резервный костюм. Он принимает телеметрию с вашего.","HeraRelayFirst")
            },
            ["relay_second"]=new Transmission[]
            {
                new(Voice.Hera,"Оба реле поднялись. Осталось добраться до ядра: там блокируется передача.","HeraRelaySecond")
            },
            ["engineering_clear"]=new Transmission[]
            {
                new(Voice.Hera,"Охрана больше не отвечает. На корпусах остались припасы. Можем идти дальше, как только оба реле будут в сети.","HeraEngineeringClear")
            },
            ["core_intro"]=new Transmission[]
            {
                new(Voice.Adam,"Узнаёте лицо, командир? Я взял вашу личность и доступ к боевой телеметрии.","AdamCoreIntro"),
                new(Voice.Vetrov,"Значит, это твой допуск остался в журнале. Отойди от ядра, я открою канал спасателей.","VetrovCoreIntro"),
                new(Voice.Hera,"Отключите оба контура охлаждения. Поле спадёт, и мы доберёмся до терминала ядра.","HeraCoreIntro")
            },
            ["coolant_first"]=new Transmission[]
            {
                new(Voice.Hera,"Первый контур отключён. Второй на противоположной стороне зала.","HeraCoolantFirst")
            },
            ["coolant_second"]=new Transmission[]
            {
                new(Voice.Hera,"Поле снято. Ищите укрытие, когда он наводится. Если включится отражение, меняйте позицию после выстрела.","HeraCoolantSecond"),
                new(Voice.Adam,"Вы повторяете свои движения. Я уже знаю, куда вы пойдёте.","AdamCoolantSecond")
            },
            ["adam_down"]=new Transmission[]
            {
                new(Voice.Adam,"Сигнал... прерван.","AdamDown"),
                new(Voice.Vetrov,"Да. ГЕРА, готовь аварийное сообщение. Я сниму блокировку с терминала.","VetrovAdamDown")
            },
            ["eva_unlocked"]=new Transmission[]
            {
                new(Voice.Hera,"Внешний шлюз открыт. Антенна на корпусе. Я останусь с вами по внутренней связи.","HeraEvaUnlocked"),
                new(Voice.Vetrov,"Выхожу к антенне. Сообщи, когда поймаешь Землю.","VetrovEvaUnlocked")
            },
            ["eva_outside"]=new Transmission[]
            {
                new(Voice.Hera,"Держитесь корпуса, командир. Доберитесь до привода и наведите тарелку на Землю.","HeraEvaOutside")
            },
            ["antenna_aligned"]=new Transmission[]
            {
                new(Voice.Hera,"Антенна наведена на Землю. Можно передавать сигнал бедствия.","HeraAntennaAligned"),
                new(Voice.Vetrov,"Тогда открывай аварийный канал.","VetrovAntennaAligned")
            },
            ["ending"]=new Transmission[]
            {
                new(Voice.Vetrov,"Земля, это «Сигнал». Орбита стабилизирована, но корабль повреждён. Просим эвакуацию экипажа. Приём.","VetrovEnding"),
                new(Voice.Earth,EarthReplyText,"EarthReply"),
                new(Voice.Hera,"Земля нас слышит. Держим курс, командир.","HeraEnding")
            }
        };

        readonly LinkedList<Transmission> queue=new();
        readonly Dictionary<string,AudioClip> speechClips=new(StringComparer.Ordinal);
        readonly AudioClip[] cues=new AudioClip[Names.Length];
        const float SpeakerTail=.38f;
        AudioSource source,commandSource;
        Transmission current;
        float remaining,duration,voiceSafeAfter;
        bool audioPaused,diagnosing,storySpeech;
        public string Speaker { get; private set; }="";
        public string Line { get; private set; }="";
        public bool IsPlaying => remaining>0 || queue.Count>0;
        public float Progress => duration>0 ? Mathf.Clamp01(1-remaining/duration) : 0;
        bool CommandSpeaking => commandSource && commandSource.isPlaying;
        public bool SuppressesVoiceInput
        {
            get
            {
                // Hold briefly after playback so speakers and microphone buffers do
                // not leave the end of a spoken response in the recognizer.
                if(CommandSpeaking || (storySpeech && source && source.isPlaying))
                    voiceSafeAfter=Time.unscaledTime+SpeakerTail;
                return Time.unscaledTime<voiceSafeAfter;
            }
        }

        public static IEnumerable<string> RequiredClipNames()
        {
            var clips=new HashSet<string>(StringComparer.Ordinal);
            void Require(Transmission line)
            {
                if(string.IsNullOrWhiteSpace(line.clipName))
                    throw new InvalidOperationException("Radio line without a recorded clip: "+line.text);
                clips.Add(line.clipName);
            }
            foreach(var script in Scripts.Values) foreach(var line in script) Require(line);
            foreach(var line in LinkChecks.Values) Require(line);
            foreach(var line in CommandReplies.Values) Require(line);
            foreach(var command in SpokenCommandCatalog.Entries)
                if(command.Action!="check_link" && !CommandReplies.ContainsKey(command.Action))
                    throw new InvalidOperationException("Missing Hera acknowledgment: "+command.Action);
            if(!CommandReplies.ContainsKey("unaim"))
                throw new InvalidOperationException("Missing Hera acknowledgment: unaim");
            return clips;
        }

        void Awake() { Initialize(); }

        public void Initialize()
        {
            if(source) return;
            source=gameObject.AddComponent<AudioSource>();
            source.playOnAwake=false; source.spatialBlend=0; source.loop=false;
            source.volume=.32f; source.priority=32;
            commandSource=gameObject.AddComponent<AudioSource>();
            commandSource.playOnAwake=false; commandSource.spatialBlend=0; commandSource.loop=false;
            commandSource.volume=.72f; commandSource.priority=33;
            cues[(int)Voice.Hera]=BuildCue("ГЕРА / вызов",740,930);
            cues[(int)Voice.Vetrov]=BuildCue("Алекс / ответ",520,625);
            cues[(int)Voice.Adam]=BuildCue("ADAM / помехи",305,250);
            cues[(int)Voice.Earth]=BuildCue("Земля / приём",815,560);
        }

        public void Play(string key,bool priority=false)
        {
            if(string.IsNullOrEmpty(key) || !Scripts.TryGetValue(key,out var script)) return;
            if(priority)
            {
                queue.Clear();
                if(!diagnosing) Skip();
            }
            foreach(var transmission in script) queue.AddLast(transmission);
        }

        public bool CheckLink(LinkStatus status)
        {
            if(audioPaused || (diagnosing && remaining>0) || !LinkChecks.TryGetValue(status,out var response)) return false;
            if(commandSource) commandSource.Stop();
            if(remaining>0) queue.AddFirst(current);
            StartTransmission(response);
            diagnosing=true;
            return true;
        }

        public void AcknowledgeCommand(string action)
        {
            if(!CommandReplies.TryGetValue(action,out var response)) return;
            var speech=LoadSpeech(response.clipName);
            if(!speech) return;
            Initialize();
            PauseStoryAudio();
            commandSource.Stop();
            commandSource.clip=speech;
            commandSource.Play();
            voiceSafeAfter=Time.unscaledTime+SpeakerTail;
        }

        // Supply zero while paused. Text does not disappear behind menus, and a
        // half-played radio cue resumes from its original position.
        public void Tick(float dt)
        {
            if(dt<=0 || CommandSpeaking)
            {
                PauseStoryAudio();
                return;
            }
            if(audioPaused)
            {
                if(source) source.UnPause();
                audioPaused=false;
            }
            remaining=Mathf.Max(0,remaining-dt);
            if(remaining>0) return;
            diagnosing=false;
            if(queue.Count==0) { Speaker=Line=""; duration=0; return; }
            var transmission=queue.First.Value;
            queue.RemoveFirst();
            StartTransmission(transmission);
        }

        void PauseStoryAudio()
        {
            if(audioPaused || !source || !source.isPlaying) return;
            source.Pause();
            audioPaused=true;
        }

        void StartTransmission(Transmission transmission)
        {
            current=transmission;
            int voice=(int)transmission.voice;
            Speaker=Names[voice];
            Line=transmission.text;
            remaining=duration=Mathf.Clamp(1.45f+Line.Length*.042f,2.6f,6.5f);
            Initialize();
            AudioClip speech=LoadSpeech(transmission.clipName);
            if(speech) remaining=duration=Mathf.Max(duration,speech.length+.2f);
            storySpeech=speech;
            if(storySpeech) voiceSafeAfter=Time.unscaledTime+SpeakerTail;
            source.volume=speech ? .8f : .32f;
            source.Stop(); source.clip=speech ? speech : cues[voice]; source.Play();
        }

        AudioClip LoadSpeech(string clipName)
        {
            if(clipName==null) return null;
            if(speechClips.TryGetValue(clipName,out var clip)) return clip;
            clip=Resources.Load<AudioClip>(ClipFolder+clipName);
            if(clip) speechClips.Add(clipName,clip);
            return clip;
        }

        public void Skip()
        {
            if(source) source.Stop();
            audioPaused=diagnosing=storySpeech=false;
            remaining=duration=0;
            Speaker=Line="";
        }

        public void Clear()
        {
            queue.Clear();
            if(commandSource) commandSource.Stop();
            Skip();
        }

        static AudioClip BuildCue(string name,float first,float second)
        {
            const int rate=22050;
            var samples=new float[4410];
            for(int i=0;i<samples.Length;i++)
            {
                float time=i/(float)rate;
                bool tail=time>=.093f;
                float t=tail ? time-.093f : time;
                float length=tail ? .078f : .067f;
                if(t>=length) continue;
                float f=tail ? second : first;
                float edge=Mathf.Clamp01(t/.008f)*Mathf.Clamp01((length-t)/.018f);
                float phase=t*f*2*Mathf.PI;
                samples[i]=edge*(.34f*Mathf.Sin(phase)+.07f*Mathf.Sin(phase*2.03f))*(tail ? .7f : 1);
            }
            var clip=AudioClip.Create(name,samples.Length,1,rate,false);
            clip.SetData(samples,0);
            return clip;
        }

        void OnDestroy()
        {
            if(source) source.Stop();
            if(commandSource) commandSource.Stop();
            foreach(var cue in cues) if(cue)
            {
                if(Application.isPlaying) Destroy(cue);
                else DestroyImmediate(cue);
            }
        }
    }
}
