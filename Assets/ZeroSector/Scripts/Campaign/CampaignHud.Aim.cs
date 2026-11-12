using UnityEngine;

namespace ZeroSector.Campaign
{
    public sealed partial class CampaignHud
    {
        static readonly Vector2 ReticleCenter = new(800, 450);
        static readonly Color ReticleOutline = new(.013f, .022f, .032f, .94f);

        enum WeaponImpact { Hit, Shield, Elimination }

        WeaponImpact weaponImpact;
        float weaponImpactUntil;

        public void RegisterWeaponImpact(bool eliminated, bool absorbed)
        {
            float now = Time.unscaledTime;
            if (!eliminated && weaponImpact == WeaponImpact.Elimination && now < weaponImpactUntil)
                return;

            weaponImpact = eliminated ? WeaponImpact.Elimination : absorbed ? WeaponImpact.Shield : WeaponImpact.Hit;
            weaponImpactUntil = now + (eliminated ? .37f : .21f);
        }

        void DrawWeaponReticle(FirstPersonPresentation view)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (game.chapter == 0 || view.Equipped == null ||
                (view.weaponPivot && !view.weaponPivot.gameObject.activeInHierarchy))
            {
                DrawInteractionReticle();
                return;
            }

            if (view.IsReloading)
            {
                DrawInteractionReticle();
                Fill(new Rect(780, 465, 40, 5), ReticleOutline);
                Fill(new Rect(781, 466, 38, 3), new Color(.38f, .5f, .57f, .62f));
                Fill(new Rect(781, 466, 38 * view.ReloadProgress, 3), cyan);
            }
            else if (view.IsSwitching || view.IsInspecting || view.IsSprinting)
            {
                DrawInteractionReticle();
            }
            else if (!view.CanFire || game.WeaponOverheated)
            {
                ReticleStroke(new Rect(798.5f, 448.5f, 3, 3), amber);
                ReticleStroke(new Rect(797, 458, 6, 2), amber);
            }
            else
            {
                float aim = view.AimAmount;
                float focalLength = game.eyes.pixelHeight * .5f * game.eyes.projectionMatrix.m11 / GUI.matrix.m11;
                float spread = focalLength * Mathf.Tan(game.AimSpreadDegrees * Mathf.Deg2Rad);
                float gap = Mathf.Max(Mathf.Lerp(7, 4, aim), spread);
                float length = Mathf.Lerp(10, 5, aim);
                float width = Mathf.Lerp(2.2f, 1.6f, aim);
                float x = ReticleCenter.x;
                float y = ReticleCenter.y;

                ReticleStroke(new Rect(x - gap - length, y - width * .5f, length, width), ink);
                ReticleStroke(new Rect(x + gap, y - width * .5f, length, width), ink);
                ReticleStroke(new Rect(x - width * .5f, y - gap - length, width, length), ink);
                ReticleStroke(new Rect(x - width * .5f, y + gap, width, length), ink);
                ReticleStroke(new Rect(x - 1.35f, y - 1.35f, 2.7f, 2.7f), Color.Lerp(ink, cyan, aim));
            }

            DrawWeaponImpact();
        }

        void DrawInteractionReticle()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            bool canInteract = !string.IsNullOrEmpty(game.Prompt);
            float size = canInteract ? 4 : 2.7f;
            ReticleStroke(new Rect(ReticleCenter.x - size * .5f, ReticleCenter.y - size * .5f, size, size),
                canInteract ? cyan : ink);
        }

        void DrawWeaponImpact()
        {
            float remaining = weaponImpactUntil - Time.unscaledTime;
            if (remaining <= 0)
                return;

            Color tint = weaponImpact switch
            {
                WeaponImpact.Shield => cyan,
                WeaponImpact.Elimination => new Color(1, .31f, .37f),
                _ => amber
            };
            tint.a = Mathf.Clamp01(remaining / .12f);
            bool absorbed = weaponImpact == WeaponImpact.Shield;
            float length = weaponImpact == WeaponImpact.Elimination ? 11 : 7;
            Matrix4x4 previous = GUI.matrix;
            Matrix4x4 pivot = Matrix4x4.Translate(ReticleCenter);
            Matrix4x4 inversePivot = Matrix4x4.Translate(-ReticleCenter);
            try
            {
                for (int side = 0; side < 4; side++)
                {
                    var rotation = Matrix4x4.Rotate(Quaternion.Euler(0, 0, 45 + side * 90));
                    GUI.matrix = previous * pivot * rotation * inversePivot;
                    Rect mark = absorbed
                        ? new Rect(ReticleCenter.x + 9, ReticleCenter.y - 3.5f, 2, 7)
                        : new Rect(ReticleCenter.x + 5, ReticleCenter.y - 1.2f, length, 2.4f);
                    ReticleStroke(mark, tint);
                }
            }
            finally { GUI.matrix = previous; }
        }

        static void ReticleStroke(Rect mark, Color tint)
        {
            Color outline = ReticleOutline;
            outline.a *= tint.a;
            Fill(new Rect(mark.x - 1, mark.y - 1, mark.width + 2, mark.height + 2), outline);
            Fill(mark, tint);
        }
    }
}
