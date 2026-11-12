using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroSector
{
    // Shared stance for the bridge and learning arena. Resizing keeps the feet in place.
    public sealed class CommanderCrouch
    {
        readonly CharacterController controller;
        readonly Transform camera;
        readonly float standingHeight, standingEye, crouchingHeight;
        readonly Vector3 standingCenter;
        readonly Collider[] overlaps = new Collider[32];
        public bool IsCrouching { get; private set; }

        public static bool ReadInput(Keyboard keys)
        {
#if UNITY_EDITOR
            // Ctrl+W/S/D are Unity shortcuts; C is safe while testing in the editor.
            return keys.cKey.isPressed;
#else
            return keys.cKey.isPressed || keys.leftCtrlKey.isPressed;
#endif
        }

        public CommanderCrouch(CharacterController controller, Transform camera,float minimumHeight=1.05f)
        {
            this.controller = controller;
            this.camera = camera;
            standingHeight = controller.height;
            standingCenter = controller.center;
            standingEye = camera ? camera.localPosition.y : 0;
            crouchingHeight = Mathf.Min(standingHeight, Mathf.Max(minimumHeight, controller.radius * 2 + .02f));
        }

        public void Tick(bool held, float deltaTime)
        {
            bool crouch = held || (IsCrouching && !HasStandingRoom());
            if (crouch != IsCrouching)
            {
                IsCrouching = crouch;
                controller.height = crouch ? crouchingHeight : standingHeight;
                controller.center = standingCenter + Vector3.up * ((controller.height - standingHeight) * .5f);
            }
            if(!camera) return;
            Vector3 eye = camera.localPosition;
            float target = standingEye - (IsCrouching ? standingHeight - crouchingHeight : 0);
            eye.y = Mathf.Lerp(eye.y, target, 1 - Mathf.Exp(-14 * deltaTime));
            camera.localPosition = eye;
        }

        public void ResetAtSafeSpawn()
        {
            IsCrouching = false;
            controller.height = standingHeight;
            controller.center = standingCenter;
            if(camera)
            {
                Vector3 eye = camera.localPosition; eye.y = standingEye;
                camera.localPosition = eye;
            }
        }

        bool HasStandingRoom()
        {
            Vector3 up = controller.transform.up;
            Vector3 center = controller.transform.TransformPoint(standingCenter);
            float radius = Mathf.Max(.05f, controller.radius - .025f);
            float halfSegment = standingHeight * .5f - controller.radius;
            int count = Physics.OverlapCapsuleNonAlloc(center - up * halfSegment, center + up * halfSegment,
                radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (overlaps[i] && overlaps[i] != controller && !overlaps[i].transform.IsChildOf(controller.transform))
                    return false;
            return true;
        }
    }
}
