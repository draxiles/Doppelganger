using UnityEngine;

namespace ZeroSector
{
    // Authored art references, with no third-party controller or input code.
    public sealed class WeaponArtBinding : MonoBehaviour
    {
        public int formatVersion;
        public FirstPersonPresentation.WeaponSlot specification = new();
    }
}
