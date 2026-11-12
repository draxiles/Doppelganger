using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroSector
{
    [RequireComponent(typeof(CharacterController))]
    public class BridgeEpisode : MonoBehaviour
    {
        public Camera eyes;
        public Transform terminal;
        public Transform airlock;
        public Light[] bridgeLights;
        public AudioClip ambience;
        CharacterController controller;
        CommanderCrouch crouch;
        AudioSource audioSource;
        Vector3 doorHome;
        float pitch, vertical, clock, messageUntil;
        bool captured, powered, finished, consoleOpen;
        int priority = 1;
        string transmission = "СИСТЕМА  /  Аварийное питание. Командир, восстановите связь на центральном терминале.";
        BridgeHud hud;

        void Start()
        {
            controller = GetComponent<CharacterController>();
            crouch = new CommanderCrouch(controller, eyes.transform);
            BridgeVisualQuality.Apply(eyes);
            doorHome = airlock.position;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.clip = ambience;
            audioSource.loop = true;
            audioSource.volume = .2f;
            audioSource.Play();
            hud = gameObject.AddComponent<BridgeHud>();
            hud.Initialize();
            messageUntil = 14;
            Application.targetFrameRate = 60;
        }

        void Update()
        {
            clock += Time.deltaTime;
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            if (keys == null || mouse == null) return;
            if (keys.escapeKey.wasPressedThisFrame)
            {
                consoleOpen = false;
                Capture(false);
            }
            if (!captured && mouse.leftButton.wasPressedThisFrame) Capture(true);
            if (captured)
            {
                Vector2 look = mouse.delta.ReadValue() * .085f;
                transform.Rotate(0, look.x, 0);
                pitch = Mathf.Clamp(pitch - look.y, -80, 80);
                eyes.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                if (!consoleOpen)
                {
                    crouch.Tick(CommanderCrouch.ReadInput(keys), Time.deltaTime);
                    float x = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
                    float z = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
                    Vector3 motion = transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(x, 0, z), 1));
                    motion *= crouch.IsCrouching ? 1.6f : keys.leftShiftKey.isPressed ? 5.4f : 3.2f;
                    if (controller.isGrounded && vertical < 0) vertical = -2;
                    vertical -= 18 * Time.deltaTime;
                    motion.y = vertical;
                    controller.Move(motion * Time.deltaTime);
                }
                if (keys.eKey.wasPressedThisFrame)
                {
                    if (consoleOpen) consoleOpen = false;
                    else if (LookingAtTerminal())
                    {
                        consoleOpen = true;
                        if (!powered) RestorePower();
                    }
                }
                if (consoleOpen)
                {
                    if (keys.digit1Key.wasPressedThisFrame) priority = 0;
                    if (keys.digit2Key.wasPressedThisFrame) priority = 1;
                    if (keys.digit3Key.wasPressedThisFrame) priority = 2;
                }
                if (keys.rKey.wasPressedThisFrame && transform.position.y < -3) Respawn();
            }
            if (powered)
            {
                airlock.position = Vector3.MoveTowards(airlock.position, doorHome + Vector3.up * 4.8f, Time.deltaTime * 1.3f);
                if (!finished && transform.position.z < -18)
                {
                    finished = true;
                    transmission = "НЕИЗВЕСТНЫЙ  /  Ты снова выбрал этот маршрут. Я бы поступил точно так же.";
                    messageUntil = clock + 18;
                    PlayerPrefs.SetInt("Doppelganger.BridgeComplete", 1);
                    PlayerPrefs.Save();
                }
            }
            if (transform.position.y < -15) Respawn();
            hud.Refresh(captured, powered, finished, consoleOpen, priority,
                captured && !consoleOpen && LookingAtTerminal(), clock < messageUntil ? transmission : "");
        }

        void RestorePower()
        {
            powered = true;
            foreach (var lamp in bridgeLights)
            {
                lamp.color = new Color(.38f, .72f, 1);
                lamp.intensity = 2.6f;
            }
            transmission = "СИСТЕМА  /  Питание восстановлено. Переборка разблокирована. Проверьте технический отсек за мостиком.";
            messageUntil = clock + 12;
        }

        bool LookingAtTerminal()
        {
            if (!terminal || Vector3.Distance(eyes.transform.position, terminal.position + Vector3.up * .7f) > 3.8f) return false;
            Vector3 direction = terminal.position + Vector3.up * .7f - eyes.transform.position;
            return Vector3.Dot(eyes.transform.forward, direction.normalized) > .7f;
        }

        void Respawn()
        {
            controller.enabled = false;
            transform.position = new Vector3(0, .1f, -5);
            controller.enabled = true;
            vertical = 0;
        }

        void Capture(bool value)
        {
            captured = value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }

        void OnDisable() { Capture(false); }

    }
}
