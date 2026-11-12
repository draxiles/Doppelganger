using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroSector.Campaign
{
    /// <summary>
    /// Semantic input shared by speech and camera. It never emulates operating-system
    /// keys: the campaign consumes the same requests as its physical input each frame.
    /// Movement has an expiry, so loss of speech/camera cannot leave a held key behind.
    /// </summary>
    public sealed class GameCommandController
    {
        readonly CampaignGame game;
        readonly Func<float> clock;
        readonly HashSet<string> pending=new(StringComparer.Ordinal);
        Vector2 direction;
        float movementUntil;
        bool sprint;
        public string CurrentAction { get; private set; }="Ожидание команды";
        public bool WantsCrouch { get; private set; }
        public bool WantsAim { get; private set; }
        public bool WantsSprint => Movement.sqrMagnitude>.001f && sprint;
        public Vector2 Movement => clock()<movementUntil ? direction : Vector2.zero;
        public float MovementSeconds => Mathf.Max(0,movementUntil-clock());
        public string MovementStatus => Movement.sqrMagnitude<.001f ? "Стоим" :
            (sprint ? "Бег" : "Ходьба")+" / "+DirectionTitle(direction)+" / "+MovementSeconds.ToString("0.0")+" с";

        public GameCommandController(CampaignGame game,Func<float> clock=null)
        { this.game=game; this.clock=clock ?? (()=>Time.unscaledTime); }

        public bool Execute(string action)
        {
            action=(action ?? "").Trim().ToLowerInvariant();
            if(!game || game.BriefingOpen) return false;
            if(action=="pause")
            {
                if(game.MainMenu || game.Dead || game.Victory) return false;
                game.SetPaused(true); CurrentAction="Пауза"; return true;
            }
            if(action=="resume")
            {
                if(game.MainMenu || game.Dead || game.Victory || game.TerminalOpen) return false;
                game.SetPaused(false); CurrentAction="Продолжить"; return true;
            }
            if(action=="stop") { StopMotion(); WantsAim=false; pending.Remove("fire"); CurrentAction="Стоп"; return true; }
            if(action=="check_link")
            {
                if(!game.CheckBridgeLink()) return false;
                CurrentAction=Title(action);
                return true;
            }
            if(!game.Running) { Clear(); return false; }
            switch(action)
            {
                case "pistol":
                    if(!game.DrawPistol()) return false;
                    sprint=false;
                    break;
                case "rifle":
                    if(!game.DrawRifle()) return false;
                    sprint=false;
                    break;
                case "forward": case "backward": case "left": case "right":
                    SetMovement(action,3); break;
                case "run":
                    sprint=true; WantsCrouch=WantsAim=false;
                    if(Movement.sqrMagnitude<.001f) direction=Vector2.up;
                    movementUntil=clock()+3; break;
                case "walk":
                    sprint=false; if(Movement.sqrMagnitude<.001f) direction=Vector2.up;
                    movementUntil=clock()+3; break;
                case "crouch": WantsCrouch=true; sprint=false; break;
                case "stand": WantsCrouch=false; break;
                case "aim": WantsAim=true; sprint=false; break;
                case "unaim": WantsAim=false; break;
                case "jump": case "reload": case "inspect": case "shield": case "weapon": case "switch": case "interact": case "fire":
                    if(action=="inspect" || action=="reload" || action=="fire" || action=="shield" || action=="weapon" || action=="switch")
                    { sprint=false; WantsAim=action=="inspect" ? false : WantsAim; }
                    pending.Add(action); break;
                default: return false;
            }
            CurrentAction=Title(action); return true;
        }

        // Held camera poses renew only a short lease; stopping the stream ends motion
        // within half a second. One-shot actions are deliberately not renewed here.
        public bool RenewMovement(string action,float seconds=.5f)
        {
            if(!game || !game.Running) { StopMotion(); return false; }
            action=(action ?? "").Trim().ToLowerInvariant();
            if(action!="forward" && action!="backward" && action!="left" && action!="right" && action!="run" && action!="walk") return false;
            float lease=Mathf.Clamp(seconds,.1f,.75f);
            if(action=="run" || action=="walk")
            {
                sprint=action=="run";
                if(sprint) WantsCrouch=WantsAim=false;
                if(Movement.sqrMagnitude<.001f) direction=Vector2.up;
                movementUntil=clock()+lease;
            }
            else SetMovement(action,lease);
            CurrentAction=Title(action); return true;
        }

        void SetMovement(string action,float seconds)
        {
            direction=action switch { "forward"=>Vector2.up,"backward"=>Vector2.down,"left"=>Vector2.left,_=>Vector2.right };
            movementUntil=clock()+seconds;
        }
        public void StopMotion() { direction=Vector2.zero; movementUntil=0; sprint=false; }
        public void Clear() { StopMotion(); pending.Clear(); WantsCrouch=WantsAim=false; CurrentAction="Ожидание команды"; }
        internal bool Consume(string action) => pending.Remove(action);
        static string DirectionTitle(Vector2 value) => value.y>0 ? "вперёд" : value.y<0 ? "назад" : value.x<0 ? "влево" : "вправо";
        public static string Title(string action) => action switch
        {
            "forward"=>"Вперёд", "backward"=>"Назад", "left"=>"Влево", "right"=>"Вправо", "stop"=>"Стоп",
            "run"=>"Бежать", "walk"=>"Идти", "jump"=>"Прыжок", "crouch"=>"Присесть", "stand"=>"Встать",
            "reload"=>"Перезарядка", "inspect"=>"Осмотр оружия", "shield"=>"Достать щит", "weapon"=>"Достать оружие",
            "switch"=>"Сменить оружие", "interact"=>"Взаимодействовать", "aim"=>"Прицелиться", "unaim"=>"Опустить прицел",
            "fire"=>"Выстрел", "pause"=>"Пауза", "resume"=>"Продолжить", "check_link"=>"Проверить связь",
            "pistol"=>"Достать пистолет", "rifle"=>"Достать автомат", _=>action
        };
    }
}
