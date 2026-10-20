using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pengumuman besar di tengah layar saat ronde selesai ("RONDE BERSIH!" / "KEMENANGAN!").
// Urutan animasi: layar meredup, sinar cahaya berputar di belakang, pita gelap terbuka,
// garis emas memanjang, judul jatuh membesar lalu memantul, subjudul muncul, lalu semuanya memudar.
// Selama tampil, GameUI menyembunyikan overlay lain (HUD, health bar, angka damage, tips).
public class RoundAnnouncement : UIScreen
{
    [SerializeField] private float defaultDuration = 3f;

    private Image dim;
    private Image rays;
    private Image raysInner;
    private RectTransform band;
    private Image bandImage;
    private RectTransform lineTop;
    private RectTransform lineBottom;
    private Image lineTopImage;
    private Image lineBottomImage;
    private TextMeshProUGUI kicker;
    private TextMeshProUGUI title;
    private TextMeshProUGUI subtitle;
    private RectTransform leftStar;
    private RectTransform rightStar;
    private Image leftStarImage;
    private Image rightStarImage;

    private float time;
    private float duration;
    private Color accent;

    public bool IsPlaying { get; private set; }

    public event Action Finished;

    protected override void Build()
    {
        dim = UIKit.CreateImage("Dim", rect, Color.clear, false);
        UIKit.Stretch(dim.rectTransform);

        rays = UIKit.CreateImage("Rays", rect, Color.clear, false);
        rays.sprite = UIKit.Rays;
        UIKit.Place(rays.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1500f, 1500f));

        raysInner = UIKit.CreateImage("RaysInner", rect, Color.clear, false);
        raysInner.sprite = UIKit.Rays;
        UIKit.Place(raysInner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(900f, 900f));

        // Pita horizontal selebar layar.
        bandImage = UIKit.CreateImage("Band", rect, Color.clear, false);
        band = bandImage.rectTransform;
        band.anchorMin = new Vector2(0f, 0.5f);
        band.anchorMax = new Vector2(1f, 0.5f);
        band.pivot = new Vector2(0.5f, 0.5f);
        band.sizeDelta = new Vector2(0f, 300f);
        band.anchoredPosition = new Vector2(0f, 20f);

        lineTopImage = UIKit.CreateImage("LineTop", rect, Color.clear, false);
        lineTop = lineTopImage.rectTransform;
        UIKit.Place(lineTop, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(0f, 4f));

        lineBottomImage = UIKit.CreateImage("LineBottom", rect, Color.clear, false);
        lineBottom = lineBottomImage.rectTransform;
        UIKit.Place(lineBottom, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(0f, 4f));

        kicker = UIKit.CreateText("Kicker", rect, string.Empty, 28f, Color.white, TextAlignmentOptions.Center, true);
        kicker.characterSpacing = 18f;
        kicker.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Place(kicker.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 128f), new Vector2(1400f, 40f));

        title = UIKit.CreateText("Title", rect, string.Empty, 132f, Color.white, TextAlignmentOptions.Center, true);
        title.characterSpacing = 8f;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.outlineWidth = 0.18f;
        title.outlineColor = new Color32(40, 22, 0, 255);
        title.enableVertexGradient = true;
        UIKit.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(1600f, 170f));

        subtitle = UIKit.CreateText("Subtitle", rect, string.Empty, 28f, UIKit.Text, TextAlignmentOptions.Center);
        subtitle.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Place(subtitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -88f), new Vector2(1400f, 40f));

        leftStarImage = UIKit.CreateImage("StarLeft", rect, Color.clear, false);
        leftStarImage.sprite = UIKit.Burst;
        leftStar = leftStarImage.rectTransform;
        UIKit.Place(leftStar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-620f, 30f), new Vector2(70f, 70f));

        rightStarImage = UIKit.CreateImage("StarRight", rect, Color.clear, false);
        rightStarImage.sprite = UIKit.Burst;
        rightStar = rightStarImage.rectTransform;
        UIKit.Place(rightStar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(620f, 30f), new Vector2(70f, 70f));
    }

    public void Play(string kickerText, string titleText, string subtitleText, Color accentColor, float seconds = -1f)
    {
        kicker.text = kickerText;
        title.text = titleText;
        subtitle.text = subtitleText;
        accent = accentColor;
        duration = seconds > 0f ? seconds : defaultDuration;
        time = 0f;

        // Gradasi judul: putih keemasan di atas, warna aksen di bawah.
        Color top = Color.Lerp(Color.white, accentColor, 0.15f);
        title.colorGradient = new VertexGradient(top, top, accentColor, accentColor);
        kicker.color = accentColor;

        // Bintang diletakkan di luar lebar judul supaya tidak menimpa huruf.
        float titleWidth = title.GetPreferredValues(titleText).x;
        float starX = Mathf.Min(titleWidth * 0.5f + 70f, 900f);
        leftStar.anchoredPosition = new Vector2(-starX, 30f);
        rightStar.anchoredPosition = new Vector2(starX, 30f);

        IsPlaying = true;
        Show();
        Animate(0f);
    }

    // Hentikan seketika, mis. saat run diulang atau kembali ke menu.
    public void Stop()
    {
        if (!IsPlaying)
            return;

        IsPlaying = false;
        HideInstant();
    }

    protected override void Update()
    {
        base.Update();

        if (!IsPlaying)
            return;

        // Tetap berjalan meski game dijeda supaya alur tidak tertahan.
        time += Time.unscaledDeltaTime;
        Animate(time);

        if (time >= duration)
        {
            IsPlaying = false;
            Hide();
            Finished?.Invoke();
        }
    }

    private void Animate(float t)
    {
        float fadeOut = Mathf.Clamp01((duration - t) / 0.45f);

        // Latar meredup.
        float dimAlpha = Ease(t / 0.3f) * 0.6f * fadeOut;
        dim.color = new Color(0f, 0f, 0f, dimAlpha);

        // Sinar berputar berlawanan arah.
        float rayAlpha = Ease((t - 0.1f) / 0.5f) * fadeOut;
        rays.color = new Color(accent.r, accent.g, accent.b, 0.22f * rayAlpha);
        rays.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 12f);
        float rayScale = Mathf.Lerp(0.6f, 1f, Ease(t / 0.8f));
        rays.rectTransform.localScale = new Vector3(rayScale, rayScale, 1f);
        raysInner.color = new Color(1f, 1f, 1f, 0.18f * rayAlpha);
        raysInner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * 20f);

        // Pita terbuka vertikal.
        float bandOpen = Ease(t / 0.28f);
        band.localScale = new Vector3(1f, bandOpen, 1f);
        bandImage.color = new Color(0.03f, 0.04f, 0.07f, 0.82f * fadeOut);

        // Garis emas memanjang dari tengah.
        float lineWidth = Ease((t - 0.15f) / 0.5f) * 1100f;
        lineTop.sizeDelta = new Vector2(lineWidth, 4f);
        lineBottom.sizeDelta = new Vector2(lineWidth, 4f);
        Color lineColor = new Color(accent.r, accent.g, accent.b, fadeOut);
        lineTopImage.color = lineColor;
        lineBottomImage.color = lineColor;

        // Judul: jatuh dari besar, memantul sedikit.
        float titleT = Mathf.Clamp01((t - 0.18f) / 0.45f);
        float titleScale = titleT <= 0f ? 2.4f : Mathf.LerpUnclamped(2.4f, 1f, EaseOutBack(titleT));
        title.rectTransform.localScale = new Vector3(titleScale, titleScale, 1f);
        title.alpha = Mathf.Clamp01(titleT * 3f) * fadeOut;

        // Kicker meluncur turun, subjudul naik.
        float kickerT = Ease((t - 0.35f) / 0.35f);
        kicker.alpha = kickerT * fadeOut;
        kicker.rectTransform.anchoredPosition = new Vector2(0f, 128f + 20f * (1f - kickerT));

        float subT = Ease((t - 0.55f) / 0.4f);
        subtitle.alpha = subT * fadeOut;
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, -88f - 20f * (1f - subT));

        // Bintang kiri-kanan berputar.
        float starT = Ease((t - 0.45f) / 0.35f);
        float starScale = starT * (1f + 0.08f * Mathf.Sin(t * 6f));
        Color starColor = new Color(accent.r, accent.g, accent.b, starT * fadeOut);
        leftStar.localScale = new Vector3(starScale, starScale, 1f);
        rightStar.localScale = new Vector3(starScale, starScale, 1f);
        leftStar.localRotation = Quaternion.Euler(0f, 0f, t * 90f);
        rightStar.localRotation = Quaternion.Euler(0f, 0f, -t * 90f);
        leftStarImage.color = starColor;
        rightStarImage.color = starColor;
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t) * (1f - t);
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }
}
