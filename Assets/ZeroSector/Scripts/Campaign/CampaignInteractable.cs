using UnityEngine;

namespace ZeroSector.Campaign
{
    public sealed class CampaignInteractable : MonoBehaviour
    {
        public string id,title;
        public float range=3.2f;
        public Vector3 FocusPoint => transform.position;
    }
}
