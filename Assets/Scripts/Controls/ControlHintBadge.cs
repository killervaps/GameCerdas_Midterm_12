using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Petunjuk tombol berbentuk lencana dengan latar gelap transparan, seperti petunjuk tombol di game konsol.
//   Gamepad   lingkaran gelap berisi huruf tombol berwarna tanpa kurung, mis. Y kuning. LB/RT/Menu ikut lingkaran
//             (melebar jadi kapsul untuk nama yang panjang).
//   Keyboard  kapsul gelap berisi "[Space]".
// Isi dan bentuknya ikut berganti saat skema kontrol atau binding berubah.
[DisallowMultipleComponent]
public class ControlHintBadge : MonoBehaviour
{
    private const float Diameter = 32f;
    private const float Padding = 12f;

    private Image background;
    private TextMeshProUGUI label;
    private PadAction action;
    private int version = -1;

    // Membuat lencana di parent, berpusat horizontal, sisi atasnya di posisi y (anchor atas tengah).
    public static ControlHintBadge Create(string name, Transform parent, PadAction action, float y, float fontSize = 18f)
    {
        Image background = UIKit.CreateImage(name, parent, new Color(0f, 0f, 0f, 0.5f), false);
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

        bool gamepad = ControlHints.IsGamepad;
        string plain;

        if (gamepad)
        {
            plain = ControlHints.PlainPadName(ControllerBindings.ControlName(ControllerBindings.GetPath(action)));
            label.text = ControlHints.PadName(ControllerBindings.ControlName(ControllerBindings.GetPath(action)));
        }
        else
        {
            plain = "[" + ControlHints.KeyboardLabel(action) + "]";
            label.text = plain;
        }

        // Satu-dua huruf: lingkaran penuh. Lebih panjang: kapsul selebar teks.
        bool round = gamepad && plain.Length <= 2;
        float width = round ? Diameter : Mathf.Max(Diameter, label.GetPreferredValues(plain).x + Padding * 2f);

        background.sprite = round ? UIKit.Circle : UIKit.Rounded;
        background.type = round ? Image.Type.Simple : Image.Type.Sliced;

        RectTransform rect = background.rectTransform;
        rect.sizeDelta = new Vector2(width, Diameter);
    }
}
