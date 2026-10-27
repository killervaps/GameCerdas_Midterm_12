using UnityEngine;

// Tingkat kelangkaan gate, dari yang paling sering muncul sampai paling langka.
// Nilai disimpan sebagai angka di aset gate, jadi rarity baru ditambahkan di akhir.
public enum GateRarity
{
    Common,
    Rare,
    VeryRare,
    Epic,
    Legendary,
    Sigma
}

// Label dan warna rarity, dipakai kartu gate di Safezone, modal info, HUD, dan menu jeda.
// SIGMA tidak punya satu warna tetap. Warnanya berputar seperti lampu RGB gaming (SigmaColor).
public static class GateRarityStyle
{
    public const int Count = 6;

    // Id link TextMeshPro untuk teks berwarna pelangi, dianimasikan TMPRainbowLinks.
    public const string RainbowLinkId = "rgb";

    // Putaran warna per detik dan selisih warna antar huruf.
    private const float HueSpeed = 0.35f;
    private const float HueSpread = 0.08f;

    public static string Label(GateRarity rarity)
    {
        switch (rarity)
        {
            case GateRarity.Rare: return "Langka";
            case GateRarity.VeryRare: return "Sangat Langka";
            case GateRarity.Epic: return "Epik";
            case GateRarity.Legendary: return "Legendaris";
            case GateRarity.Sigma: return "SIGMA";
            default: return "Biasa";
        }
    }

    // Warna statis. Untuk SIGMA ini warna saat ini dari putaran RGB.
    public static Color Color(GateRarity rarity)
    {
        switch (rarity)
        {
            case GateRarity.Rare: return new Color(0.36f, 0.85f, 0.42f);
            case GateRarity.VeryRare: return new Color(0.31f, 0.64f, 1f);
            case GateRarity.Epic: return new Color(0.78f, 0.49f, 1f);
            case GateRarity.Legendary: return new Color(1f, 0.68f, 0.2f);
            case GateRarity.Sigma: return SigmaColor();
            default: return new Color(0.69f, 0.72f, 0.77f);
        }
    }

    public static bool IsAnimated(GateRarity rarity)
    {
        return rarity == GateRarity.Sigma;
    }

    // Warna RGB yang berputar terus. Offset menggeser fase, mis. per huruf supaya membentuk gelombang pelangi.
    public static Color SigmaColor(float offset = 0f)
    {
        float hue = Mathf.Repeat(Time.unscaledTime * HueSpeed + offset, 1f);
        return UnityEngine.Color.HSVToRGB(hue, 0.75f, 1f);
    }

    public static float LetterOffset(int index)
    {
        return index * HueSpread;
    }

    // Label rarity dalam rich text TMP. SIGMA dibungkus link supaya hurufnya dianimasikan pelangi.
    public static string RichLabel(GateRarity rarity)
    {
        if (IsAnimated(rarity))
            return "<b><link=\"" + RainbowLinkId + "\">" + Label(rarity) + "</link></b>";

        return "<color=#" + ColorUtility.ToHtmlStringRGB(Color(rarity)) + ">" + Label(rarity) + "</color>";
    }

    // Membungkus teks bebas (mis. nama gate) dengan warna rarity atau link pelangi.
    public static string Wrap(GateRarity rarity, string text)
    {
        if (IsAnimated(rarity))
            return "<link=\"" + RainbowLinkId + "\">" + text + "</link>";

        return "<color=#" + ColorUtility.ToHtmlStringRGB(Color(rarity)) + ">" + text + "</color>";
    }
}
