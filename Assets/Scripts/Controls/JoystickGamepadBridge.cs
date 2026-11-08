using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

// Controller DirectInput (mis. Logitech F310/F710 di mode "D", atau gamepad generik) dibaca Unity sebagai
// Joystick HID, bukan Gamepad. Akibatnya semua binding <Gamepad> (RT, Menu, A di menu, dst.) tidak jalan.
// Kelas ini membuat satu Gamepad virtual per joystick seperti itu, lalu menyalin state joystick ke sana setiap update
// dengan tata letak tombol ala Xbox. Seluruh game cukup memakai binding <Gamepad>.
//
// Pemetaan (urutan tombol Logitech Dual Action / F310 mode D):
//   1 X   2 A   3 B   4 Y   5 LB   6 RB   7 LT   8 RT   9 View   10 Menu   11 LS   12 RS
//   stick = stik kiri, z/rz = stik kanan, hat = D-pad
// Getaran tidak didukung di mode DirectInput. Untuk getaran, geser saklar di belakang controller Logitech ke "X".
public static class JoystickGamepadBridge
{
    private class Link
    {
        public Joystick source;
        public Gamepad target;
        public ButtonControl[] buttons;
        public AxisControl rightX;
        public AxisControl rightY;
        public DpadControl hat;
        public GamepadState last;
        public bool hasLast;
    }

    private static readonly GamepadButton[] ButtonMap =
    {
        GamepadButton.West,          // 1
        GamepadButton.South,         // 2
        GamepadButton.East,          // 3
        GamepadButton.North,         // 4
        GamepadButton.LeftShoulder,  // 5
        GamepadButton.RightShoulder, // 6
        GamepadButton.LeftTrigger,   // 7
        GamepadButton.RightTrigger,  // 8
        GamepadButton.Select,        // 9
        GamepadButton.Start,         // 10
        GamepadButton.LeftStick,     // 11
        GamepadButton.RightStick     // 12
    };

    private static readonly List<Link> links = new List<Link>();
    private static bool hooked;

    public static bool IsVirtual(InputDevice device)
    {
        return Find(device) != null;
    }

    // Nama joystick asli di balik Gamepad virtual, atau null.
    public static string SourceName(InputDevice device)
    {
        Link link = Find(device);
        return link != null ? link.source.displayName : null;
    }

    public static void Enable()
    {
        if (hooked)
            return;

        hooked = true;
        InputSystem.onDeviceChange += HandleDeviceChange;
        InputSystem.onBeforeUpdate += Feed;

        foreach (InputDevice device in InputSystem.devices)
            TryAdd(device);
    }

    public static void Disable()
    {
        if (!hooked)
            return;

        hooked = false;
        InputSystem.onDeviceChange -= HandleDeviceChange;
        InputSystem.onBeforeUpdate -= Feed;

        foreach (Link link in links)
        {
            if (link.target != null && link.target.added)
                InputSystem.RemoveDevice(link.target);
        }

        links.Clear();
    }

    private static Link Find(InputDevice device)
    {
        foreach (Link link in links)
        {
            if (link.target == device)
                return link;
        }

        return null;
    }

    private static void HandleDeviceChange(InputDevice device, InputDeviceChange change)
    {
        switch (change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Reconnected:
                TryAdd(device);
                break;
            case InputDeviceChange.Removed:
            case InputDeviceChange.Disconnected:
                Remove(device);
                break;
        }
    }

    private static void TryAdd(InputDevice device)
    {
        if (!(device is Joystick joystick) || device is Gamepad || IsXInputHid(device))
            return;

        foreach (Link existing in links)
        {
            if (existing.source == joystick)
                return;
        }

        Link link = new Link
        {
            source = joystick,
            buttons = new ButtonControl[ButtonMap.Length],
            rightX = joystick.TryGetChildControl<AxisControl>("z"),
            rightY = joystick.TryGetChildControl<AxisControl>("rz"),
            hat = joystick.TryGetChildControl<DpadControl>("hat")
        };

        for (int i = 0; i < ButtonMap.Length; i++)
            link.buttons[i] = joystick.TryGetChildControl<ButtonControl>(i == 0 ? "trigger" : "button" + (i + 1));

        link.target = InputSystem.AddDevice<Gamepad>(string.IsNullOrEmpty(joystick.displayName) ? "Gamepad (DirectInput)" : joystick.displayName);
        links.Add(link);

        Debug.Log("JoystickGamepadBridge: '" + joystick.displayName + "' (" + joystick.layout +
                  ") dipetakan ke Gamepad virtual dengan tata letak Xbox.");
    }

    // Controller XInput (mis. GameSir atau Logitech mode X) juga punya antarmuka HID dengan "IG_" di path-nya.
    // Controller itu sudah tampil sebagai Gamepad asli (XInputController), jadi jangan dipetakan dua kali.
    private static bool IsXInputHid(InputDevice device)
    {
        string caps = device.description.capabilities ?? string.Empty;
        string serial = device.description.serial ?? string.Empty;
        string product = device.description.product ?? string.Empty;

        return caps.IndexOf("IG_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               serial.IndexOf("IG_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               product.IndexOf("Xbox", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               product.IndexOf("XInput", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void Remove(InputDevice device)
    {
        for (int i = links.Count - 1; i >= 0; i--)
        {
            if (links[i].source != device)
                continue;

            if (links[i].target != null && links[i].target.added)
                InputSystem.RemoveDevice(links[i].target);

            links.RemoveAt(i);
        }
    }

    // Dipanggil sebelum Input System memproses event, jadi state yang di-queue di sini ikut diproses di update yang sama.
    private static void Feed()
    {
        for (int i = 0; i < links.Count; i++)
        {
            Link link = links[i];

            if (link.source == null || !link.source.added || link.target == null || !link.target.added)
                continue;

            GamepadState state = new GamepadState();

            for (int b = 0; b < link.buttons.Length; b++)
            {
                if (link.buttons[b] != null && link.buttons[b].isPressed)
                    state = state.WithButton(ButtonMap[b]);
            }

            // Trigger di mode D hanya tombol digital.
            state.leftTrigger = link.buttons[6] != null && link.buttons[6].isPressed ? 1f : 0f;
            state.rightTrigger = link.buttons[7] != null && link.buttons[7].isPressed ? 1f : 0f;

            state.leftStick = link.source.stick != null ? link.source.stick.ReadValue() : Vector2.zero;
            state.rightStick = new Vector2(
                link.rightX != null ? link.rightX.ReadValue() : 0f,
                link.rightY != null ? -link.rightY.ReadValue() : 0f);

            if (link.hat != null)
            {
                if (link.hat.up.isPressed) state = state.WithButton(GamepadButton.DpadUp);
                if (link.hat.down.isPressed) state = state.WithButton(GamepadButton.DpadDown);
                if (link.hat.left.isPressed) state = state.WithButton(GamepadButton.DpadLeft);
                if (link.hat.right.isPressed) state = state.WithButton(GamepadButton.DpadRight);
            }

            if (link.hasLast && Same(link.last, state))
                continue;

            link.last = state;
            link.hasLast = true;
            InputSystem.QueueStateEvent(link.target, state);
        }
    }

    private static bool Same(GamepadState a, GamepadState b)
    {
        return a.buttons == b.buttons &&
               a.leftStick == b.leftStick && a.rightStick == b.rightStick &&
               Mathf.Approximately(a.leftTrigger, b.leftTrigger) && Mathf.Approximately(a.rightTrigger, b.rightTrigger);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        links.Clear();
        hooked = false;
    }
}
