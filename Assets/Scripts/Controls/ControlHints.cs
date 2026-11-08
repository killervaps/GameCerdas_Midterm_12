using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Teks petunjuk tombol yang mengikuti skema kontrol aktif.
//   Keyboard/mouse  "Aura Burst [Space]"   (tombol dibaca dari binding keyboard saat ini)
//   Gamepad         "Aura Burst" + ikon tombol ala Xbox (ControlGlyphs), mis. lingkaran gelap berhuruf Y kuning.
// Tombol dibaca dari binding saat ini, jadi petunjuk ikut berubah setelah rebind keyboard maupun gamepad.
// Label yang memakai ikon harus memasang sprite asset lewat ControlGlyphs.Apply (ControlHintLabel.Bind sudah melakukannya).
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
        KeyboardBindings.Changed += Bump;
    }

    // Naik setiap kali skema atau binding berubah. ControlHintLabel memakai ini untuk tahu kapan menulis ulang.
    public static int Version => version;

    public static bool IsGamepad => ControlScheme.IsGamepad;

    private static void Bump()
    {
        version++;
    }

    // ---------- Petunjuk siap pakai ----------

    // "[Space]" atau ikon tombol gamepad.
    public static string Glyph(PadAction action)
    {
        return IsGamepad ? PadGlyph(action) : "[" + KeyboardLabel(action) + "]";
    }

    // "[WASD]" (mengikuti binding gerak) atau ikon stik kiri.
    public static string Move => IsGamepad ? Icon("LS", "L-Stick") : "[" + MoveKeys() + "]";

    // Petunjuk navigasi menu (pilih item).
    public static string Navigate => IsGamepad ? Icon("Dpad", "D-pad") : "[Panah]";

    public static string Confirm => Glyph(PadAction.Select);

    public static string Back => Glyph(PadAction.Back);

    // Ikon gamepad bernama (mis. "LT", "LB"), dengan teks cadangan jika atlas ikon tidak ada.
    public static string Icon(string glyphName, string fallback)
    {
        return ControlGlyphs.Has(glyphName) ? ControlGlyphs.Tag(glyphName) : "(" + Neutral(fallback) + ")";
    }

    // Ikon tombol gamepad sebuah aksi. Selalu versi gamepad walau skema aktif keyboard.
    public static string PadGlyph(PadAction action)
    {
        return PadControlGlyph(ControllerBindings.ControlName(ControllerBindings.GetPath(action)));
    }

    // Ikon untuk nama kontrol gamepad, mis. "buttonNorth". Kontrol tanpa ikon ditulis "(nama)".
    public static string PadControlGlyph(string control)
    {
        string glyph = ControlGlyphs.ForControl(control);
        return ControlGlyphs.Has(glyph) ? ControlGlyphs.Tag(glyph) : "(" + PadName(control) + ")";
    }

    // Isi tombol gamepad tanpa kurung, berwarna (cadangan teks).
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

    // Nama tombol keyboard/mouse aksi ini sesuai binding saat ini.
    public static string KeyboardLabel(PadAction action)
    {
        switch (action)
        {
            case PadAction.Attack: return KeyboardBindings.Name(KeyAction.Attack);
            case PadAction.Interact: return KeyboardBindings.Name(KeyAction.Interact);
            case PadAction.AuraBurst: return KeyboardBindings.Name(KeyAction.AuraBurst);
            case PadAction.CycleTarget: return KeyboardBindings.Name(KeyAction.CycleTarget);
            case PadAction.Pause: return KeyboardBindings.Name(KeyAction.Pause);
            case PadAction.Select: return "Enter";
            case PadAction.Back: return "Esc";
            default: return action.ToString();
        }
    }

    // "WASD" jika keempat tombol gerak berupa satu huruf, selain itu "W/A/S/D" (urutan atas, kiri, bawah, kanan).
    public static string MoveKeys()
    {
        string up = KeyboardBindings.Name(KeyAction.MoveUp);
        string left = KeyboardBindings.Name(KeyAction.MoveLeft);
        string down = KeyboardBindings.Name(KeyAction.MoveDown);
        string right = KeyboardBindings.Name(KeyAction.MoveRight);

        if (up.Length == 1 && left.Length == 1 && down.Length == 1 && right.Length == 1)
            return up + left + down + right;

        return up + "/" + left + "/" + down + "/" + right;
    }

    // ---------- Tutup tombol (kotak "E" di kartu gate dan portal Goal) ----------

    // Keyboard: kotak emas berisi tombol Interaksi saat ini. Gamepad: ikon tombol (mis. RT) menggantikan kotaknya.
    public static void StyleCap(Image cap, TextMeshProUGUI label, PadAction action)
    {
        if (cap == null || label == null)
            return;

        Image glyph = CapGlyph(cap);
        LayoutElement layout = cap.GetComponent<LayoutElement>();

        if (IsGamepad)
        {
            string control = ControllerBindings.ControlName(ControllerBindings.GetPath(action));
            Sprite sprite = ControlGlyphs.Get(ControlGlyphs.ForControl(control));

            if (sprite != null)
            {
                float height = (layout != null && layout.preferredHeight > 0f ? layout.preferredHeight : cap.rectTransform.rect.height) + 6f;
                float width = height * sprite.rect.width / sprite.rect.height;

                glyph.sprite = sprite;
                glyph.rectTransform.sizeDelta = new Vector2(width, height);
                glyph.enabled = true;

                cap.color = Color.clear;
                label.text = string.Empty;
                SetCapWidth(layout, width - 6f);
                return;
            }
        }

        glyph.enabled = false;

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
            SetCapWidth(layout, text.Length <= 1 ? 40f : text.Length <= 2 ? 52f : 30f + text.Length * 10f);
        }

        cap.color = fill;
        label.color = textColor;
    }

    private static void SetCapWidth(LayoutElement layout, float width)
    {
        if (layout != null && !Mathf.Approximately(layout.preferredWidth, width))
        {
            layout.minWidth = width;
            layout.preferredWidth = width;
        }
    }

    // Image ikon gamepad di tengah tutup tombol, dibuat sekali saat pertama dibutuhkan.
    private static Image CapGlyph(Image cap)
    {
        Transform existing = cap.transform.Find("PadGlyph");

        if (existing != null && existing.TryGetComponent(out Image found))
            return found;

        Image glyph = UIKit.CreateImage("PadGlyph", cap.transform, Color.white, false);
        glyph.preserveAspect = true;
        glyph.enabled = false;

        RectTransform rect = glyph.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        glyph.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return glyph;
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
