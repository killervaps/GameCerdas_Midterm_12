using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum ControlSchemeKind
{
    KeyboardMouse,
    Gamepad
}

// Skema kontrol yang sedang dipakai pemain, dideteksi otomatis dari input terakhir (lihat ControllerRuntime).
// Semua petunjuk tombol di UI (ControlHints) ikut berganti saat skema berubah, termasuk di tengah permainan.
public static class ControlScheme
{
    public static ControlSchemeKind Current { get; private set; } = ControlSchemeKind.KeyboardMouse;

    public static bool IsGamepad => Current == ControlSchemeKind.Gamepad;

    // Gamepad yang terakhir dipakai. Getaran dikirim ke gamepad ini.
    public static Gamepad ActiveGamepad { get; private set; }

    public static event Action Changed;

    internal static void Set(ControlSchemeKind kind, Gamepad pad)
    {
        if (pad != null)
            ActiveGamepad = pad;

        if (Current == kind)
            return;

        Current = kind;

        if (kind != ControlSchemeKind.Gamepad)
            GamepadRumble.Stop();

        Changed?.Invoke();
    }

    internal static void HandleGamepadRemoved(Gamepad pad)
    {
        if (pad != ActiveGamepad)
            return;

        ActiveGamepad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null;

        if (ActiveGamepad == null)
            Set(ControlSchemeKind.KeyboardMouse, null);
    }
}
