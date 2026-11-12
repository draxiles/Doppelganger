using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZeroSector.Editor
{
    // Runs against isolated real arm/weapon rigs. No campaign, AI, microphone, webcam,
    // policy or save-file APIs are instantiated or called by this fixture.
    [InitializeOnLoad]
    public static class CampaignWeaponCheck
    {
        const string Key="Doppelganger.WeaponCheck", Previous="Doppelganger.WeaponCheck.Scene";
        const string Report="Diagnostics/CampaignPreviews/weapon-check.txt";
        const string Root="Isolated weapon integration check";
        internal static bool IsRunning => SessionState.GetBool(Key,false);
        internal static bool Passed => File.Exists(Report) && File.ReadAllText(Report).Contains("RESULT: PASSED");
        static FirstPersonPresentation view;
        static int phase=-1, weapon, initialMagazine, initialReserve;
        static int burstShots;
        static float firstRifleSpread;
        static float begun;
        static double waitUntil;
        static bool completed;

        static CampaignWeaponCheck()
        {
            EditorApplication.playModeStateChanged+=ModeChanged;
            if(SessionState.GetBool(Key,false)) Attach();
        }

        public static void Start()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and stop Play Mode before the weapon check.");
            string previous=SceneManager.GetActiveScene().path;
            if(string.IsNullOrEmpty(previous)) throw new InvalidOperationException("Open a saved scene before the weapon check.");
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            File.WriteAllText(Report,"Real-rig ammo/reload/switch/inspection/spread integration check. No player save/model modified.\n");
            SessionState.SetString(Previous,previous);
            // The temporary unsaved scene isolates this test from enemies, objectives and saves.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                var root=new GameObject(Root,typeof(CharacterController),typeof(FirstPersonPresentation));
                var camera=new GameObject("Test camera",typeof(Camera)).GetComponent<Camera>();
                camera.transform.SetParent(root.transform,false); camera.transform.localPosition=Vector3.up*1.65f;
                var fixture=root.GetComponent<FirstPersonPresentation>(); fixture.eyes=camera;
                fixture.body=root.GetComponent<CharacterController>();
                fixture.weaponPivot=new GameObject("Test weapon presentation").transform;
                fixture.weaponPivot.SetParent(camera.transform,false);
                foreach(string kind in new[]{"Terraformer","Robot"})
                {
                    var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DoppelgangerGenerated/Weapons/"+kind+".prefab");
                    if(!asset) throw new InvalidOperationException("Missing generated weapon: "+kind);
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(asset,fixture.weaponPivot);
                    var binding=instance.GetComponent<WeaponArtBinding>();
                    if(!binding || binding.formatVersion<3 || !binding.specification.reload || !binding.specification.secondaryReload)
                        throw new InvalidOperationException("Run FpsSampleArt.Prepare before testing reload clips: "+kind);
                    var specification=binding.specification; specification.root=instance.transform;
                    fixture.weapons.Add(specification);
                }
                SessionState.SetBool(Key,true); Attach(); EditorApplication.EnterPlaymode();
            }
            catch
            {
                SessionState.EraseBool(Key); EditorSceneManager.OpenScene(previous,OpenSceneMode.Single); throw;
            }
        }

        static void Attach()
        {
            phase=-1; weapon=0; completed=false; waitUntil=EditorApplication.timeSinceStartup+1;
            EditorApplication.update-=Tick; EditorApplication.update+=Tick;
            Application.logMessageReceived-=Log; Application.logMessageReceived+=Log;
        }
        static void Log(string message,string stack,LogType type)
        {
            if(type==LogType.Exception || type==LogType.Error || type==LogType.Assert)
                Finish(false,"Runtime error: "+message);
        }
        static void Check(bool condition,string label)
        {
            File.AppendAllText(Report,(condition ? "PASS: " : "FAIL: ")+label+"\n");
            if(!condition) throw new InvalidOperationException(label);
        }
        static void Begin(int next) { phase=next; begun=Time.time; EditorApplication.QueuePlayerLoopUpdate(); }
        static bool Elapsed(float seconds) => Time.time-begun>=seconds;

        static void Tick()
        {
            if(completed || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if(EditorApplication.isPaused) throw new InvalidOperationException("Play Mode is paused during weapon check.");
                if(phase==-1)
                {
                    if(EditorApplication.timeSinceStartup<waitUntil) return;
                    var root=GameObject.Find(Root);
                    if(!root) throw new InvalidOperationException("Isolated weapon test fixture did not enter Play Mode.");
                    view=root.GetComponent<FirstPersonPresentation>(); view.Initialize(view.body,view.eyes);
                    Time.timeScale=1;
                    view.RefillAmmo(); Begin(0);
                }
                view.SetInput(0,Vector2.zero,0,false,false,phase==11,false,true);
                switch(phase)
                {
                    case 0:
                        view.Equip(weapon); view.RefillAmmo();
                        Check(view.Equipped.reload && view.Equipped.secondaryReload,"native reload clips are connected / "+weapon);
                        initialMagazine=view.MagazineAmmo; initialReserve=view.ReserveAmmo;
                        for(int i=0;i<3;i++) Check(view.TryConsumeRound(),"real shot consumes ammunition / "+weapon);
                        Check(view.MagazineAmmo==initialMagazine-3 && view.ReserveAmmo==initialReserve,"only magazine is spent / "+weapon);
                        view.Equip(weapon);
                        Check(view.MagazineAmmo==initialMagazine-3,"immediate Equip cannot refill spent magazine / "+weapon);
                        Check(view.BeginReload() && view.IsReloading && !view.CanFire,"reload begins and blocks fire / "+weapon);
                        Begin(1); break;
                    case 1:
                        if(!Elapsed(.15f)) break;
                        Check(view.IsReloading && view.MagazineAmmo==initialMagazine-3 && view.ReserveAmmo==initialReserve,"reload cannot grant ammo before completion / "+weapon);
                        Check(!view.TryConsumeRound(),"reload rejects shots / "+weapon); Begin(2); break;
                    case 2:
                        if(view.IsReloading) { if(Elapsed(20)) throw new TimeoutException("Native reload never completed."); break; }
                        Check(view.MagazineAmmo==initialMagazine && view.ReserveAmmo==initialReserve-3,"completed reload conserves total ammunition / "+weapon);
                        Check(view.TryConsumeRound() && view.TryConsumeRound(),"two subsequent rounds consumed / "+weapon);
                        initialMagazine=view.MagazineAmmo; initialReserve=view.ReserveAmmo;
                        Check(view.BeginReload(),"partly empty magazine may reload / "+weapon);
                        Check(view.RequestEquip(1-weapon) && !view.IsReloading && view.IsSwitching,"animated switch cancels pending reload / "+weapon);
                        Check(!view.TryConsumeRound(),"switch blocks shots / "+weapon); Begin(3); break;
                    case 3:
                        if(!Elapsed(.1f)) break;
                        Check(view.equippedIndex==weapon,"old weapon remains until it is lowered / "+weapon); Begin(4); break;
                    case 4:
                        if(view.IsSwitching) { if(Elapsed(3)) throw new TimeoutException("Switch never completed."); break; }
                        Check(view.equippedIndex==1-weapon && view.weapons.Count(slot=>slot.root.gameObject.activeSelf)==1,"switch completes with exactly one visible weapon / "+weapon);
                        Check(view.RequestEquip(weapon),"switch back requested / "+weapon); Begin(5); break;
                    case 5:
                        if(view.IsSwitching) { if(Elapsed(3)) throw new TimeoutException("Return switch never completed."); break; }
                        Check(view.MagazineAmmo==initialMagazine && view.ReserveAmmo==initialReserve,"cancelled reload grants no ammunition and per-weapon ammo survives switching / "+weapon);
                        while(view.MagazineAmmo>0) Check(view.TryConsumeRound(),"spend remaining round / "+weapon);
                        Check(!view.TryConsumeRound() && !view.CanFire,"empty magazine cannot fire / "+weapon);
                        Check(view.BeginReload(),"empty magazine starts native reload / "+weapon); Begin(6); break;
                    case 6:
                        if(view.IsReloading) { if(Elapsed(20)) throw new TimeoutException("Empty reload never completed."); break; }
                        Check(view.MagazineAmmo==Mathf.Min(view.MagazineCapacity,initialReserve),"empty reload transfers exact available reserve / "+weapon);
                        Check(view.ReserveAmmo==Mathf.Max(0,initialReserve-view.MagazineCapacity),"reserve is debited once / "+weapon);
                        Check(!view.BeginReload(),"full magazine cannot duplicate reserve by reloading / "+weapon);
                        view.SetInput(0,Vector2.zero,0,false,false,false,true,true);
                        initialMagazine=view.MagazineAmmo;
                        Check(view.IsInspecting && !view.TryConsumeRound(),"F inspection blocks fire / "+weapon); Begin(7); break;
                    case 7:
                        if(!Elapsed(.8f)) break;
                        Check(view.IsInspecting && Quaternion.Angle(view.weaponPivot.localRotation,Quaternion.identity)>10,"inspection visibly moves the actual arms and weapon / "+weapon);
                        Check(view.MagazineAmmo==initialMagazine,"inspection cannot consume ammunition / "+weapon);
                        if(++weapon<2) { view.ResetView(); Begin(0); }
                        else Begin(8);
                        break;
                    case 8:
                        view.Equip(0); view.RefillAmmo(); burstShots=1;
                        Check(view.ShotSpreadDegrees==0 && view.ShotDirection()==view.eyes.transform.forward,
                            "the first shot from rest follows the exact center of the sight");
                        ShootSpreadRound(); firstRifleSpread=view.ShotSpreadDegrees;
                        Check(firstRifleSpread>.5f,"an actual rifle shot opens the cone"); Begin(9); break;
                    case 9:
                        if(!Elapsed(view.Equipped.interval)) break;
                        ShootSpreadRound();
                        if(++burstShots<5) { Begin(9); break; }
                        Check(view.ShotSpreadDegrees>firstRifleSpread*1.8f,"fire at the rifle's normal rate accumulates spread");
                        float maxAngle=0;
                        for(int i=0;i<40;i++) maxAngle=Mathf.Max(maxAngle,Vector3.Angle(view.eyes.transform.forward,view.ShotDirection()));
                        Check(maxAngle>firstRifleSpread && maxAngle<=view.ShotSpreadDegrees+.03f,
                            "sampled directions leave the center and stay inside the indicated cone");
                        Begin(10); break;
                    case 10:
                        if(!Elapsed(1.45f)) break;
                        Check(view.ShotSpreadDegrees==0 && view.ShotDirection()==view.eyes.transform.forward,
                            "a pause after a burst restores exact center accuracy");
                        Begin(11); break;
                    case 11:
                        if(!Elapsed(.4f)) break;
                        ShootSpreadRound();
                        Check(view.AimAmount>.9f && view.ShotSpreadDegrees<firstRifleSpread*.55f,
                            "aimed fire uses a smaller cone for the same round");
                        view.Equip(1); ShootSpreadRound();
                        Check(view.ShotSpreadDegrees>firstRifleSpread*1.5f,"the heavy emitter has stronger initial bloom than the rifle");
                        view.InstallCombatSidearm(); view.Equip(view.weapons.Count-1); ShootSpreadRound();
                        Check(view.Equipped.id==FirstPersonPresentation.ServicePistolId && view.ShotSpreadDegrees<firstRifleSpread,
                            "the semi-automatic P-7 has its own smaller bloom profile");
                        Finish(true,"Real weapon rigs passed reload, ammunition conservation, cancellation, switching, inspection and firing spread checks.");
                        break;
                }
            }
            catch(Exception exception) { Finish(false,exception.ToString()); }
        }

        static void ShootSpreadRound()
        {
            if(!view.TryConsumeRound()) throw new InvalidOperationException("The spread fixture could not consume its round.");
            view.OnShot(false);
        }

        static void Finish(bool passed,string detail)
        {
            if(completed) return;
            completed=true; File.AppendAllText(Report,(passed ? "RESULT: PASSED\n" : "RESULT: FAILED\n")+detail+"\n");
            EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
            if(EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        }
        static void ModeChanged(PlayModeStateChange change)
        {
            if(change!=PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key,false)) return;
            SessionState.EraseBool(Key); EditorApplication.update-=Tick; Application.logMessageReceived-=Log;
            string previous=SessionState.GetString(Previous,""); SessionState.EraseString(Previous);
            EditorApplication.delayCall+=()=>{ if(!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous,OpenSceneMode.Single); };
        }
    }
}
