using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSector.Campaign
{
    /// <summary>The world model shared by the first-person commander and ADAM.</summary>
    [DisallowMultipleComponent]
    public sealed class CommanderAvatar : MonoBehaviour
    {
        public const string ResourcePath = "Doppelganger/CommanderAvatar";

        public RobotPresentation Motion => GetComponent<RobotPresentation>();

        public static RobotPresentation Attach(Transform owner, bool shadowsOnly)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (!prefab || !owner) return null;

            var instance = Instantiate(prefab, owner, false);
            instance.name = shadowsOnly ? "Алекс / тень командира" : "ADAM / костюм Алекса";
            if (shadowsOnly)
            {
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    renderer.receiveShadows = false;
                }
                foreach (var light in instance.GetComponentsInChildren<Light>(true)) Destroy(light);
            }
            return instance.GetComponent<RobotPresentation>();
        }
    }
}
