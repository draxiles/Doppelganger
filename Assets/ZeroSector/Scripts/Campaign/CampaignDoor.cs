using UnityEngine;

namespace ZeroSector.Campaign
{
    public sealed class CampaignDoor : MonoBehaviour
    {
        public string unlockId;
        public Vector3 openOffset = new(0, 3.5f, 0);

        Vector3 closedPosition;
        bool opening;

        void Awake() => closedPosition = transform.localPosition;

        public void Open() => opening = true;

        public void RestoreOpen()
        {
            opening = false;
            transform.localPosition = closedPosition + openOffset;
        }

        void Update()
        {
            if (!opening) return;
            Vector3 target = closedPosition + openOffset;
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, target, Time.deltaTime * 2);
            opening = transform.localPosition != target;
        }
    }
}
