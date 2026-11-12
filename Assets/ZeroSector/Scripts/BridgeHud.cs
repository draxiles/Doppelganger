using UnityEngine;
using UnityEngine.UI;

namespace ZeroSector
{
    // Overlay canvas renders text at the current display resolution, after scene effects.
    public sealed class BridgeHud : MonoBehaviour
    {
        Canvas canvas;
        Font font;
        Text objective, status, message;
        GameObject transmissionPanel, terminalPanel, pausePanel, prompt;
        readonly Image[] choices = new Image[3];
        readonly Text[] choiceLabels = new Text[3];
        readonly Color ink = new(.86f,.93f,.97f,1);
        readonly Color accent = new(.22f,.8f,.95f,1);
        readonly Color panel = new(.012f,.025f,.04f,.95f);

        public void Initialize()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] {"Segoe UI", "Arial", "DejaVu Sans"}, 24);
            var root = new GameObject("Doppelganger • HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440,900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;

            RectTransform top = Box(root.transform,"Mission",new Vector2(0,1),new Vector2(32,-30),new Vector2(920,150),new Color(0,0,0,0));
            Box(top,"Accent",new Vector2(0,1),Vector2.zero,new Vector2(3,82),accent);
            Label(top,"Chapter","D O P P E L G Ä N G E R    /    ЭПИЗОД 01",new Vector2(20,-2),new Vector2(800,26),15,accent);
            Label(top,"Title","ЧУЖОЙ ГОЛОС",new Vector2(18,-36),new Vector2(800,45),30,ink,FontStyle.Bold);
            objective=Label(top,"Objective","",new Vector2(20,-91),new Vector2(900,55),20,ink);
            var network=Box(root.transform,"Network",new Vector2(1,1),new Vector2(-325,-33),new Vector2(295,35),new Color(0,0,0,0));
            status=Label(network,"Network status","",Vector2.zero,new Vector2(295,35),15,accent);
            Box(root.transform,"Crosshair H",new Vector2(.5f,.5f),new Vector2(-4,1),new Vector2(8,2),accent);
            Box(root.transform,"Crosshair V",new Vector2(.5f,.5f),new Vector2(-1,4),new Vector2(2,8),accent);

            var tips=Box(root.transform,"Controls",new Vector2(0,0),new Vector2(32,48),new Vector2(1370,34),new Color(0,0,0,0));
            Label(tips,"Keys","WASD  движение     МЫШЬ  обзор     SHIFT  бег     C  присесть     E  действие     ESC  курсор",Vector2.zero,new Vector2(1370,34),17,accent);
            var tx=Box(root.transform,"Transmission",new Vector2(.5f,0),new Vector2(-490,180),new Vector2(980,112),panel);
            transmissionPanel=tx.gameObject;
            Box(tx,"Signal",new Vector2(0,1),Vector2.zero,new Vector2(3,112),accent);
            message=Label(tx,"Message","",new Vector2(24,-18),new Vector2(932,85),21,ink);
            var hint=Box(root.transform,"Interact",new Vector2(.5f,.5f),new Vector2(-230,-65),new Vector2(460,55),panel);
            prompt=hint.gameObject;
            Label(hint,"Hint","[ E ]  Командирский терминал",new Vector2(20,-14),new Vector2(420,38),21,ink);

            var terminal=Box(root.transform,"Reactor",new Vector2(.5f,.5f),new Vector2(-320,235),new Vector2(640,470),panel);
            terminalPanel=terminal.gameObject;
            Label(terminal,"Heading","УПРАВЛЕНИЕ РЕАКТОРОМ",new Vector2(32,-30),new Vector2(580,50),27,ink,FontStyle.Bold);
            Label(terminal,"Description","Распределение энергии / приоритет системы",new Vector2(32,-95),new Vector2(580,58),21,ink);
            for(int i=0;i<3;i++)
            {
                var row=Box(terminal,"Priority "+i,new Vector2(0,1),new Vector2(32,-170-i*65),new Vector2(576,50),panel);
                choices[i]=row.GetComponent<Image>();
                choiceLabels[i]=Label(row,"Label","",new Vector2(18,-10),new Vector2(545,40),22,ink);
            }
            Label(terminal,"Close","Переборка открыта. [ E ] — выйти",new Vector2(32,-405),new Vector2(580,40),18,accent);
            var pause=Box(root.transform,"Paused",new Vector2(.5f,.5f),new Vector2(-340,78),new Vector2(680,156),panel);
            pausePanel=pause.gameObject;
            Label(pause,"Game title","DOPPELGÄNGER",new Vector2(30,-24),new Vector2(620,66),42,ink,FontStyle.Bold);
            Label(pause,"Resume","Нажмите в игровом окне, чтобы продолжить",new Vector2(30,-100),new Vector2(620,42),22,ink);
            Refresh(false,false,false,false,1,false,"");
        }

        static RectTransform Place(GameObject go,Transform parent,Vector2 anchor,Vector2 position,Vector2 size)
        {
            var rect=go.GetComponent<RectTransform>();
            rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=anchor;
            rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=position;
            rect.sizeDelta=size;
            return rect;
        }

        RectTransform Box(Transform parent,string name,Vector2 anchor,Vector2 position,Vector2 size,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));
            var rect=Place(go,parent,anchor,position,size);
            var image=go.GetComponent<Image>();
            image.color=color;
            image.raycastTarget=false;
            return rect;
        }

        Text Label(Transform parent,string name,string value,Vector2 position,Vector2 size,int fontSize,Color color,FontStyle style=FontStyle.Normal)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));
            Place(go,parent,new Vector2(0,1),position,size);
            var text=go.GetComponent<Text>();
            text.font=font;
            text.fontSize=fontSize;
            text.fontStyle=style;
            text.color=color;
            text.text=value;
            text.raycastTarget=false;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;
            text.verticalOverflow=VerticalWrapMode.Overflow;
            return text;
        }

        public void Refresh(bool captured,bool powered,bool finished,bool consoleOpen,int priority,bool canInteract,string transmission)
        {
            if(!canvas) return;
            objective.text=finished ? "Сигнал перехвачен. Продолжение следует." : powered ? "ЗАДАЧА  /  Пройдите в технический отсек за мостиком" : "ЗАДАЧА  /  Восстановите питание на центральной консоли";
            status.text=powered ? "СЕТЬ  /  ВОССТАНОВЛЕНА" : "СЕТЬ  /  АВАРИЯ";
            transmissionPanel.SetActive(!string.IsNullOrEmpty(transmission));
            message.text=transmission;
            prompt.SetActive(canInteract);
            terminalPanel.SetActive(consoleOpen);
            pausePanel.SetActive(!captured);
            string[] labels={"[1]  ОРУЖИЕ","[2]  ЩИТЫ","[3]  ДВИГАТЕЛИ"};
            for(int i=0;i<3;i++)
            {
                choices[i].color=i==priority ? new Color(.04f,.27f,.34f) : new Color(.035f,.07f,.1f);
                choiceLabels[i].text=labels[i]+(i==priority ? "    /  60%" : "    /  20%");
            }
        }

        void OnDestroy()
        {
            if(canvas) Destroy(canvas.gameObject);
            if(font) Destroy(font);
        }
    }
}
