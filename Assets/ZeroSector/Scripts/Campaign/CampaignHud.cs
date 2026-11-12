using UnityEngine;

namespace ZeroSector.Campaign
{
    // Presentation adapter for CampaignGame. Devices start only from explicit buttons.
    public sealed partial class CampaignHud : MonoBehaviour
    {
        CampaignGame game;
        Font font, boldFont;
        GUIStyle body,small,title,heading,button,ammo,zoneLabel,menuKicker,menuPrimary,menuSecondary;
        Texture2D keyArt,menuShade;
        bool help,ownsFont;
        int controlPage;
        readonly Color cyan=new(.34f,.81f,.94f),ink=new(.92f,.95f,.96f),panel=new(.012f,.021f,.033f,.90f),amber=new(1,.67f,.34f);

        public void Initialize(CampaignGame owner)
        {
            game=owner;
            keyArt=Resources.Load<Texture2D>("Doppelganger/Interface/SignalKeyArt");
        }

        void Styles()
        {
            if(body!=null) return;
            font=Resources.Load<Font>("Doppelganger/Interface/GolosText-Regular");
            boldFont=Resources.Load<Font>("Doppelganger/Interface/GolosText-Bold");
            if(!font)
            {
                font=Font.CreateDynamicFontFromOSFont(new[]{"Segoe UI","Arial","DejaVu Sans"},24);
                ownsFont=true;
            }
            body=Style(21,ink); small=Style(17,new Color(.69f,.77f,.83f));
            title=Style(91,ink,FontStyle.Bold); title.wordWrap=false;
            var blackFont=Resources.Load<Font>("Doppelganger/Interface/GolosText-Black");
            if(blackFont) title.font=blackFont;
            heading=Style(26,ink,FontStyle.Bold);
            ammo=Style(44,ink,FontStyle.Bold);
            menuKicker=Style(16,amber,FontStyle.Bold); menuKicker.wordWrap=false;
            menuPrimary=Style(22,new Color(.046f,.027f,.016f),FontStyle.Bold);
            menuSecondary=Style(21,ink,FontStyle.Bold);
            zoneLabel=Style(14,ink,FontStyle.Bold); zoneLabel.alignment=TextAnchor.MiddleCenter; zoneLabel.wordWrap=false;
            button=new GUIStyle(GUI.skin.button) { font=boldFont ? boldFont : font,fontSize=20,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(20,15,8,8) };
        }
        GUIStyle Style(int size,Color color,FontStyle weight=FontStyle.Normal)
        {
            bool packagedBold=weight==FontStyle.Bold && boldFont;
            var style=new GUIStyle(GUI.skin.label) { font=packagedBold ? boldFont : font,fontSize=size,fontStyle=packagedBold ? FontStyle.Normal : weight,wordWrap=true,richText=false };
            style.normal.textColor=color; return style;
        }
        static void Fill(Rect rect,Color color)
        {
            Color previous=GUI.color; GUI.color=color; GUI.DrawTexture(rect,Texture2D.whiteTexture); GUI.color=previous;
        }
        void Box(Rect rect)
        {
            Fill(rect,panel);
            Fill(new Rect(rect.x,rect.y,rect.width,1),new Color(.35f,.49f,.6f,.32f));
            Fill(new Rect(rect.x,rect.y,42,2),amber);
        }
        void Text(float x,float y,float width,float height,string text,GUIStyle style=null)
        { GUI.Label(new Rect(x,y,width,height),text,style ?? body); }
        bool Button(float x,float y,float width,string text)
        { return GUI.Button(new Rect(x,y,width,51),text,button); }

        void OnGUI()
        {
            if(!game || game.PrologueActive) return;
            Styles(); Matrix4x4 previous=GUI.matrix; Color prior=GUI.color;
            float scale=Mathf.Min(Screen.width/1600f,Screen.height/900f);
            if(game.MainMenu && keyArt) GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),keyArt,ScaleMode.ScaleAndCrop,false);
            Vector3 offset=new((Screen.width-1600*scale)*.5f,(Screen.height-900*scale)*.5f,0);
            GUI.matrix=Matrix4x4.TRS(offset,Quaternion.identity,new Vector3(scale,scale,1));
            try
            {
                if(game.BriefingOpen)
                {
                    Briefing();
                    if(game.ScreenFade>0) Fill(new Rect(0,0,1600,900),new Color(0,0,0,game.ScreenFade));
                    return;
                }
                if(game.MainMenu) Menu();
                else if(game.TerminalOpen) Terminal();
                else
                {
                    Gameplay();
                    if(game.Dead || game.Victory) Result();
                    else if(game.Paused) Pause();
                }
                if(!game.MainMenu && !game.Paused && !game.Dead && !game.Victory) DrawRadio();
                if(!help || (!game.MainMenu && !game.Paused)) DeviceOverlay();
                if(!string.IsNullOrEmpty(game.Notice))
                {
                    Box(new Rect(410,701,780,58)); Text(428,710,742,45,game.Notice,small);
                }
                if(game.ScreenFade>0) Fill(new Rect(0,0,1600,900),new Color(0,0,0,game.ScreenFade));
            }
            finally { GUI.matrix=previous; GUI.color=prior; }
        }

        void Menu()
        {
            if(help) { Controls(default); return; }
            if(!menuShade)
            {
                menuShade=new Texture2D(128,1,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontSave};
                for(int i=0;i<128;i++) menuShade.SetPixel(i,0,new Color(.014f,.023f,.037f,.94f*(1-Mathf.SmoothStep(0,1,(i-28)/75f))));
                menuShade.Apply(false,true);
            }
            GUI.DrawTexture(new Rect(0,0,1600,900),menuShade);
            Fill(new Rect(76,81,32,2),amber);
            Text(120,66,530,32,"СИГНАЛ  /  ОРБИТАЛЬНАЯ ПРОГРАММА",menuKicker);
            Text(69,116,660,119,"ДВОЙНИК",title);
            Text(77,232,570,37,"ОПЕРАЦИЯ «СИГНАЛ»",heading);
            Text(78,288,488,66,"2187 год. Экипаж теряет связь в низкой орбите Земли.",body);
            if(MenuButton(381,"01","Начать операцию",true)) game.StartNewGame();
            bool enabled=GUI.enabled; GUI.enabled=CampaignSession.HasSave;
            if(MenuButton(449,"02","Загрузить контрольную точку",false)) game.ContinueGame();
            GUI.enabled=enabled;
            if(MenuButton(517,"03","Управление и устройства",false)) help=true;
            if(MenuButton(585,"04","Брифинг операции",false)) game.OpenBriefing();
            if(MenuButton(653,"05","Выйти",false)) game.QuitGame();
#if UNITY_EDITOR
            if(game.chapter>0 && Button(76,715,493,"Запустить эту сцену")) game.StartCurrentEpisode();
#endif
            Fill(new Rect(76,775,493,1),new Color(.63f,.75f,.80f,.25f));
            Text(77,798,502,37,"ОПЕРАЦИЯ 01   /   НИЗКАЯ ОРБИТА",small);
            Fill(new Rect(1247,768,280,1),new Color(.8f,.87f,.93f,.44f));
            Text(1248,781,295,31,"«СИГНАЛ»  /  КОРПУС 01",menuKicker);
            Text(1248,812,304,29,"БОРТОВОЙ АРХИВ",small);
        }
        bool MenuButton(float y,string index,string label,bool primary)
        {
            var rect=new Rect(76,y,493,55);
            bool hover=GUI.enabled && rect.Contains(Event.current.mousePosition);
            Fill(rect,primary ? (hover ? new Color(1,.78f,.49f) : amber) : hover ? new Color(.14f,.19f,.24f,.97f) : new Color(.028f,.047f,.066f,.90f));
            if(!primary) Fill(new Rect(rect.x,rect.y,rect.width,1),new Color(.60f,.72f,.81f,hover ? .6f : .21f));
            Text(94,y+16,42,28,index,primary ? menuPrimary : menuKicker);
            Text(149,y+13,372,36,label,primary ? menuPrimary : GUI.enabled ? menuSecondary : small);
            if(hover || primary) Text(527,y+13,24,36,"›",primary ? menuPrimary : menuSecondary);
            return GUI.Button(rect,GUIContent.none,GUIStyle.none);
        }

        void Gameplay()
        {
            Box(new Rect(30,25,690,130));
            Text(48,37,650,38,game.ChapterTitle,heading); Text(48,82,650,68,game.Objective);
            var view=game.presentation;
            Box(new Rect(30,738,330,130));
            Text(48,749,300,35,$"{game.Health:000} HP   /   {game.Shield:00} ЗАРЯД");
            Fill(new Rect(49,794,290,5),new Color(.2f,.28f,.35f)); Fill(new Rect(49,794,290*game.Health/100,5),ink);
            Fill(new Rect(49,807,290,5),new Color(.2f,.28f,.35f)); Fill(new Rect(49,807,290*game.Shield/60,5),cyan);
            Text(48,825,300,35,"КОСТЮМ: "+new[]{"ОРУЖИЕ","ЛИЧНЫЙ ЩИТ","СЕРВОПРИВОДЫ"}[game.Energy],small);
            if(game.ActiveShield)
            {
                var shield=game.ActiveShield;
                string state=game.Energy!=1 ? "УБРАН" : shield.IsDepleted ? "РАЗРЯЖЕН" : shield.Deployed ? "РАСКРЫТ" : "РАСКРЫВАЕТСЯ";
                Box(new Rect(30,678,330,52));
                Text(48,681,300,24,"ЭНЕРГОЩИТ: "+state,small);
                Text(48,706,300,22,$"Заряд {shield.Charge:00}/60 · Перехватов {shield.BlockedHits}",small);
            }
            if(game.Carrying)
            {
                Box(new Rect(1220,738,350,130)); Text(1238,755,306,38,"АККУМУЛЯТОР В РУКАХ",heading);
                Text(1238,806,310,54,"E — установить в щиток\nG — положить",small);
            }
            else if(game.Outside)
            {
                Box(new Rect(1220,738,350,130)); Text(1238,752,310,42,"СКАФАНДР / EVA",heading);
                Text(1238,801,310,35,$"КИСЛОРОД   {game.Oxygen:00}%",body);
                Fill(new Rect(1239,845,304,6),new Color(.1f,.18f,.23f));
                Fill(new Rect(1239,845,304*game.Oxygen/100,6),game.Oxygen<24 ? new Color(1,.35f,.15f) : cyan);
            }
            else if(view)
            {
                Box(new Rect(1220,738,350,130));
                bool bridgeHolstered=game.chapter==0 && view.weaponPivot && !view.weaponPivot.gameObject.activeSelf;
                if(view.BridgeSidearmVisible || bridgeHolstered)
                {
                    Text(1238,748,310,32,bridgeHolstered ? "МОСТИК / СНАРЯЖЕНИЕ" : view.WeaponTitle,small);
                    Text(1238,793,310,54,bridgeHolstered ? "Оружие убрано\nF8 — жесты снаряжения" : "Предохранитель включён",small);
                }
                else
                {
                    Text(1238,748,310,30,view.WeaponTitle,small);
                    Text(1238,782,310,60,$"{view.MagazineAmmo:00} / {view.ReserveAmmo:000}",ammo);
                    Text(1238,842,310,25,view.IsReloading ? "ПЕРЕЗАРЯДКА "+Mathf.RoundToInt(view.ReloadProgress*100)+"%" : view.IsSwitching ? "СМЕНА ОРУЖИЯ" : view.IsInspecting ? "ОСМОТР" : $"НАГРЕВ {game.Heat:00}%",small);
                }
                if(game.Running)
                {
                    DrawWeaponReticle(view);
                    if(view.DamagePulse>0)
                    {
                        Color damage=new(1,.08f,.02f,view.DamagePulse*.6f);
                        Fill(new Rect(0,0,1600,5),damage); Fill(new Rect(0,895,1600,5),damage);
                        Fill(new Rect(0,0,5,900),damage); Fill(new Rect(1595,0,5,900),damage);
                    }
                }
            }
            if(game.Running && (game.Carrying || game.Outside))
                DrawInteractionReticle();
            if(!string.IsNullOrEmpty(game.Prompt))
            { Box(new Rect(470,575,660,65)); Text(490,588,620,45,game.Prompt); }
            if(!string.IsNullOrEmpty(game.Subtitle) && (!game.Radio || !game.Radio.IsPlaying))
            { Box(new Rect(370,773,830,88)); Text(390,785,790,70,game.Subtitle); }
            Text(380,873,1220,24,"E ДЕЙСТВИЕ   R ПЕРЕЗАРЯДКА   Q СМЕНА   X ПИСТОЛЕТ   F ОСМОТР   C ПРИСЕД   H AI   F7 ГОЛОС   F8 КАМЕРА   ESC МЕНЮ",small);
            var target=game.NextObjective();
            if(target && game.Running)
            {
                Vector3 screen=game.eyes.WorldToViewportPoint(target.FocusPoint);
                bool behind=screen.z<=0;
                float x=behind ? screen.x>.5f ? 64 : 1450 : Mathf.Clamp(screen.x*1600,64,1450);
                float y=behind ? 468 : Mathf.Clamp((1-screen.y)*900,175,650);
                // Keep the objective label clear of the center of the sight.
                var sightArea=new Rect(ReticleCenter.x-60,ReticleCenter.y-60,120,120);
                if(sightArea.Overlaps(new Rect(x-7,y-3,102,30))) y=sightArea.yMax+3;
                var mark=new Color(1,.73f,.24f);
                Fill(new Rect(x-7,y-3,102,30),new Color(.035f,.03f,.02f,.91f));
                Fill(new Rect(x-7,y-3,2,30),mark);
                Text(x,y,95,28,(behind ? x<500 ? "‹ " : "› " : "◆  ")+Vector3.Distance(game.player.transform.position,target.FocusPoint).ToString("F0")+" м",small);
            }
            if(game.Boss && !game.Boss.Dead && game.Running) BossStatus(game.Boss);
            if(game.ShowDiagnostics) Diagnostics();
        }
        void BossStatus(CampaignEnemy boss)
        {
            Box(new Rect(735,25,350,124));
            Text(749,31,324,32,game.CoreExposed ? "ADAM / ДВОЙНИК" : "ADAM / ПОЛЕ АКТИВНО",small);
            Fill(new Rect(751,64,318,6),new Color(.17f,.19f,.24f));
            Fill(new Rect(751,64,318*Mathf.Clamp01(boss.health/CampaignEnemy.BossMaxHealth),6),game.CoreExposed ? new Color(1,.39f,.37f) : new Color(.57f,.33f,1));
            Text(749,75,325,28,boss.BossCue,small);
            if(boss.IsWarning)
                Fill(new Rect(751,108,318*boss.WarningProgress,4),amber);
            else if(boss.IsMirroring) Text(749,101,325,28,"КОПИРУЕТ ДВИЖЕНИЕ И ОГОНЬ",small);
            else if(boss.Phase==CampaignEnemy.BossPhase.Recovery && game.CoreExposed)
                Text(749,101,325,28,"УРОН В КОНТРАТАКЕ +18%",small);
        }
        void DrawRadio()
        {
            var radio=game.Radio;
            if(!radio || string.IsNullOrEmpty(radio.Line)) return;
            var accent=radio.Speaker=="ЗЕМЛЯ" ? new Color(.48f,1,.71f) :
                radio.Speaker=="ADAM" ? new Color(1,.37f,.45f) : cyan;
            Fill(new Rect(375,765,825,105),new Color(.007f,.021f,.033f,.95f));
            Fill(new Rect(375,765,3,105),accent);
            for(int i=0;i<9;i++)
            {
                float h=4+12*Mathf.Abs(Mathf.Sin(Time.unscaledTime*9+i*1.17f));
                Fill(new Rect(393+i*5,792-h*.5f,2,h),accent);
            }
            string channel=radio.Speaker=="ГЕРА" ? "ВНУТРЕННЯЯ СЕТЬ" : radio.Speaker=="ЗЕМЛЯ" ? "ВНЕШНИЙ КАНАЛ" : "СВЯЗЬ";
            Text(449,774,510,30,radio.Speaker+"   /   "+channel,small);
            Text(1033,774,145,30,"Tab — дальше",small);
            Text(393,807,785,59,radio.Line,body);
            Fill(new Rect(393,863,782*radio.Progress,2),accent);
        }
        void Diagnostics()
        {
            Box(new Rect(1110,25,460,365));
            Text(1130,38,420,37,"ТЕЛЕМЕТРИЯ БОЯ",heading);
            Text(1130,88,420,270,
                $"Примеры игрока: {CampaignSession.Data.samples}\nПосле полученного урона: {CampaignSession.Data.underFire}\nОбновления весов: {CampaignSession.Policy.Updates}\n"+
                (game.chapter==2 ? "Режим: адаптивный бой и отражение\n" : "Режим: запись в инженерном отсеке\n")+
                $"Приказ охране: {game.CommanderOrder}\nТвои выстрелы / попадания / приседы:\n{CampaignSession.Data.shots} / {CampaignSession.Data.hits} / {CampaignSession.Data.crouches}\n"+
                (game.Boss ? $"Двойник: {game.Boss.BossCue}\nСвоих выстрелов {game.Boss.AutonomousShots} · Отражённых {game.Boss.ReflectedShots}\nПопадания {game.Boss.Hits} · Приседы {game.Boss.Crouches}" : "F6 — сохранить отчёт"),small);
        }
        void Pause()
        {
            if(help) { Controls(default); return; }
            Fill(new Rect(0,0,1600,900),new Color(0,0,.015f,.7f)); Box(new Rect(120,150,600,630));
            Text(150,174,530,60,"ПАУЗА",heading);
            if(Button(150,250,540,"Продолжить")) game.SetPaused(false);
            if(Button(150,315,540,"Сохранить контрольную точку")) game.SaveCheckpoint();
            if(Button(150,380,540,"Вернуться к контрольной точке")) game.RestartCheckpoint();
            if(Button(150,445,540,"Управление и устройства")) help=!help;
            if(Button(150,510,540,"Брифинг операции / F1")) game.OpenBriefing();
            if(Button(150,575,540,"В главное меню")) game.BackToMenu();
            Text(150,647,530,45,$"Сигнатуры: {CampaignSession.Data.credits}",small);
            if(Button(150,698,540,CampaignSession.Data.alternateColor ? "Янтарный след открыт" : "Янтарный след / 200 сигнатур")) game.UnlockColor();
            if(help) Controls(new Rect(760,85,720,760));
        }
        void Controls(Rect rect)
        {
            Fill(new Rect(0,0,1600,900),new Color(.002f,.009f,.018f,.96f));
            Box(new Rect(50,35,1500,830));
            Text(75,53,1230,40,"УПРАВЛЕНИЕ И УСТРОЙСТВА",heading);
            if(GUI.Button(new Rect(1360,50,165,46),"Закрыть",button)) { help=false; return; }
            if(!game.Devices) return;
            var devices=game.Devices; float x=80,w=460;
            Text(x,116,w,35,"МИКРОФОН",heading);
            if(Button(x,160,290,devices.VoiceEnabled ? "Выключить / F7" : "Включить / F7")) devices.ToggleVoice();
            bool enabled=GUI.enabled; GUI.enabled=enabled && !devices.VoiceEnabled;
            if(Button(x+300,160,160,"Выбрать →")) devices.NextMicrophone();
            GUI.enabled=enabled;
            Text(x,218,w,42,devices.VoiceDeviceName,small);
            Text(x,262,w,50,devices.VoiceStatus,small);
            LevelBar(new Rect(x,318,w,7),devices.VoiceLevel);
            Text(x,336,w,35,"Голосовых команд принято: "+devices.VoiceCommandCount,small);
            Text(x,384,w,35,"КАМЕРА / ГОЛОВА И ЖЕСТЫ",heading);
            if(Button(x,428,290,devices.CameraEnabled ? "Выключить / F8" : "Включить / F8")) devices.ToggleCamera();
            GUI.enabled=enabled && !devices.CameraEnabled;
            if(Button(x+300,428,160,"Выбрать →")) devices.NextCamera();
            GUI.enabled=enabled;
            Text(x,486,w,36,devices.CameraDeviceName,small);
            Text(x,526,w,52,devices.CameraStatus,small);
            if(devices.CameraPreview) DrawCamera(new Rect(x,587,w,213));
            else { Fill(new Rect(x,587,w,213),Color.black); Text(x+20,650,w-40,80,"После включения здесь появятся твоё изображение и точки пальцев.",small); }
            Fill(new Rect(x,806,w,5),new Color(.15f,.22f,.28f));
            Fill(new Rect(x,806,w*devices.CameraProgress,5),cyan);
            Text(x,821,245,29,$"Чувствительность  {devices.HeadSensitivity:0.00}",small);
            float sensitivity=GUI.HorizontalSlider(new Rect(x+252,825,w-252,24),devices.HeadSensitivity,.7f,1.4f);
            if(Mathf.Abs(sensitivity-devices.HeadSensitivity)>.005f) devices.HeadSensitivity=sensitivity;
            float right=585,rw=925;
            string[] tabs={"Клавиатура","Голос","Жесты"};
            for(int tab=0;tab<3;tab++)
                if(GUI.Button(new Rect(right+tab*(rw/3),110,rw/3-8,45),(controlPage==tab ? "● " : "")+tabs[tab],button)) controlPage=tab;
            if(controlPage==0)
            {
                Text(right,180,rw,410,"WASD — ходьба; мышь — обзор\nShift — бег; C / Ctrl — присесть; пробел — прыжок\nЛКМ — огонь; ПКМ — прицеливание\nR — перезарядка; F — осмотр оружия\nQ / колесо — смена оружия; X — пистолет\n1 — достать оружие; 2 — раскрыть энергощит; 3 — ускорение\nE — взаимодействие с предметом / терминалом\nEsc — пауза; F1 — брифинг; H — телеметрия боя\nF7 — микрофон; F8 — камера; F9 — калибровка головы\nF5 — контрольная точка; F6 — отчёт о бое");
                Text(right,630,rw,160,"Смотри прямо и нажми F9. После калибровки слегка поверни голову, чтобы осмотреться. WASD ориентируется на текущий взгляд. Для движения жестом держи руку в кадре. Голосовая команда движения действует до трёх секунд.");
            }
            else if(controlPage==1)
            {
                Text(right,173,rw,42,"Говори одну команду, затем делай короткую паузу. Движение — до 3 с.",small);
                int row=0;
                foreach(var entry in SpokenCommandCatalog.Entries)
                {
                    float y=218+row*29;
                    if(row%2==0) Fill(new Rect(right-3,y,rw,28),new Color(.08f,.15f,.21f,.45f));
                    Text(right+8,y+2,420,27,"«"+entry.Phrases[0]+"»",small);
                    Text(right+440,y+2,rw-450,27,entry.Title,small); row++;
                }
                Text(right,839,rw,34,"На мостике «Гера, проверь связь» вызывает ответ бортового компьютера.",small);
            }
            else
            {
                Text(right,173,rw,46,"Рука целиком в кадре. Удержи жест ≈ 0,6 с. Для повтора убери руку.",small);
                string[,] rows={
                    {"Раскрытая ладонь","Стоп"}, {"Кулак","Достать щит"},
                    {"Один указательный, большой согнут","Автомат на мостике"},
                    {"Указательный + средний, большой согнут","Пистолет на мостике"},
                    {"Один указательный (вне мостика)","Движение по направлению"},
                    {"V (вне мостика) / три пальца*","Бежать / идти шагом"},
                    {"Большой палец вверх / вниз","Прыжок / присесть"},
                    {"Четыре пальца, большой прижат","Встать"},
                    {"Указательный + средний + безымянный","Перезарядка"},
                    {"Только мизинец","Осмотр оружия"},
                    {"Указательный + мизинец","Сменить оружие"},
                    {"Большой + указательный + мизинец","Достать оружие"},
                    {"ОК: большой и указательный кольцом","Взаимодействовать"},
                    {"Два кулака","Включить / убрать прицел"},
                    {"Ладонь одной руки + кулак другой","Один выстрел"},
                    {"Две раскрытые ладони","Пауза / продолжить"}
                };
                for(int row=0;row<rows.GetLength(0);row++)
                {
                    float y=226+row*34;
                    if(row%2==0) Fill(new Rect(right-3,y,rw,33),new Color(.08f,.15f,.21f,.45f));
                    Text(right+8,y+2,535,33,rows[row,0],small);
                    Text(right+550,y+2,rw-560,33,rows[row,1],small);
                }
                Text(right,787,rw,58,"* Большой + указательный + средний — шаг. F9 выставляет центр взгляда, пока ты смотришь прямо.",small);
            }
        }
        void LevelBar(Rect rect,float level)
        {
            Fill(rect,new Color(.15f,.22f,.28f));
            Fill(new Rect(rect.x,rect.y,rect.width*Mathf.Clamp01(level*4),rect.height),cyan);
        }
        void DeviceOverlay()
        {
            var devices=game.Devices;
            if(!devices || (!devices.VoiceEnabled && !devices.CameraEnabled)) return;
            float x=game.ShowDiagnostics ? 30 : 1110, y=game.ShowDiagnostics ? 175 : 25;
            float previewHeight=game.ShowDiagnostics ? 150 : 200;
            bool bridge=game.chapter==0;
            const float headStatusHeight=51;
            float height=(devices.CameraEnabled ? previewHeight+135+headStatusHeight : 0)+(devices.VoiceEnabled ? 125 : 0)+85;
            Box(new Rect(x,y,460,height)); float row=y+12;
            if(devices.CameraEnabled)
            {
                Text(x+20,row,420,35,"F8 / "+devices.CameraDeviceName,heading);
                if(devices.CameraPreview) DrawCamera(new Rect(x+20,row+42,420,previewHeight));
                else { Fill(new Rect(x+20,row+42,420,previewHeight),Color.black); Text(x+40,row+65,380,60,"Ожидание изображения камеры…",small); }
                Text(x+20,row+previewHeight+48,420,50,devices.CameraStatus,small);
                Fill(new Rect(x+20,row+previewHeight+100,420,4),new Color(.15f,.22f,.28f));
                Fill(new Rect(x+20,row+previewHeight+100,420*devices.CameraProgress,4),cyan);
                Text(x+20,row+previewHeight+110,420,25,"Удержи жест 0,6 с. Esc — список жестов.",small);
                row+=previewHeight+135;
                Text(x+20,row+2,420,47,devices.HeadTrackingStatus,small);
                row+=headStatusHeight;
            }
            if(devices.VoiceEnabled)
            {
                Text(x+20,row,420,32,"МИКРОФОН / F7 — ВЫКЛЮЧИТЬ",heading);
                LevelBar(new Rect(x+20,row+37,420,6),devices.VoiceLevel);
                Text(x+20,row+51,420,48,devices.VoiceStatus,small);
                Text(x+20,row+99,420,26,bridge ? "«Гера, проверь связь»" : "«Вперёд» · «Щит» · «Перезарядка»",small);
                row+=125;
            }
            Text(x+20,row+9,420,40,string.IsNullOrEmpty(devices.LastCommand) ? "Принятая команда появится здесь." : devices.LastCommand,small);
            Text(x+20,row+49,420,25,$"Голос: {devices.VoiceCommandCount}   Жесты: {devices.CameraCommandCount}   Esc — справка",small);
        }
        void DrawCamera(Rect target)
        {
            Fill(target,Color.black);
            var devices=game.Devices; var texture=devices.CameraPreview;
            if(!texture || texture.width<=16 || texture.height<=16) return;
            int rotation=devices.CameraRotation;
            float aspect=rotation%180==0 ? texture.width/(float)texture.height : texture.height/(float)texture.width;
            float width=Mathf.Min(target.width,target.height*aspect),height=width/aspect;
            target=new Rect(target.center.x-width*.5f,target.center.y-height*.5f,width,height);
            GUI.BeginGroup(target); Matrix4x4 before=GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(rotation,new Vector2(target.width*.5f,target.height*.5f));
                Rect image=rotation%180==0 ? new Rect(0,0,target.width,target.height) :
                    new Rect((target.width-target.height)*.5f,(target.height-target.width)*.5f,target.height,target.width);
                GUI.DrawTextureWithTexCoords(image,texture,devices.CameraVerticallyMirrored ? new Rect(0,1,1,-1) : new Rect(0,0,1,1));
            }
            finally { GUI.matrix=before; GUI.EndGroup(); }
            foreach(var hand in devices.CameraHands)
            {
                if(hand==null || hand.Landmarks==null || hand.Landmarks.Length<21) continue;
                int[,] links={{0,1},{1,2},{2,3},{3,4},{0,5},{5,6},{6,7},{7,8},{5,9},{9,10},{10,11},{11,12},{9,13},{13,14},{14,15},{15,16},{13,17},{0,17},{17,18},{18,19},{19,20}};
                for(int edge=0;edge<links.GetLength(0);edge++) DrawHandLine(Point(hand.Landmarks[links[edge,0]],target),Point(hand.Landmarks[links[edge,1]],target));
                foreach(var point in hand.Landmarks) { Vector2 pixel=Point(point,target); Fill(new Rect(pixel.x-2,pixel.y-2,4,4),ink); }
            }
            Fill(new Rect(target.x,target.yMax-29,target.width,29),new Color(0,0,0,.8f));
            Text(target.x+5,target.yMax-28,target.width-10,27,devices.CameraGesture,zoneLabel);
        }
        static Vector2 Point(Vector3 landmark,Rect rect) => new(rect.x+Mathf.Clamp01(landmark.x)*rect.width,rect.y+Mathf.Clamp01(landmark.y)*rect.height);
        void DrawHandLine(Vector2 a,Vector2 b)
        {
            Matrix4x4 previous=GUI.matrix;
            try { GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a); Fill(new Rect(a.x,a.y,Vector2.Distance(a,b),2),cyan); }
            finally { GUI.matrix=previous; }
        }
        void Terminal()
        {
            var flight=game.Flight;
            if(flight==null) return;
            Fill(new Rect(0,0,1600,900),new Color(.012f,.03f,.048f,.18f));
            Box(new Rect(30,25,690,111));
            Text(50,38,650,39,"РУЧНОЕ УПРАВЛЕНИЕ / КОРРЕКЦИЯ КУРСА",heading);
            Text(50,85,650,37,"Совмести метку и удерживай курс до захвата.",body);
            Box(new Rect(1120,29,450,304));
            Text(1141,49,407,36,"ОШИБКА ОРИЕНТАЦИИ",heading);
            var correction=flight.Error;
            Text(1141,103,403,35,$"W / S   ТАНГАЖ    {correction.x:+0.0;-0.0;0.0}°");
            Text(1141,153,403,35,$"A / D   РЫСКАНИЕ    {correction.y:+0.0;-0.0;0.0}°");
            Text(1141,203,403,35,$"Q / E   КРЕН    {correction.z:+0.0;-0.0;0.0}°");
            Text(1141,261,409,32,"Цель: ноль по всем трём осям.",small);
            Color indicator=flight.Ready ? new Color(.33f,1,.65f) : new Color(1,.72f,.25f);
            Fill(new Rect(747,450,106,2),cyan); Fill(new Rect(799,425,2,53),cyan);
            float px=800+correction.y*7,py=450-correction.x*7;
            var before=GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(correction.z,new Vector2(px,py));
                Fill(new Rect(px-52,py,104,2),indicator);
                Fill(new Rect(px-52,py-12,2,14),indicator); Fill(new Rect(px+50,py-12,2,14),indicator);
                Fill(new Rect(px-2,py-2,4,4),indicator);
            }
            finally { GUI.matrix=before; }
            Box(new Rect(550,632,500,121));
            Text(574,640,456,33,flight.Ready ? "КУРС ЗАХВАЧЕН / ГОТОВО К ИМПУЛЬСУ" : "СТАБИЛИЗАЦИЯ "+Mathf.RoundToInt(flight.Stability*100)+"%",small);
            Fill(new Rect(574,673,452*flight.Stability,3),indicator);
            bool enabled=GUI.enabled; GUI.enabled=enabled && flight.Ready;
            if(Button(574,687,452,"Пробел / Подать импульс")) game.CloseTerminal(game.Energy);
            GUI.enabled=enabled;
            if(Button(30,685,240,"Esc / Встать с пульта")) game.CancelTerminal();
        }
        void Result()
        {
            Fill(new Rect(0,0,1600,900),new Color(0,0,.01f,.8f)); Box(new Rect(350,170,900,570));
            Text(390,205,820,70,game.Victory ? "СВЯЗЬ ВОССТАНОВЛЕНА" : "СИГНАЛ ПОТЕРЯН",heading);
            if(game.Victory) Text(390,251,820,36,"Земля приняла SOS. Маяк активен, спасатели в пути.",small);
            Text(390,300,820,150,$"Время: {CampaignSession.Data.seconds/60:F1} мин\nОхрана нейтрализована: {CampaignSession.Data.kills}\nВыстрелы / попадания: {CampaignSession.Data.shots} / {CampaignSession.Data.hits}\nЗаписано боевых примеров: {CampaignSession.Data.samples}.");
            if(!game.Victory && Button(390,475,820,"Вернуться к контрольной точке")) game.RestartCheckpoint();
            if(Button(390,540,820,"Сохранить отчёт об AI")) game.ExportReport();
            if(Button(390,605,820,"В главное меню")) game.BackToMenu();
        }
        void OnDestroy()
        {
            if(ownsFont && font) Destroy(font);
            if(menuShade) Destroy(menuShade);
        }
    }
}
