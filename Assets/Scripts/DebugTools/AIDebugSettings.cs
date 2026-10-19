using System;

// Pengaturan global AI Debug Mode. Dinyalakan atau dimatikan lewat F1 (AIDebugOverlay).
public static class AIDebugSettings
{
    private static bool enabled = true;

    public static bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
                return;

            enabled = value;
            Changed?.Invoke(enabled);
        }
    }

    // Label in-game di atas kepala musuh. Mengikuti Enabled, tetapi bisa dimatikan sendiri dari Inspector overlay.
    public static bool ShowLabels => enabled && LabelsAllowed;

    public static bool LabelsAllowed = true;

    public static event Action<bool> Changed;
}
