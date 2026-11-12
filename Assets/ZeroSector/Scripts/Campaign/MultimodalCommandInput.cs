using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroSector.Campaign
{
    // Update the sampled pose before movement and aim consume it in CampaignGame.
    [DefaultExecutionOrder(-120)]
    public sealed class MultimodalCommandInput : MonoBehaviour
    {
        const string CameraPreference="Doppelganger.PhysicalCamera";
        const string SensitivityPreference="Doppelganger.HeadSensitivity";
        const string HeadCenterInstruction="Посмотри в центр экрана и замри на полсекунды.";
        public bool VoiceEnabled { get; private set; }
        public bool CameraEnabled => cameraRequested;
        public string VoiceStatus { get; private set; }="Голос выключен. F7 — команды; полный список в справке.";
        public float VoiceLevel => russianVoice ? russianVoice.MicrophoneLevel : 0;
        public string CameraStatus { get; private set; }="Камера выключена. F8 — включить.";
        public string State => VoiceStatus+" | "+CameraStatus;
        public Texture CameraPreview => cameraTexture;
        public float CameraProgress => gestureGate.Progress;
        public bool CameraCalibrating => cameraRequested && handRecognizer!=null && !handRecognizer.IsReady;
        public int CameraRotation => cameraTexture ? cameraTexture.videoRotationAngle : 0;
        public bool CameraVerticallyMirrored => cameraTexture && cameraTexture.videoVerticallyMirrored;
        public bool BridgeCamera => game && game.chapter==0;
        public Vector2 HeadViewOffset => cameraRequested && game && game.Running ? headView.Offset : Vector2.zero;
        public float HeadSensitivity
        {
            get => headView.Sensitivity;
            set
            {
                float scale=float.IsNaN(value) ? 1f : Mathf.Round(Mathf.Clamp(value,.7f,1.4f)*100)*.01f;
                if(Mathf.Approximately(scale,headView.Sensitivity)) return;
                headView.Sensitivity=scale;
                PlayerPrefs.SetFloat(SensitivityPreference,scale);
            }
        }
        public string HeadTrackingStatus { get; private set; }="";
        public string CameraGesture { get; private set; }="Покажи ладонь в камеру";
        public HandGestureHand[] CameraHands { get; private set; }=Array.Empty<HandGestureHand>();
        public string LastCommand { get; private set; }="Пока нет команд";
        public int VoiceCommandCount { get; private set; }
        public int CameraCommandCount { get; private set; }
        public string VoiceDeviceName
        {
            get
            {
                if(russianVoice && russianVoice.Requested) return russianVoice.DeviceName;
                string[] devices=Microphone.devices;
                if(devices.Length==0) return "Микрофон не найден";
                if(Array.IndexOf(devices,selectedMicrophone)<0) selectedMicrophone=devices[0];
                return selectedMicrophone;
            }
        }
        public string CameraDeviceName
        {
            get
            {
                if(cameraTexture) return cameraTexture.deviceName;
                return ChooseCamera(DeviceNames(),selectedCamera) ?? "Физическая камера не найдена";
            }
        }

        CampaignGame game;
        WebCamTexture cameraTexture;
        Color32[] pixels;
        HandGestureRecognizer handRecognizer;
        readonly HeadViewTracking headView=new();
        readonly GestureCommandGate gestureGate=new();
        bool cameraRequested;
        int cameraGeneration;
        float lastCameraFrame,nextFrame,lastVoiceCommand,lastGestureResult,headAcceptAfter;
        string selectedMicrophone,selectedCamera,motionOwner;
        readonly Queue<string> spoken=new();
        RussianVoiceRecognizer russianVoice;
        static bool MovementAction(string action) => action=="forward" || action=="backward" || action=="left" || action=="right" || action=="run" || action=="walk";
        void Awake()
        {
            game=GetComponent<CampaignGame>();
            selectedCamera=PlayerPrefs.GetString(CameraPreference,"");
            float sensitivity=PlayerPrefs.GetFloat(SensitivityPreference,1f);
            headView.Sensitivity=float.IsNaN(sensitivity) ? 1f : Mathf.Clamp(sensitivity,.7f,1.4f);
        }

        public void NextMicrophone()
        {
            if(VoiceEnabled) return;
            string[] devices=Microphone.devices; if(devices.Length==0) return;
            selectedMicrophone=devices[(Array.IndexOf(devices,VoiceDeviceName)+1)%devices.Length];
            VoiceStatus="Выбран микрофон: "+selectedMicrophone+". F7 — включить.";
        }
        public void NextCamera()
        {
            if(cameraRequested) return;
            string[] devices=Array.FindAll(DeviceNames(),IsUsableCamera);
            if(devices.Length==0) return;
            int current=Array.IndexOf(devices,ChooseCamera(devices,selectedCamera));
            selectedCamera=devices[(current+1)%devices.Length];
            PlayerPrefs.SetString(CameraPreference,selectedCamera);
            CameraStatus="Выбрана камера: "+selectedCamera+". F8 — включить.";
        }

        static string[] DeviceNames() => Array.ConvertAll(WebCamTexture.devices,device=>device.name);

        static bool HasName(string name,string part) => name.IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0;

        static bool IsUsableCamera(string name) => !string.IsNullOrWhiteSpace(name) &&
            !HasName(name,"virtual") && !HasName(name,"виртуальн") && !HasName(name,"obs camera") &&
            !HasName(name,"nvidia broadcast") && !HasName(name,"snap camera") && !HasName(name,"manycam") &&
            !HasName(name,"infrared") && !HasName(name,"IR camera") && !HasName(name,"IR webcam");

        public static string ChooseCamera(string[] devices,string preferred)
        {
            string builtIn=null, first=null;
            foreach(string name in devices)
            {
                if(!IsUsableCamera(name)) continue;
                if(string.Equals(name,preferred,StringComparison.OrdinalIgnoreCase)) return name;
                if(first==null) first=name;
                if(HasName(name,"BisonCam")) builtIn=name;
                else if(builtIn==null && (HasName(name,"integrated") || HasName(name,"built-in") || HasName(name,"встроенн")))
                    builtIn=name;
            }
            return builtIn ?? first;
        }

        public void ToggleVoice()
        {
            if(VoiceEnabled) { StopVoice(); VoiceStatus="Голос выключен."; return; }
            if(!russianVoice) { russianVoice=gameObject.AddComponent<RussianVoiceRecognizer>(); russianVoice.Phrase+=OnPhrase; }
            russianVoice.SuppressInput=RadioSuppressesVoice;
            lock(spoken) spoken.Clear(); lastVoiceCommand=float.NegativeInfinity;
            russianVoice.StartListening(VoiceDeviceName);
            VoiceEnabled=russianVoice.Requested; VoiceStatus=russianVoice.Status; game?.Notify(VoiceStatus);
        }
        void OnPhrase(string phrase) { lock(spoken) { if(spoken.Count<8) spoken.Enqueue(phrase); } }
        void StopVoice()
        {
            VoiceEnabled=false; if(russianVoice) russianVoice.StopListening(); lock(spoken) spoken.Clear();
            StopOwnedMotion("voice");
        }
        void StopOwnedMotion(string owner)
        {
            if(motionOwner!=owner) return;
            if(game && game.Commands!=null) game.Commands.StopMotion(); motionOwner=null;
        }

        public void ToggleCamera()
        {
            if(cameraRequested) { StopCamera(); CameraStatus="Камера выключена."; return; }
            cameraRequested=true; CameraStatus="Запрашиваю доступ к камере…";
            StartCoroutine(StartCamera(cameraGeneration));
        }
        IEnumerator StartCamera(int generation)
        {
            if(!Application.HasUserAuthorization(UserAuthorization.WebCam)) yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if(!cameraRequested || generation!=cameraGeneration) yield break;
            if(!Application.HasUserAuthorization(UserAuthorization.WebCam)) { FailCamera("Доступ к камере запрещён в Windows."); yield break; }
            try
            {
                string device=ChooseCamera(DeviceNames(),selectedCamera);
                if(device==null) { FailCamera("Физическая веб-камера не найдена. Проверь её в Windows."); yield break; }
                cameraTexture=new WebCamTexture(device,640,360,30);
                cameraTexture.Play();
                handRecognizer=new HandGestureRecognizer(); handRecognizer.Start(true);
                RecalibrateHead();
                lastCameraFrame=lastGestureResult=Time.unscaledTime; nextFrame=0; gestureGate.Reset();
                CameraStatus="Загружаю распознавание лица и кистей…";
            }
            catch(Exception exception) { FailCamera("Не удалось запустить жесты: "+ShortError(exception)); }
        }
        public void RecalibrateCamera()
        {
            gestureGate.Reset(); CameraHands=Array.Empty<HandGestureHand>();
            StopOwnedMotion("camera"); CameraStatus="Покажи жест целиком и удерживай примерно 0,6 секунды.";
            RecalibrateHead();
        }

        public void RecalibrateHead()
        {
            if(!cameraRequested) return;
            headView.Recenter();
            headAcceptAfter=Time.unscaledTime;
            HeadTrackingStatus=HeadCenterInstruction;
        }

        void Update()
        {
            string phrase=null;
            if(russianVoice)
            {
                bool failed=VoiceEnabled && !russianVoice.Requested;
                VoiceEnabled=russianVoice.Requested; VoiceStatus=russianVoice.Status;
                if(failed) { StopOwnedMotion("voice"); game?.Notify(VoiceStatus); }
            }
            bool radioSpeaking=RadioSuppressesVoice();
            lock(spoken)
            {
                if(radioSpeaking) spoken.Clear();
                else if(spoken.Count>0) phrase=spoken.Dequeue();
            }
            if(VoiceEnabled && phrase!=null && Time.unscaledTime-lastVoiceCommand>.35f)
            { lastVoiceCommand=Time.unscaledTime; ApplyPhrase(phrase); }
            if(!cameraRequested || !cameraTexture) return;
            float now=Time.unscaledTime;
            bool headActive=game && game.Running;
            var keys=Keyboard.current;
            if(keys!=null && keys.f9Key.wasPressedThisFrame) RecalibrateHead();
            bool physicalMovement=keys!=null && (keys.wKey.isPressed || keys.aKey.isPressed || keys.sKey.isPressed || keys.dKey.isPressed);
            if(physicalMovement) StopOwnedMotion("camera");
            if(now-lastCameraFrame>8) { FailCamera(cameraTexture.deviceName+" не отдаёт кадры. Проверь, не занята ли камера другим приложением."); return; }
            if(handRecognizer!=null)
            {
                while(handRecognizer.TryGetResult(out var result,BridgeCamera))
                {
                    if(result.FrameAgeSeconds>.25f) continue;
                    float capturedAt=now-result.FrameAgeSeconds;
                    if(headActive && capturedAt>=headAcceptAfter && result.Head!=null && result.Head.Tracked)
                        headView.Observe(new Vector2(result.Head.Yaw,result.Head.Pitch),capturedAt);
                    if(result.HandSample) ProcessGesture(result,now,physicalMovement);
                }
                if(!handRecognizer.IsReady) CameraStatus=handRecognizer.Status;
            }
            UpdateHead(headActive,now);
            if(now-lastGestureResult>.65f) { StopOwnedMotion("camera"); CameraHands=Array.Empty<HandGestureHand>(); gestureGate.Update(null,0,now); }
            if(physicalMovement) gestureGate.SuppressCurrent(MovementAction);
            if(now<nextFrame || !cameraTexture.didUpdateThisFrame || cameraTexture.width<32 || cameraTexture.height<24) return;
            nextFrame=now+(headActive ? .027f : .07f); lastCameraFrame=now;
            if(handRecognizer==null || !handRecognizer.IsReady) return;
            try
            {
                pixels=cameraTexture.GetPixels32(pixels!=null && pixels.Length==cameraTexture.width*cameraTexture.height ? pixels : null);
                handRecognizer.Submit(pixels,cameraTexture.width,cameraTexture.height,CameraRotation,CameraVerticallyMirrored);
            }
            catch(Exception exception) { FailCamera("Ошибка распознавания: "+ShortError(exception)); }
        }
        bool RadioSuppressesVoice() => game && game.Radio && game.Radio.SuppressesVoiceInput;

        void UpdateHead(bool active,float now)
        {
            if(!active)
            {
                headView.Suspend();
                headAcceptAfter=now;
                HeadTrackingStatus="Управление взглядом включается во время игры.";
                return;
            }
            headView.Tick(now,Time.unscaledDeltaTime);
            if(handRecognizer==null || !handRecognizer.IsReady) HeadTrackingStatus="Готовлю модель лица…";
            else if(!headView.IsCalibrated) HeadTrackingStatus=HeadCenterInstruction;
            else if(!headView.IsTracking(now)) HeadTrackingStatus="Лицо вне кадра. Повернись к экрану.";
            else HeadTrackingStatus=$"Взгляд {headView.Offset.x:+0;-0;0}° / {headView.Offset.y:+0;-0;0}°   ·   F9 — центр";
        }
        void ApplyPhrase(string phrase)
        {
            if(!SpokenCommandCatalog.TryResolve(phrase,out string action)) return;
            if(!ExecuteAction(action,"voice",out string executed)) { VoiceStatus="Сейчас недоступно: "+SpokenCommandCatalog.GetActionTitle(action); return; }
            VoiceCommandCount++; VoiceStatus="Услышано: «"+phrase+"».";
            LastCommand="Голос → "+game.Commands.CurrentAction; game?.Notify(LastCommand);
            game.Radio?.AcknowledgeCommand(executed);
        }
        bool ExecuteAction(string action,string source,out string executed)
        {
            executed=action;
            if(!game || game.Commands==null) return false;
            if(source=="camera" && action=="pause" && game.Paused) action="resume";
            if(action=="aim" && game.Commands.WantsAim) action="unaim";
            if(!game.Commands.Execute(action)) return false;
            executed=action;
            if(MovementAction(action)) motionOwner=source;
            else if(action=="stop" || action=="pause" || action=="resume") { game.Commands.StopMotion(); motionOwner=null; }
            else if(source=="voice" && motionOwner=="camera") motionOwner=null;
            return true;
        }
        void ProcessGesture(HandGestureResult result,float now,bool physicalMovement)
        {
            lastGestureResult=now;
            CameraHands=result.Hands ?? Array.Empty<HandGestureHand>();
            CameraGesture=string.IsNullOrEmpty(result.Pose) ? "Рука не найдена" : result.Pose;
            bool accepted=gestureGate.Update(result.Action,result.Confidence,now);
            if(string.IsNullOrEmpty(result.Action) || result.Confidence<.65f)
            { StopOwnedMotion("camera"); CameraStatus="Покажи руку целиком. Список жестов — Esc → Управление."; return; }
            if(motionOwner=="camera" && (!MovementAction(result.Action) || result.Action!=gestureGate.AcceptedAction)) StopOwnedMotion("camera");
            if(accepted && !(physicalMovement && MovementAction(result.Action)))
            {
                if(ExecuteAction(result.Action,"camera",out _))
                {
                    CameraCommandCount++; LastCommand="Жест → "+game.Commands.CurrentAction; game?.Notify(LastCommand);
                }
            }
            if(motionOwner=="camera" && result.Action==gestureGate.AcceptedAction && MovementAction(result.Action) && game && game.Commands!=null)
                game.Commands.RenewMovement(result.Action,.55f);
            CameraStatus=gestureGate.Progress>=1 ? "Жест определён. Для повтора убери руку и покажи жест снова." : "Удерживай жест, пока заполнится индикатор.";
        }
        static string ShortError(Exception exception) { string message=exception.Message.Replace('\n',' '); return message.Length>180 ? message.Substring(0,180) : message; }
        void FailCamera(string reason) { StopCamera(); CameraStatus=reason; game?.Notify(reason); }
        void StopCamera()
        {
            cameraRequested=false; cameraGeneration++; gestureGate.Reset(); StopOwnedMotion("camera");
            headView.Recenter(); HeadTrackingStatus="";
            handRecognizer?.Dispose(); handRecognizer=null; CameraHands=Array.Empty<HandGestureHand>();
            if(cameraTexture) { if(cameraTexture.isPlaying) cameraTexture.Stop(); Destroy(cameraTexture); cameraTexture=null; }
            pixels=null;
        }
        void OnDisable() { StopAllCoroutines(); StopVoice(); StopCamera(); }
        void OnDestroy() { StopVoice(); StopCamera(); }
    }

    // Stable pose, one event per hold; a neutral hand gap rearms the same gesture.
    public sealed class GestureCommandGate
    {
        public float Progress { get; private set; }
        public string AcceptedAction { get; private set; }
        string stable; float hold,neutral,lastTime=-1,lastAccept=-100;
        public void Reset() { Progress=hold=neutral=0; stable=AcceptedAction=null; lastTime=-1; lastAccept=-100; }
        public void SuppressCurrent(Func<string,bool> filter=null)
        { if(stable!=null && (filter==null || filter(stable))) AcceptedAction=stable; }
        public bool Update(string action,float confidence,float now)
        {
            float dt=lastTime<0 ? 0 : Mathf.Clamp(now-lastTime,0,.3f); lastTime=now;
            if(string.IsNullOrEmpty(action) || confidence<.65f)
            {
                neutral+=dt; stable=null; hold=Progress=0;
                if(neutral>=.35f) AcceptedAction=null;
                return false;
            }
            neutral=0;
            if(stable!=action) { stable=action; hold=0; }
            hold+=dt; float required=action=="stop" ? .2f : .6f; Progress=Mathf.Clamp01(hold/required);
            if(AcceptedAction==action || hold<required || (action!="stop" && now-lastAccept<.45f)) return false;
            AcceptedAction=action; lastAccept=now; return true;
        }
    }
}
