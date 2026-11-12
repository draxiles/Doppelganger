using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroSector.Learning;

namespace ZeroSector.Editor
{
    // Integration check in Play Mode, using the game's presentation API and real rigs.
    // Does not train, load or save the player's learned policy.
    [InitializeOnLoad]
    public static class CampaignPresentationCheck
    {
        const string Key="Doppelganger.PresentationCheck";
        const string Folder="Diagnostics/CampaignPreviews";
        static FirstPersonPresentation view;
        static int step=-1;
        static double after;
        static bool failed;
        static Vector3[] idleBones;
        static Quaternion[] idleRotations;
        static float begun;

        static CampaignPresentationCheck()
        {
            if(SessionState.GetBool(Key,false))
            {
                after=EditorApplication.timeSinceStartup+2;
                EditorApplication.update+=Tick;
                Application.logMessageReceived+=Log;
            }
        }

        public static void Start()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Presentation check needs a saved scene in Edit Mode.");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder+"/presentation-check.txt","Play Mode check: two real weapon rigs, animation, aim, sprint, inspect and fire.\n");
            SessionState.SetBool(Key,true);
            step=-1; failed=false; after=EditorApplication.timeSinceStartup+2;
            EditorApplication.update-=Tick; EditorApplication.update+=Tick;
            Application.logMessageReceived-=Log; Application.logMessageReceived+=Log;
            EditorApplication.EnterPlaymode();
        }

        static void Log(string message,string stack,LogType type)
        {
            if(type!=LogType.Error && type!=LogType.Exception && type!=LogType.Assert) return;
            failed=true; File.AppendAllText(Folder+"/presentation-check.txt","RUNTIME ERROR: "+message+"\n");
        }
        static void Require(bool condition,string description)
        {
            File.AppendAllText(Folder+"/presentation-check.txt",(condition ? "PASS: " : "FAIL: ")+description+"\n");
            if(!condition) failed=true;
        }
        static void Tick()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if(!EditorApplication.isPlaying)
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.EraseBool(Key); EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
                return;
            }
            try
            {
                if(EditorApplication.isPaused) throw new InvalidOperationException("Play Mode paused during the check.");
                if(step==-1)
                {
                    if(EditorApplication.timeSinceStartup<after) return;
                    var arena=UnityEngine.Object.FindAnyObjectByType<ImitationArena>();
                    if(!arena || !arena.presentation || !arena.presentation.eyeHeight) throw new InvalidOperationException("Arena did not initialize.");
                    arena.enabled=false; view=arena.presentation;
                    Require(view.weapons.Count==2,"two distinct equipped weapon slots");
                    step=0; Begin(); return;
                }
                int action=step%5;
                view.SetInput(0,Vector2.zero,action==1 ? 1 : 0,action==1,false,action==2,false,true);
                if(Time.time-begun < (action==4 ? .12f : .65f)) return;
                Verify();
                if(++step<10) { Begin(); return; }
                File.AppendAllText(Folder+"/presentation-check.txt",failed ? "RESULT: FAILED\n" : "RESULT: PASSED\n");
                SessionState.EraseBool(Key); EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
                EditorApplication.ExitPlaymode();
            }
            catch(Exception exception)
            {
                File.AppendAllText(Folder+"/presentation-check.txt","RESULT: FAILED\n"+exception+"\n");
                SessionState.EraseBool(Key); EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
                EditorApplication.ExitPlaymode();
            }
        }

        static void Begin()
        {
            int action=step%5;
            if(action==0) view.Equip(step/5);
            view.SetInput(0,Vector2.zero,action==1 ? 1 : 0,action==1,false,action==2,action==3,true);
            if(action==0)
            {
                var bones=view.weaponAnimator.GetComponentsInChildren<Transform>();
                idleBones=bones.Select(t=>t.localPosition).ToArray();
                idleRotations=bones.Select(t=>t.localRotation).ToArray();
            }
            if(action==4)
            {
                view.ResetView(); view.OnShot(true);
                Require(view.ShotPulse==1 && view.HitPulse==1,"fire and confirmed-hit presentation impulses / "+view.WeaponTitle);
            }
            after=EditorApplication.timeSinceStartup+(action==4 ? .12 : .7);
            begun=Time.time;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static void Verify()
        {
            int action=step%5;
            string label=view.WeaponTitle+" / "+new[]{"idle","sprint","aim","inspect","fire"}[action];
            Require(view.HasWeapon && view.weaponModel.gameObject.activeInHierarchy,"visible equipped rig / "+label);
            Require(view.weapons.Count(item=>item.root.gameObject.activeSelf)==1,"only one weapon active / "+label);
            Require(view.weaponModel.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m && m.shader && m.shader.isSupported)),"supported materials / "+label);
            if(action==1)
            {
                Require(view.IsSprinting && !view.CanFire,"sprint blocks fire / "+label);
                var bones=view.weaponAnimator.GetComponentsInChildren<Transform>();
                Require(bones.Where((t,i)=>i<idleBones.Length && ((t.localPosition-idleBones[i]).sqrMagnitude>.000001f || Quaternion.Angle(t.localRotation,idleRotations[i])>.1f)).Any(),"real rig animation changes pose / "+label);
            }
            if(action==2) Require(view.AimAmount>.9f && view.eyes.fieldOfView<58,"aim blends camera FOV / "+label);
            if(action==3) Require(view.IsInspecting && !view.CanFire && Quaternion.Angle(view.weaponPivot.localRotation,Quaternion.identity)>10,"inspection rotates actual weapon and blocks fire / "+label);
            CreepyDeckBuilder.Capture(SceneManager.GetActiveScene(),view.eyes,Folder+"/weapon-"+(step/5)+"-"+new[]{"idle","sprint","aim","inspect","fire"}[action]+".png");
        }
    }
}
