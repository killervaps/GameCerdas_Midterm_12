// Tujuan satu ronde. Dipilih PCG di Safezone bersama komposisi musuh.
public enum ObjectiveType
{
    // Habisi semua musuh.
    Exterminate,
    // Bertahan sampai waktu habis.
    Survival,
    // Kuasai zona sampai progres penuh.
    HoldZone
}

// Teks objective untuk HUD, preview Safezone, dan panel debug.
public static class ObjectiveInfo
{
    public static string Label(ObjectiveType type)
    {
        switch (type)
        {
            case ObjectiveType.Survival: return "Bertahan";
            case ObjectiveType.HoldZone: return "Rebut Zona";
            default: return "Basmi";
        }
    }

    public static string Goal(ObjectiveType type)
    {
        switch (type)
        {
            case ObjectiveType.Survival: return "Bertahan sampai waktu habis";
            case ObjectiveType.HoldZone: return "Kuasai zona sampai 100%";
            default: return "Habisi semua Brainrot";
        }
    }
}
