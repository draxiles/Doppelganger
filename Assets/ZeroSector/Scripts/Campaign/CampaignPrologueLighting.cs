using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ZeroSector.Campaign
{
    public sealed partial class CampaignPrologue
    {
        const float ExteriorShadowDistance=126;
        UniversalRenderPipelineAsset orbitPipeline;
        float previousShadowDistance;
        Light previousSun;
        Scene lightingScene;
        bool orbitalLighting;

        void EnableOrbitLighting()
        {
            if(orbitalLighting || !orbitalSun) return;
            orbitalLighting=true;
            lightingScene=SceneManager.GetActiveScene();
            previousSun=RenderSettings.sun;
            RenderSettings.sun=orbitalSun;
            orbitPipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(!orbitPipeline) return;
            previousShadowDistance=orbitPipeline.shadowDistance;
            orbitPipeline.shadowDistance=Mathf.Max(previousShadowDistance,ExteriorShadowDistance);
        }

        void RestoreOrbitLighting()
        {
            if(!orbitalLighting) return;
            orbitalLighting=false;
            if(orbitPipeline) orbitPipeline.shadowDistance=previousShadowDistance;
            orbitPipeline=null;
            if(SceneManager.GetActiveScene()==lightingScene) RenderSettings.sun=previousSun;
            previousSun=null;
        }
    }
}
