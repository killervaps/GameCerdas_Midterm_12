using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Teks petunjuk tombol yang mengikuti skema kontrol aktif.
//   Keyboard/mouse  "Aura Burst [Space]"
//   Gamepad         "Aura Burst (Y)" dengan huruf berwarna ala Xbox: A hijau, B merah, X biru, Y kuning.
// Tombol gamepad dibaca dari binding saat ini, jadi petunjuk ikut berubah setelah rebind.
public static class ControlHints
{
    public const string PadAHex = "#6CC24A";
    public const string PadBHex = "#E8434B";
    public const string PadXHex = "#3E8EEA";
    public const string PadYHex = "#F4C21B";
    public const string PadNeutralHex = "#ECEEF3";

    public static readonly Color PadA = Hex(PadAHex);
    public static readonly Color PadB = Hex(PadBHex);
    public static readonly Color PadX = Hex(PadXHex);
    public static readonly Color PadY = Hex(PadYHex);

    private static readonly Color CapNeutral = new Color(0.16f, 0.18f, 0.24f, 1f);
    private static readonly Color CapKeyboardText = new Color(0.23f, 0.15f, 0.02f);

    private static int version;

    static ControlHints()
    {
        ControlScheme.Changed += Bump;
        ControllerBindings.Changed += Bump;
    }

    // Naik setiap kali skema atau binding berubah. ControlHintLabel memakai ini untuk tahu kapan menulis ulang.
    public static int Version => version;

    public static bool IsGamepad => ControlScheme.IsGamepad;

    private static void Bump()
    {
        version++;
    }

    // ---------- Petunjuk siap pakai ----------

    // "[Space]" atau "(Y)".
    public static string Glyph(PadAction action)
    {
        return IsGamepad ? PadGlyph(action) : "[" + KeyboardLabel(action) + "]";
    }

    public static string Move => IsGamepad ? "(" + Neutral("L-Stick") + ")" : "[WASD]";

    // Petunjuk navigasi menu (pilih item).
    public static string Navigate => IsGamepad ? "(" + Neutral("D-pad") + ")" : "[Panah]";

    public static string Confirm => Glyph(PadAction.Select);

    public static string Back => Glyph(PadAction.Back);

    // Tombol gamepad untuk sebuah aksi, mis. "(Y)" berwarna. Selalu versi gamepad walau skema aktif keyboard.
    public static string PadGlyph(PadAction action)
    {
        return "(" + PadName(ControllerBindings.ControlName(ControllerBindings.GetPath(action))) + ")";
    }

    // Isi tombol gamepad tanpa kurung, berwarna.
    public static string PadName(string control)
    {
        switch (control)
        {
            case "buttonSouth": return Colored("A", PadAHex);
            case "buttonEast": return Colored("B", PadBHex);
            case "buttonWest": return Colored("X", PadXHex);
            case "buttonNorth": return Colored("Y", PadYHex);
            default: return Neutral(PlainPadName(control));
        }
    }

    public static string PlainPadName(string control)
    {
        switch (control)
        {
            case "buttonSouth": return "A";
            case "buttonEast": return "B";
            case "buttonWest": return "X";
            case "buttonNorth": return "Y";
            case "leftShoulder": return "LB";
            case "rightShoulder": return "RB";
            case "leftTrigger": return "LT";
            case "rightTrigger": return "RT";
            case "start": return "Menu";
            case "select": return "View";
            case "leftStickPress": return "LS";
            case "rightStickPress": return "RS";
            case "dpad/up": return "D-pad Atas";
            case "dpad/down": return "D-pad Bawah";
            case "dpad/left": return "D-pad Kiri";
            case "dpad/right": return "D-pad Kanan";
            case "": return "-";
            default: return control;
        }
    }

    public static string KeyboardLabel(PadAction action)
    {
        switch (action)
        {
            case PadAction.Attack: return "Klik Kiri";
            case PadAction.Interact: return "E";
            case PadAction.AuraBurst: return "Space";
            case PadAction.CycleTarget: return "Tab";
            case PadAction.Pause: return "Esc";
            case PadAction.Select: return "Enter";
            case PadAction.Back: return "Esc";
            default: return action.ToString();
        }
    }

    // ---------- Tutup tombol (kotak "E" di kartu gate dan portal Goal) ----------

    // Keyboard: kotak emas berhuruf "E". Gamepad: lingkaran warna tombol (Y kuning, dst.) atau kotak gelap untuk RT/RB.
    public static void StyleCap(Image cap, TextMeshProUGUI label, PadAction action)
    {
        if (cap == null || label == null)
            return;

        string text;
        Color fill;
        Color textColor;

        if (IsGamepad)
        {
            string control = ControllerBindings.ControlName(ControllerBindings.GetPath(action));
            text = PlainPadName(control);

            switch (control)
            {
                case "buttonSouth": fill = PadA; break;
                case "buttonEast": fill = PadB; break;
                case "buttonWest": fill = PadX; break;
                case "buttonNorth": fill = PadY; break;
                default: fill = CapNeutral; break;
            }

            textColor = fill == CapNeutral ? Color.white : new Color(0.06f, 0.07f, 0.09f);
        }
        else
        {
            text = KeyboardLabel(action);
            fill = new Color(0.93f, 0.66f, 0.22f);
            textColor = CapKeyboardText;
        }

        if (label.text != text)
        {
            label.text = text;
            label.fontSize = text.Length <= 1 ? 24f : text.Length <= 2 ? 20f : 16f;

            float width = text.Length <= 1 ? 40f : text.Length <= 2 ? 52f : 30f + text.Length * 10f;
            LayoutElement layout = cap.GetComponent<LayoutElement>();

            if (layout != null)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
            }
        }

        cap.color = fill;
        label.color = textColor;
    }

    // ---------- Util ----------

    private static string Colored(string text, string hex)
    {
        return "<b><color=" + hex + ">" + text + "</color></b>";
    }

    private static string Neutral(string text)
    {
        return Colored(text, PadNeutralHex);
    }

    private static Color Hex(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.white;
    }
}
