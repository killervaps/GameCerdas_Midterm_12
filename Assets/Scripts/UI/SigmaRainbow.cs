using UnityEngine;
using UnityEngine.UI;

// Latar berwarna RGB berputar untuk elemen rarity SIGMA, mis. chip gate di HUD.
// Teks di atasnya (opsional) memakai versi gelap dari warna latar supaya tetap terbaca.
[DisallowMultipleComponent]
public class SigmaRainbow : MonoBehaviour
{
    [SerializeField] private Graphic background;
    [SerializeField] private Graphic label;
    // Seberapa gelap teks dibanding latar (0 = sama, 1 = tinta hitam).
    [SerializeField, Range(0f, 1f)] private float labelDarkness = 0.82f;

    public void Bind(Graphic backgroundGraphic, Graphic labelGraphic)
    {
        background = backgroundGraphic;
        label = labelGraphic;
        Apply();
    }

    private void Update()
    {
        Apply();
    }

    private void Apply()
    {
        Color color = GateRarityStyle.SigmaColor();

        if (background != null)
            background.color = color;

        if (label != null)
            label.color = Color.Lerp(color, UIKit.Ink, labelDarkness);
    }
}
