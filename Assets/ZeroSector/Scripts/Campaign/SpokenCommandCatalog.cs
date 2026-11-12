using System;
using System.Collections.Generic;
using System.Text;

namespace ZeroSector.Campaign
{
    // One vocabulary is used by the native recognizer, action router and help panel.
    public static class SpokenCommandCatalog
    {
        public sealed class Entry
        {
            public readonly string Action, Title;
            public readonly string[] Phrases;
            public Entry(string action,string title,params string[] phrases)
            { Action=action; Title=title; Phrases=phrases; }
        }

        public static readonly Entry[] Entries=
        {
            new("forward","Вперёд","вперед","вперёд"),
            new("backward","Назад","назад"),
            new("left","Влево","влево"),
            new("right","Вправо","вправо"),
            new("stop","Остановиться","стоп","стой","остановись"),
            new("run","Бежать","бежать","беги","бег","ускорение","двигатели","ко мне"),
            new("walk","Идти шагом","идти","шагом"),
            new("jump","Прыжок","прыжок","прыгай"),
            new("crouch","Присесть","присесть","присядь","присед"),
            new("stand","Встать","встать","встань"),
            new("reload","Перезарядка","перезарядка","перезарядить"),
            new("inspect","Осмотр оружия","осмотр","осмотреть оружие"),
            new("shield","Достать щит","щит","достать щит","поднять щит","оборона","щиты"),
            new("weapon","Достать оружие","оружие","достать оружие","убрать щит","атака"),
            new("pistol","Достать пистолет","пистолет","достать пистолет"),
            new("rifle","Достать автомат","автомат","достать автомат"),
            new("switch","Сменить оружие","сменить оружие","поменять оружие"),
            new("interact","Взаимодействовать","взаимодействие","использовать"),
            new("aim","Включить / убрать прицел","прицел","прицелиться"),
            new("fire","Выстрел","огонь","выстрел"),
            new("check_link","Проверить связь с Землёй","Гера, проверь связь"),
            new("pause","Пауза","пауза"),
            new("resume","Продолжить","продолжить")
        };

        static readonly Dictionary<string,string> Actions=BuildActions();
        public static readonly string Grammar=BuildGrammar();

        public static string Normalize(string phrase)
        {
            if(string.IsNullOrWhiteSpace(phrase)) return "";
            var words=phrase.Trim().ToLowerInvariant().Replace('ё','е').Replace(',',' ')
                .Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ",words);
        }

        public static bool TryResolve(string phrase,out string action) => Actions.TryGetValue(Normalize(phrase),out action);
        public static string GetActionTitle(string action)
        {
            foreach(var entry in Entries) if(entry.Action==action) return entry.Title;
            return action ?? "";
        }

        static Dictionary<string,string> BuildActions()
        {
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var entry in Entries) foreach(string phrase in entry.Phrases)
            {
                string normalized=Normalize(phrase);
                if(result.TryGetValue(normalized,out string previous) && previous!=entry.Action)
                    throw new InvalidOperationException("Ambiguous spoken command: "+normalized);
                result[normalized]=entry.Action;
            }
            return result;
        }

        static string BuildGrammar()
        {
            var values=new List<string>();
            foreach(var entry in Entries) foreach(string phrase in entry.Phrases)
            {
                string normalized=Normalize(phrase);
                // Vosk's Russian lexicon spells this word with ё. Resolving the
                // transcript still accepts both spellings and any casing.
                string nativePhrase=normalized=="вперед" ? "вперёд" : normalized;
                // The small model lacks this abbreviated noun; its complete
                // spoken alternatives «присесть» and «присядь» are in the grammar.
                if(normalized=="присед") continue;
                if(!values.Contains(nativePhrase)) values.Add(nativePhrase);
            }
            values.Add("[unk]");
            var json=new StringBuilder("[");
            for(int index=0;index<values.Count;index++)
            { if(index>0) json.Append(','); json.Append('"').Append(values[index]).Append('"'); }
            return json.Append(']').ToString();
        }
    }
}
