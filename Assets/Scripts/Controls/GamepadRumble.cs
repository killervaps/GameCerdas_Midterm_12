using UnityEngine;
using UnityEngine.InputSystem;

// Getaran gamepad. Pengaturan (aktif/mati dan intensitas) disimpan di PlayerPrefs dan diatur dari menu Pengaturan.
//   Kena damage   getaran singkat lemah (damage kecil) atau sedang (damage besar)
//   Aura Burst    getaran kuat
// Getaran hanya dikirim saat pemain sedang memakai gamepad, dan berhenti saat game dijeda.
public static class GamepadRumble
{
    private const string EnabledKey = "MathGate.Pad.Vibration";
    private const string IntensityKey = "MathGate.Pad.VibrationIntensity";

    // Damage di atas fraksi HP maksimum ini dianggap besar (getaran sedang).
    private const float HeavyHitFraction = 0.1f;

    private static bool loaded;
    private static bool enabled = true;
    private static float intensity = 1f;

    private static Gamepad pulsePad;
    private static float pulseEnd;
    private static float pulseLow;
    private static float pulseHigh;
    private static bool pulseIsPreview;

    public static bool Enabled
    {
        get
        {
            Load();
            return enabled;
        }
        set
        {
            Load();
            enabled = value;
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);

            if (!value)
                Stop();
        }
    }

    // 0-1, pengali kekuatan semua getaran.
    public static float Intensity
    {
        get
        {
            Load();
            return intensity;
        }
        set
        {
            Load();
            intensity = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(IntensityKey, intensity);
        }
    }

    public static void ResetToDefault()
    {
        Enabled = true;
        Intensity = 1f;
    }

    // finalDamage / MaxHealth.
    public static void Damage(float fractionOfMaxHealth)
    {
        if (fractionOfMaxHealth <= 0f)
            return;

        if (fractionOfMaxHealth < HeavyHitFraction)
            Pulse(0.2f, 0.3f, 0.12f);
        else
            Pulse(0.5f, 0.65f, 0.2f);
    }

    public static void AuraBurst()
    {
        Pulse(1f, 1f, 0.4f);
    }

    // Ronde bersih: 2 detik kuat untuk ronde biasa, 3 detik sangat kuat untuk kemenangan di ronde terakhir.
    public static void RoundCleared(bool finalRound)
    {
        if (finalRound)
            Pulse(1f, 1f, 3f);
        else
            Pulse(0.7f, 0.8f, 2f);
    }

    // Contoh getaran di menu Pengaturan. Tetap jalan walau game dijeda.
    public static void Preview()
    {
        Pad(true, 0.5f, 0.65f, 0.25f);
    }

    public static void Pulse(float lowFrequency, float highFrequency, float duration)
    {
        if (!ControlScheme.IsGamepad)
            return;

        Pad(false, lowFrequency, highFrequency, duration);
    }

    private static void Pad(bool preview, float low, float high, float duration)
    {
        Load();

        if (!enabled || intensity <= 0f)
            return;

        Gamepad pad = ControlScheme.ActiveGamepad ?? Gamepad.current;

        if (pad == null)
            return;

        low *= intensity;
        high *= intensity;

        // Getaran yang lebih lemah tidak memotong getaran kuat yang sedang berjalan (mis. damage saat Aura Burst).
        bool running = pulsePad != null && Time.unscaledTime < pulseEnd;

        if (running && pulsePad == pad && low <= pulseLow && high <= pulseHigh)
            return;

        if (pulsePad != null && pulsePad != pad)
            pulsePad.SetMotorSpeeds(0f, 0f);

        pad.SetMotorSpeeds(low, high);
        pulsePad = pad;
        pulseLow = low;
        pulseHigh = high;
        pulseEnd = Time.unscaledTime + duration;
        pulseIsPreview = preview;
    }

    // Dipanggil ControllerRuntime setiap frame.
    internal static void Tick(bool gamePaused)
    {
        if (pulsePad == null)
            return;

        if (Time.unscaledTime >= pulseEnd || (gamePaused && !pulseIsPreview))
            Stop();
    }

    public static void Stop()
    {
        if (pulsePad != null)
            pulsePad.SetMotorSpeeds(0f, 0f);

        pulsePad = null;
        pulseLow = 0f;
        pulseHigh = 0f;
    }

    private static void Load()
    {
        if (loaded)
            return;

        loaded = true;
        enabled = PlayerPrefs.GetInt(EnabledKey, 1) == 1;
        intensity = Mathf.Clamp01(PlayerPrefs.GetFloat(IntensityKey, 1f));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = false;
        pulsePad = null;
    }
}
