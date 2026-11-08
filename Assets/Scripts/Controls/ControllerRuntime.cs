using UnityEngine;
using UnityEngine.InputSystem;

// Objek tersembunyi (DontDestroyOnLoad) yang dibuat otomatis saat Play. Tugasnya:
//   - mendeteksi skema kontrol aktif dari input terakhir (keyboard/mouse atau gamepad)
//   - memuat binding gamepad tersimpan (ControllerBindings)
//   - mematikan getaran tepat waktu dan saat game dijeda (GamepadRumble)
[DefaultExecutionOrder(-100)]
public class ControllerRuntime : MonoBehaviour
{
    // Ambang supaya stik yang sedikit drift atau mouse yang tersenggol tidak membuat skema berganti-ganti.
    private const float StickThreshold = 0.35f;
    private const float TriggerThreshold = 0.35f;
    private const float MouseMoveThreshold = 4f;

    private static ControllerRuntime instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject go = new GameObject("ControllerRuntime");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ControllerRuntime>();
    }

    private void Awake()
    {
        ControllerBindings.EnsureLoaded();
        KeyboardBindings.EnsureLoaded();
    }

    private void OnEnable()
    {
        InputSystem.onDeviceChange += HandleDeviceChange;

        // Controller DirectInput (joystick HID) dipetakan ke Gamepad virtual supaya binding <Gamepad> berlaku.
        JoystickGamepadBridge.Enable();
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= HandleDeviceChange;
        JoystickGamepadBridge.Disable();
        GamepadRumble.Stop();
        InputSystem.ResetHaptics();
    }

    private void OnApplicationQuit()
    {
        GamepadRumble.Stop();
        InputSystem.ResetHaptics();
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus)
            GamepadRumble.Stop();
    }

    private void Update()
    {
        DetectScheme();
        ControllerBindings.ApplyToUIModule();

        bool paused = GameUI.Current != null && GameUI.Current.IsPaused;
        GamepadRumble.Tick(paused);
    }

    private static void DetectScheme()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
        {
            ControlScheme.Set(ControlSchemeKind.KeyboardMouse, null);
            return;
        }

        Mouse mouse = Mouse.current;

        if (mouse != null &&
            (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame ||
             mouse.delta.ReadValue().sqrMagnitude > MouseMoveThreshold * MouseMoveThreshold ||
             mouse.scroll.ReadValue().sqrMagnitude > 0.01f))
        {
            ControlScheme.Set(ControlSchemeKind.KeyboardMouse, null);
            return;
        }

        var pads = Gamepad.all;

        for (int i = 0; i < pads.Count; i++)
        {
            if (IsActive(pads[i]))
            {
                ControlScheme.Set(ControlSchemeKind.Gamepad, pads[i]);
                return;
            }
        }
    }

    private static bool IsActive(Gamepad pad)
    {
        return pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed || pad.buttonNorth.isPressed ||
               pad.leftShoulder.isPressed || pad.rightShoulder.isPressed ||
               pad.startButton.isPressed || pad.selectButton.isPressed ||
               pad.leftStickButton.isPressed || pad.rightStickButton.isPressed ||
               pad.leftTrigger.ReadValue() > TriggerThreshold || pad.rightTrigger.ReadValue() > TriggerThreshold ||
               pad.leftStick.ReadValue().magnitude > StickThreshold || pad.rightStick.ReadValue().magnitude > StickThreshold ||
               pad.dpad.ReadValue().sqrMagnitude > 0.25f;
    }

    private static void HandleDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is Gamepad pad && (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected))
            ControlScheme.HandleGamepadRemoved(pad);
    }
}
