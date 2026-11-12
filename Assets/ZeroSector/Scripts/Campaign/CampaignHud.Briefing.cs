using UnityEngine;

namespace ZeroSector.Campaign
{
    public sealed partial class CampaignHud
    {
        static readonly (string Title,string Instructions)[] MissionSteps=
        {
            ("Командный мостик", "Прочитай журнал, установи аккумулятор в щиток и выровняй курс на пульте. Подай импульс и спустись к шлюзу."),
            ("Пробитый шлюз", "Восстанови давление у пульта, зачисти грузовую палубу и считай журнал C–04. Вызови подъёмник."),
            ("Инженерный отсек", "Включи реле A и B, обезвредь оставшихся роботов и открой проход к ядру."),
            ("Ядро", "Отключи оба контура охлаждения. Победи Двойника и сними блокировку связи на терминале."),
            ("Внешняя антенна", "Выйди через шлюз, наведи антенну на Землю, передай SOS и дождись ответа.")
        };

        GUIStyle briefingTitle;

        void Briefing()
        {
            briefingTitle ??= Style(39,ink,FontStyle.Bold);
            Fill(new Rect(0,0,1600,900),new Color(.003f,.009f,.016f,.93f));
            Box(new Rect(105,63,1390,774));
            Text(156,99,940,64,"Брифинг операции «Сигнал»",briefingTitle);
            Text(157,163,940,42,"Восстанови связь с Землёй и дождись ответа.");
            Fill(new Rect(156,216,1288,1),new Color(.35f,.49f,.6f,.32f));

            int current=-1;
            if(!game.MainMenu)
                current=game.chapter==0 ? 0 : game.OnBoardingRoute ? 1 : game.chapter==1 ? 2 :
                    CampaignSession.Has("core") ? 4 : 3;

            for(int i=0;i<MissionSteps.Length;i++)
            {
                float y=237+i*89;
                bool active=i==current;
                Color accent=active ? amber : cyan;
                Fill(new Rect(156,y,842,79),active ? new Color(.15f,.10f,.065f,.82f) : new Color(.042f,.065f,.089f,.63f));
                Fill(new Rect(156,y,active ? 3 : 1,79),active ? amber : new Color(.35f,.49f,.6f,.32f));
                Text(178,y+12,40,29,(i+1).ToString("00"),menuKicker);
                Text(229,y+6,630,36,MissionSteps[i].Title,menuSecondary);
                Text(229,y+38,744,39,MissionSteps[i].Instructions,small);
                if(active || i<current)
                {
                    Fill(new Rect(855,y+11,119,26),new Color(accent.r,accent.g,accent.b,.13f));
                    Text(855,y+11,119,26,active ? "СЕЙЧАС" : "ВЫПОЛНЕНО",zoneLabel);
                }
            }

            Fill(new Rect(1020,237,424,435),new Color(.039f,.068f,.097f,.88f));
            Text(1046,253,373,35,current<0 ? "Первая задача" : "Текущая задача",heading);
            Text(1046,296,373,103,current<0 ? "Найди журнал у места командира на мостике." : game.Objective);
            Fill(new Rect(1046,407,372,1),new Color(.35f,.49f,.6f,.32f));
            Text(1046,429,372,36,"Что пригодится",heading);
            Text(1046,478,372,177,
                "Жёлтая метка указывает на цель. Нажми E для взаимодействия.\n\nПКМ сужает прицел. Стреляй короткими очередями, чтобы держать разброс под контролем.");

            Fill(new Rect(156,715,1288,1),new Color(.35f,.49f,.6f,.32f));
            Text(156,750,815,44,"F1 — открыть брифинг   ·   Enter / Esc — закрыть",small);
            var close=new Rect(1055,741,389,57);
            Fill(close,close.Contains(Event.current.mousePosition) ? new Color(1,.78f,.49f) : amber);
            Text(1078,753,335,35,game.BriefingStartsOperation ? "На мостик" : "Закрыть брифинг",menuPrimary);
            Text(1408,753,25,35,"›",menuPrimary);
            if(GUI.Button(close,GUIContent.none,GUIStyle.none)) game.CloseBriefing();
        }
    }
}
