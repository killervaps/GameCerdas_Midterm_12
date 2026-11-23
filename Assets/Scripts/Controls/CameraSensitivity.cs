using UnityEngine;

// Sensitivitas putar kamera orang ketiga, terpisah untuk mouse dan stik kanan gamepad.
// Disimpan di PlayerPrefs dan diatur dari menu Pengaturan (tab Keyboard untuk mouse, tab Kontroler untuk stik).
// Nilai adalah pengali: 1 = bawaan, 0,2 = sangat lambat, 2,2 = sangat cepat.
public static class CameraSensitivity
{
    public const float Min = 0.2f;
    public const float Max = 2.2f;
    public const float Default = 1f;

    private const string MouseKey = "MathGate.Camera.MouseSensitivity";
    private const string StickKey = "MathGate.Camera.StickSensitivity";

    private static bool loaded;
    private static float mouse = Default;
    private static float stick = Default;

    public static float Mouse
    {
        get
        {
            Load();
            return mouse;
        }
        set
        {
            Load();
            mouse = Mathf.Clamp(value, Min, Max);
            PlayerPrefs.SetFloat(MouseKey, mouse);
        }
    }

    public static float Stick
    {
        get
        {
            Load();
            return stick;
        }
        set
        {
            Load();
            stick = Mathf.Clamp(value, Min, Max);
            PlayerPrefs.SetFloat(StickKey, stick);
        }
    }

    public static void ResetMouse()
    {
        Mouse = Default;
    }

    public static void ResetStick()
    {
        Stick = Default;
    }

    // Teks untuk menu, mis. "1.0x".
    public static string Format(float value)
    {
        return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x";
    }

    private static void Load()
    {
        if (loaded)
            return;

        loaded = true;
        mouse = Mathf.Clamp(PlayerPrefs.GetFloat(MouseKey, Default), Min, Max);
        stick = Mathf.Clamp(PlayerPrefs.GetFloat(StickKey, Default), Min, Max);
    }
}
