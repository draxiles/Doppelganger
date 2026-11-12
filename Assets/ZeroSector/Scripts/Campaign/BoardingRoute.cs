using UnityEngine;

namespace ZeroSector.Campaign
{
    public sealed class BoardingRoute : MonoBehaviour
    {
        public const string ResourcePath = "Doppelganger/BoardingRoute";
        public const string SealId = "boardingSeal";
        public const string ManifestId = "boardingManifest";
        public const string TransitId = "boardingTransit";
        static readonly Vector3 WorldOrigin = new(55, 0, 0);

        public Transform entry;
        public Transform[] boarderSpawns;
        public TextMesh pressureDisplay, sealConsoleDisplay, cargoDisplay, liftDisplay;
        public Light pressureLight;
        public GameObject breachSparks;

        public Transform Entry => entry;
        public Transform[] BoarderSpawns => boarderSpawns;

        public static BoardingRoute Create(Transform owner)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            return prefab ? Instantiate(prefab, WorldOrigin, Quaternion.identity, owner).GetComponent<BoardingRoute>() : null;
        }

        void Start() => Refresh();

        public void Refresh()
        {
            bool sealedLock = CampaignSession.Has(SealId);
            bool capturedManifest = CampaignSession.Has(ManifestId);
            if (pressureDisplay)
                WriteDisplay(pressureDisplay, sealedLock ? "КОНТУР ГЕРМЕТИЧЕН" : "РАЗГЕРМЕТИЗАЦИЯ", sealedLock);
            if (sealConsoleDisplay)
                WriteDisplay(sealConsoleDisplay, sealedLock ? "ЗАТВОР ОТКРЫТ" : "ТРЕБУЕТСЯ ГЕРМЕТИЗАЦИЯ", sealedLock);
            if (cargoDisplay)
                WriteDisplay(cargoDisplay, capturedManifest ? "ЖУРНАЛ АБОРДАЖА СКОПИРОВАН" : "ИСТОЧНИК ТАРАНА НЕИЗВЕСТЕН", capturedManifest);
            if (liftDisplay)
                WriteDisplay(liftDisplay, capturedManifest ? "МАРШРУТ В ИНЖЕНЕРНЫЙ ОТСЕК" : "ДОСТУП ЧЕРЕЗ ТЕРМИНАЛ C–04", capturedManifest);
            if (pressureLight)
                pressureLight.color = sealedLock ? new Color(.28f, .82f, .59f) : new Color(1, .31f, .055f);
            if (breachSparks)
                breachSparks.SetActive(!sealedLock);
        }

        static void WriteDisplay(TextMesh display, string message, bool ready)
        {
            if (display.font) display.font.RequestCharactersInTexture(message, display.fontSize, display.fontStyle);
            display.text = message;
            display.color = ready ? new Color(.46f, 1, .75f) : new Color(1, .69f, .36f);
        }
    }
}
