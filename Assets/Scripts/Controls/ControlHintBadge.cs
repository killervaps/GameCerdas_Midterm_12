using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Petunjuk tombol berbentuk lencana, seperti petunjuk tombol di game konsol.
//   Gamepad   ikon tombol ala Xbox dari ControlGlyphs (mis. lingkaran gelap berhuruf Y kuning, bumper RB, Menu).
//   Keyboard  kapsul gelap transparan berisi "[Space]" (tombol keyboard saat ini).
// Isi dan bentuknya ikut berganti saat skema kontrol atau binding berubah.
[DisallowMultipleComponent]
public class ControlHintBadge : MonoBehaviour
{
    private const float Diameter = 32f;
    // Ikon gamepad sedikit lebih besar karena sudah punya garis tepi sendiri.
    private const float GlyphHeight = 36f;
    private const float Padding = 12f;
    private static readonly Color KeyboardBackground = new Color(0f, 0f, 0f, 0.5f);

    private Image background;
    private TextMeshProUGUI label;
    private PadAction action;
    private int version = -1;

    // Membuat lencana di parent, berpusat horizontal, sisi atasnya di posisi y (anchor atas tengah).
    public static ControlHintBadge Create(string name, Transform parent, PadAction action, float y, float fontSize = 18f)
    {
        Image background = UIKit.CreateImage(name, parent, KeyboardBackground, false);
        UIKit.Place(background.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(Diameter, Diameter));

        TextMeshProUGUI label = UIKit.CreateText("Label", background.transform, string.Empty, fontSize, UIKit.Text, TextAlignmentOptions.Center, true);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Stretch(label.rectTransform);

        ControlHintBadge badge = background.gameObject.AddComponent<ControlHintBadge>();
        badge.background = background;
        badge.label = label;
        badge.action = action;
        badge.Refresh();
        return badge;
    }

    private void OnEnable()
    {
        if (label != null && version != ControlHints.Version)
            Refresh();
    }

    private void LateUpdate()
    {
        if (label != null && version != ControlHints.Version)
            Refresh();
    }

    public void Refresh()
    {
        version = ControlHints.Version;
        RectTransform rect = background.rectTransform;

        if (ControlHints.IsGamepad)
        {
            string control = ControllerBindings.ControlName(ControllerBindings.GetPath(action));
            Sprite sprite = ControlGlyphs.Get(ControlGlyphs.ForControl(control));

            if (sprite != null)
            {
                background.sprite = sprite;
                background.type = Image.Type.Simple;
                background.color = Color.white;
                label.text = string.Empty;
                rect.sizeDelta = new Vector2(GlyphHeight * sprite.rect.width / sprite.rect.height, GlyphHeight);
                return;
            }

            // Cadangan tanpa atlas ikon: lingkaran gelap berhuruf berwarna.
            string plainName = ControlHints.PlainPadName(control);
            label.text = ControlHints.PadName(control);
            SetPill(plainName, plainName.Length <= 2);
            return;
        }

        string plain = "[" + ControlHints.KeyboardLabel(action) + "]";
        label.text = plain;
        SetPill(plain, false);
    }

    // Satu-dua huruf: lingkaran penuh. Lebih panjang: kapsul selebar teks.
    private void SetPill(string plain, bool round)
    {
        float width = round ? Diameter : Mathf.Max(Diameter, label.GetPreferredValues(plain).x + Padding * 2f);

        background.color = KeyboardBackground;
        background.sprite = round ? UIKit.Circle : UIKit.Rounded;
        background.type = round ? Image.Type.Simple : Image.Type.Sliced;
        background.rectTransform.sizeDelta = new Vector2(width, Diameter);
    }
}
