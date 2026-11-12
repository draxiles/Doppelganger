using UnityEngine;
using UnityEngine.UI;

namespace ZeroSector.Learning
{
    public sealed class ArenaHud : MonoBehaviour
    {
        Canvas canvas;
        Font font;
        Text mode, model, metrics, notice, resources, opponent, counters, stance, location;
        GameObject intro, diagnostics;
        readonly RectTransform[] reticle = new RectTransform[4];
        readonly Image[] hitMarker = new Image[4], damageEdges = new Image[4];
        Image healthBar, shieldBar, heatBar, enemyBar, center;
        bool showDiagnostics;
        readonly Color ink=new(.86f,.94f,1), cyan=new(.2f,.83f,1), bg=new(.012f,.025f,.045f,.82f);

        public void Initialize()
        {
            font=Font.CreateDynamicFontFromOSFont(new[]{"Segoe UI","Arial","DejaVu Sans"},28);
            var root=new GameObject("Doppelganger combat HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.pixelPerfect=true;
            var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1600,900); scaler.matchWidthOrHeight=.5f;
            var title=Panel(root.transform,"Mission",new Vector2(0,1),new Vector2(30,-28),new Vector2(475,78),bg);
            Panel(title,"Accent",new Vector2(0,1),Vector2.zero,new Vector2(3,78),cyan);
            location=Label(title,"ДВОЙНИК  /  КОНТАКТ 01",new Vector2(18,-10),new Vector2(435,27),19,ink,FontStyle.Bold);
            mode=Label(title,"",new Vector2(18,-42),new Vector2(435,25),15,cyan);
            var topRight=Panel(root.transform,"Opponent telemetry",new Vector2(1,1),new Vector2(-380,-28),new Vector2(350,125),bg);
            opponent=Label(topRight,"",new Vector2(16,-10),new Vector2(318,23),16,ink,FontStyle.Bold);
            enemyBar=Bar(topRight,"Enemy shield and hull",new Vector2(16,-40),new Vector2(318,3),new Color(1,.43f,.22f));
            counters=Label(topRight,"",new Vector2(16,-54),new Vector2(318,66),15,ink);
            var bottom=Panel(root.transform,"Suit telemetry",new Vector2(0,0),new Vector2(30,148),new Vector2(340,118),bg);
            resources=Label(bottom,"",new Vector2(16,-10),new Vector2(310,25),18,ink,FontStyle.Bold);
            healthBar=Bar(bottom,"Health",new Vector2(16,-44),new Vector2(308,5),ink);
            shieldBar=Bar(bottom,"Shield",new Vector2(16,-57),new Vector2(308,5),cyan);
            heatBar=Bar(bottom,"Heat",new Vector2(16,-70),new Vector2(308,3),new Color(1,.53f,.2f));
            stance=Label(bottom,"",new Vector2(16,-80),new Vector2(310,36),12,cyan);
            notice=Label(root.transform,"",new Vector2(400,111),new Vector2(800,55),16,ink);
            notice.rectTransform.anchorMin=notice.rectTransform.anchorMax=new Vector2(0,0);
            var help=Label(root.transform,"WASD ДВИЖЕНИЕ   SHIFT БЕГ   C ПРИСЕД   ПКМ ПРИЦЕЛ   F ОСМОТР   Q / КОЛЕСО ОРУЖИЕ   TAB РЕЖИМ   H ДАННЫЕ",new Vector2(0,24),new Vector2(1600,22),12,new Color(.7f,.8f,.87f,.8f));
            help.rectTransform.anchorMin=help.rectTransform.anchorMax=new Vector2(0,0); help.alignment=TextAnchor.MiddleCenter;
            diagnostics=Panel(root.transform,"Learning diagnostics",new Vector2(1,1),new Vector2(-450,-175),new Vector2(420,490),bg).gameObject;
            model=Label(diagnostics.transform,"",new Vector2(18,-18),new Vector2(384,205),17,ink);
            metrics=Label(diagnostics.transform,"",new Vector2(18,-240),new Vector2(384,225),16,cyan);
            diagnostics.SetActive(false);
            center=Panel(root.transform,"Aim dot",new Vector2(.5f,.5f),new Vector2(-1.5f,1.5f),new Vector2(3,3),ink).GetComponent<Image>();
            for(int i=0;i<4;i++)
            {
                reticle[i]=Panel(root.transform,"Reticle "+i,new Vector2(.5f,.5f),Vector2.zero,i<2 ? new Vector2(7,1.5f) : new Vector2(1.5f,7),ink);
                reticle[i].pivot=new Vector2(.5f,.5f);
                var mark=Panel(root.transform,"Hit "+i,new Vector2(.5f,.5f),Vector2.zero,new Vector2(7,2),Color.clear);
                mark.pivot=new Vector2(.5f,.5f); mark.anchoredPosition=new Vector2(i%2==0 ? -10 : 10,i<2 ? 10 : -10);
                mark.localRotation=Quaternion.Euler(0,0,i==0 || i==3 ? -45 : 45);
                hitMarker[i]=mark.GetComponent<Image>();
            }
            damageEdges[0]=Panel(root.transform,"Damage top",new Vector2(0,1),Vector2.zero,new Vector2(1600,4),Color.clear).GetComponent<Image>();
            damageEdges[1]=Panel(root.transform,"Damage bottom",new Vector2(0,0),new Vector2(0,4),new Vector2(1600,4),Color.clear).GetComponent<Image>();
            damageEdges[2]=Panel(root.transform,"Damage left",new Vector2(0,1),Vector2.zero,new Vector2(4,900),Color.clear).GetComponent<Image>();
            damageEdges[3]=Panel(root.transform,"Damage right",new Vector2(1,1),new Vector2(-4,0),new Vector2(4,900),Color.clear).GetComponent<Image>();
            var overlay=Panel(root.transform,"Pause and instructions",new Vector2(.5f,.5f),new Vector2(-395,190),new Vector2(790,380),bg);
            intro=overlay.gameObject;
            Label(overlay,"ДВОЙНИК / ИСПЫТАТЕЛЬНЫЙ ОТСЕК",new Vector2(30,-24),new Vector2(730,44),25,cyan,FontStyle.Bold);
            Label(overlay,"Нажми ЛКМ, чтобы войти. Esc — освободить мышь.\n\nF1: учись на моих действиях под ответным огнём.\nF2: бой с обученной моделью, веса заморожены.\nF4: новый раунд. F3: очистить модель.\nF5: сохранить модель. F9: загрузить. F6: отчёт.\nH: показать вероятности и качество обучения.\n\n1 / 2 / 3: энергия в оружие / щиты / двигатели.",new Vector2(30,-85),new Vector2(730,280),19,ink);
        }

        public void ToggleDiagnostics() { showDiagnostics=!showDiagnostics; if(diagnostics) diagnostics.SetActive(showDiagnostics); }
        public void SetLocation(string title) { if(location) location.text="ДВОЙНИК  /  "+title; }
        RectTransform Panel(Transform parent,string name,Vector2 anchor,Vector2 position,Vector2 size,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));
            var rect=go.GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=anchor; rect.pivot=new Vector2(0,1); rect.anchoredPosition=position; rect.sizeDelta=size;
            var image=go.GetComponent<Image>(); image.color=color; image.raycastTarget=false;
            return rect;
        }
        Image Bar(Transform parent,string name,Vector2 position,Vector2 size,Color color)
        {
            var rail=Panel(parent,name+" rail",new Vector2(0,1),position,size,new Color(.35f,.45f,.5f,.3f));
            return Panel(rail,name,new Vector2(0,1),Vector2.zero,size,color).GetComponent<Image>();
        }
        Text Label(Transform parent,string content,Vector2 position,Vector2 size,int points,Color color,FontStyle style=FontStyle.Normal)
        {
            var go=new GameObject("Text",typeof(RectTransform),typeof(Text));
            var rect=go.GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=position; rect.sizeDelta=size;
            var text=go.GetComponent<Text>(); text.font=font; text.fontSize=points; text.color=color; text.fontStyle=style;
            text.text=content; text.raycastTarget=false; text.verticalOverflow=VerticalWrapMode.Overflow;
            return text;
        }
        static void Fill(Image bar,float fraction) { bar.rectTransform.localScale=new Vector3(Mathf.Clamp01(fraction),1,1); }
        public void Refresh(bool learning,bool captured,bool roundOver,string status,string network,string diagnosticsText,string note)
        {
            mode.text=roundOver ? "РАУНД ОКОНЧЕН  /  F4 — ПОВТОР" : learning ? "F1  ОБУЧЕНИЕ ПОД ОГНЁМ" : "F2  ДУЭЛЬ  /  ВЕСА ЗАМОРОЖЕНЫ";
            model.text=network; metrics.text=diagnosticsText; notice.text=note; intro.SetActive(!captured);
        }
        public void RefreshCombat(FirstPersonPresentation view,float hp,float shield,float heat,float enemyHp,float enemyShield,
            ImitationArena.CombatCounters human,ImitationArena.CombatCounters enemy,bool learning,bool crouch,string power)
        {
            resources.text=$"{hp:000} HP    {shield:00} ЩИТ";
            stance.text=(crouch ? "ПРИСЕД" : view.IsSprinting ? "БЕГ" : view.Movement>.1f ? "ХОДЬБА" : "СТОЙКА")+"   /   ЭНЕРГИЯ: "+power+"\nНАГРЕВ "+heat.ToString("F0")+"%";
            opponent.text=learning ? "УЧЕБНЫЙ ПРОТИВНИК" : "ДВОЙНИК / ОБУЧЕННАЯ МОДЕЛЬ";
            counters.text=$"Выстрелы {enemy.shots}   Попадания {enemy.hits}\nПриседы {enemy.crouchEntries}   |   За сессию\nТвои: {human.shots} / {human.hits} / {human.crouchEntries}";
            Fill(healthBar,hp/100); Fill(shieldBar,shield/60); Fill(heatBar,heat/100); Fill(enemyBar,(enemyHp+enemyShield)/160);
            float gap=Mathf.Lerp(8+view.Movement*5+view.ShotPulse*10,3,view.AimAmount);
            reticle[0].anchoredPosition=new Vector2(-gap-3,0); reticle[1].anchoredPosition=new Vector2(gap+3,0);
            reticle[2].anchoredPosition=new Vector2(0,gap+3); reticle[3].anchoredPosition=new Vector2(0,-gap-3);
            for(int i=0;i<4;i++)
            {
                reticle[i].GetComponent<Image>().color=new Color(ink.r,ink.g,ink.b,1-view.AimAmount*.9f);
                hitMarker[i].color=new Color(1,.64f,.25f,view.HitPulse);
                damageEdges[i].color=new Color(1,.15f,.08f,view.DamagePulse*.8f);
            }
            center.color=Color.Lerp(ink,new Color(1,.65f,.3f),view.HitPulse);
        }
        void OnDestroy() { if(canvas) Destroy(canvas.gameObject); if(font) Destroy(font); }
    }
}
