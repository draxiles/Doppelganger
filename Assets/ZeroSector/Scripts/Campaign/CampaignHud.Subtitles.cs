using UnityEngine;

namespace ZeroSector.Campaign
{
    public sealed partial class CampaignHud
    {
        const string LargeSubtitlePreference = "Doppelganger.Subtitles.Large";
        const string ContrastSubtitlePreference = "Doppelganger.Subtitles.HighContrast";
        const float RadioBottom = 870;
        const float MinimumRadioHeight = 105;

        readonly GUIContent subtitleContent = new();
        bool largeSubtitles, highContrastSubtitles;
        GUIStyle largeSubtitleStyle, contrastSubtitleStyle, largeContrastSubtitleStyle, contrastCaptionStyle;

        public bool LargeSubtitles
        {
            get => largeSubtitles;
            set => SetSubtitlePreference(ref largeSubtitles, LargeSubtitlePreference, value);
        }

        public bool HighContrastSubtitles
        {
            get => highContrastSubtitles;
            set => SetSubtitlePreference(ref highContrastSubtitles, ContrastSubtitlePreference, value);
        }

        void LoadSubtitlePreferences()
        {
            largeSubtitles = PlayerPrefs.GetInt(LargeSubtitlePreference) == 1;
            highContrastSubtitles = PlayerPrefs.GetInt(ContrastSubtitlePreference) == 1;
        }

        static void SetSubtitlePreference(ref bool current, string preference, bool enabled)
        {
            if (current == enabled) return;
            current = enabled;
            PlayerPrefs.SetInt(preference, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        void SubtitleControls(float x, float width)
        {
            Text(x, 180, width, 40, "СУБТИТРЫ РАДИО", heading);
            Text(x, 231, width, 54, "Размер и контраст текста радиопереговоров.", body);

            float optionWidth = (width - 16) / 2;
            float contrastX = x + optionWidth + 16;
            Text(x, 305, optionWidth, 30, "Размер текста", small);
            if (Button(x, 340, optionWidth, LargeSubtitles ? "Крупный" : "Обычный"))
                LargeSubtitles = !LargeSubtitles;
            Text(contrastX, 305, optionWidth, 30, "Повышенный контраст", small);
            if (Button(contrastX, 340, optionWidth, HighContrastSubtitles ? "Включён" : "Выключен"))
                HighContrastSubtitles = !HighContrastSubtitles;

            Text(x, 437, width, 35, "ПРИМЕР", small);
            DrawRadioSubtitle(x, 652, width, "ГЕРА",
                "Командир Алекс, это Гера, бортовой компьютер. Я на внутренней связи. «Сигнал» теряет высоту, Земля не отвечает.",
                .55f, false);
        }

        float DrawRadio()
        {
            var radio = game.Radio;
            if (!radio || string.IsNullOrEmpty(radio.Line)) return RadioBottom - MinimumRadioHeight;
            return DrawRadioSubtitle(375, RadioBottom, 825, radio.Speaker, radio.Line, radio.Progress, true);
        }

        float DrawRadioSubtitle(float x, float bottom, float width, string speaker, string line, float progress, bool live)
        {
            GUIStyle textStyle = body;
            GUIStyle captionStyle = small;
            if (HighContrastSubtitles)
            {
                textStyle = LargeSubtitles
                    ? (largeContrastSubtitleStyle ??= Style(29, Color.white))
                    : (contrastSubtitleStyle ??= Style(21, Color.white));
                captionStyle = contrastCaptionStyle ??= Style(17, Color.white, FontStyle.Bold);
            }
            else if (LargeSubtitles)
                textStyle = largeSubtitleStyle ??= Style(29, ink);

            const float textOffset = 42;
            const float footer = 11;
            float textWidth = width - 40;
            subtitleContent.text = line;
            float measuredHeight = Mathf.Ceil(textStyle.CalcHeight(subtitleContent, textWidth));
            float height = Mathf.Max(MinimumRadioHeight, textOffset + measuredHeight + footer);
            float top = bottom - height;

            Color accent = speaker == "ЗЕМЛЯ" ? new Color(.48f, 1, .71f)
                : speaker == "ADAM" ? new Color(1, .37f, .45f) : cyan;
            Fill(new Rect(x, top, width, height), HighContrastSubtitles ? Color.black : new Color(.007f, .021f, .033f, .95f));
            Fill(new Rect(x, top, 3, height), accent);
            float signalTime = live ? Time.unscaledTime * 9 : 0;
            for (int i = 0; i < 9; i++)
            {
                float barHeight = 4 + 12 * Mathf.Abs(Mathf.Sin(signalTime + i * 1.17f));
                Fill(new Rect(x + 18 + i * 5, top + 27 - barHeight * .5f, 2, barHeight), accent);
            }
            string channel = speaker == "ГЕРА" ? "ВНУТРЕННЯЯ СЕТЬ" : speaker == "ЗЕМЛЯ" ? "ВНЕШНИЙ КАНАЛ" : "СВЯЗЬ";
            Text(x + 74, top + 9, width - 315, 30, speaker + "   /   " + channel, captionStyle);
            if (live) Text(x + width - 167, top + 9, 145, 30, "Tab — дальше", captionStyle);
            GUI.Label(new Rect(x + 18, top + textOffset, textWidth, height - textOffset - footer), subtitleContent, textStyle);
            Fill(new Rect(x + 18, bottom - 7, (width - 43) * Mathf.Clamp01(progress), 2), accent);
            return top;
        }
    }
}
