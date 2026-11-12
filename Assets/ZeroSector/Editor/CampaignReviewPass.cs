using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZeroSector.Editor
{
    public static class CampaignReviewPass
    {
        const string Request="Temp/DoppelgangerChecks/review-campaign.request";
        static int stage;
        static double renderAfter;
        static string original;
        public static bool IsRunning { get; private set; }
        static readonly string[] Names={"01_CommandBridge","02_Engineering","03_DoppelgangerCore"};

        [InitializeOnLoadMethod]
        static void Pending()
        { if(File.Exists(Request)) EditorApplication.delayCall+=Start; }

        public static void Start()
        {
            if(IsRunning) return;
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            if(SceneManager.GetActiveScene().isDirty) { Debug.LogWarning("Сохрани сцену перед созданием превью."); return; }
            if(File.Exists(Request)) File.Delete(Request);
            original=SceneManager.GetActiveScene().path; stage=0; IsRunning=true;
            Open(); EditorApplication.update+=Tick;
        }
        static void Open()
        {
            EditorSceneManager.OpenScene("Assets/DoppelgangerGenerated/Campaign/"+Names[stage]+".unity",OpenSceneMode.Single);
            renderAfter=EditorApplication.timeSinceStartup+1.2;
            EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll();
        }
        static void Tick()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.update-=Tick; IsRunning=false; return; }
            if(EditorApplication.timeSinceStartup<renderAfter || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var scene=SceneManager.GetActiveScene();
            CreepyDeckBuilder.Capture(scene,Camera.main,"Diagnostics/CampaignPreviews/"+Names[stage]+"-review.png");
            if(stage==0) EarthViews(scene);
            if(++stage<Names.Length) { Open(); return; }
            EditorApplication.update-=Tick;
            IsRunning=false;
            if(File.Exists(original)) EditorSceneManager.OpenScene(original,OpenSceneMode.Single);
            Debug.Log("Три сцены отсняты отдельно, после обновления рендера. Diagnostics/CampaignPreviews/*-review.png");
        }

        static void EarthViews(Scene scene)
        {
            var camera=Camera.main;
            var position=camera.transform.position; var rotation=camera.transform.rotation; float fov=camera.fieldOfView;
            var view=UnityEngine.Object.FindAnyObjectByType<FirstPersonPresentation>();
            bool visible=view.weaponPivot.gameObject.activeSelf; view.weaponPivot.gameObject.SetActive(false);
            try
            {
                camera.transform.position=new Vector3(0,2,8.2f); camera.transform.rotation=Quaternion.identity;
                CreepyDeckBuilder.Capture(scene,camera,"Diagnostics/CampaignPreviews/earth-through-window.png");
                camera.transform.position=new Vector3(0,200,180);
                camera.transform.LookAt(new Vector3(-28,-130,240)); camera.fieldOfView=60;
                CreepyDeckBuilder.Capture(scene,camera,"Diagnostics/CampaignPreviews/earth-orbit-review.png");
            }
            finally
            {
                camera.transform.position=position; camera.transform.rotation=rotation; camera.fieldOfView=fov;
                view.weaponPivot.gameObject.SetActive(visible);
            }
        }
    }
}
