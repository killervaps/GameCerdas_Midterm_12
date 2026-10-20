using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Palet warna dan pabrik elemen uGUI. Semua UI game dibangun dari kode lewat kelas ini
// supaya tampilannya seragam dan tidak bergantung pada prefab atau sprite eksternal.
// Teks hanya memakai karakter ASCII/Latin-1 supaya aman dengan font TMP bawaan.
public static class UIKit
{
    // ---------- Palet ----------

    public static readonly Color Backdrop = new Color(0.04f, 0.05f, 0.08f, 0.82f);
    public static readonly Color Panel = new Color(0.09f, 0.11f, 0.15f, 0.94f);
    public static readonly Color PanelLight = new Color(0.15f, 0.18f, 0.24f, 0.95f);
    public static readonly Color OutlineColor = new Color(1f, 1f, 1f, 0.08f);
    public static readonly Color Accent = new Color(0.95f, 0.76f, 0.31f);
    public static readonly Color AccentDark = new Color(0.55f, 0.42f, 0.14f);
    public static readonly Color Text = new Color(0.93f, 0.94f, 0.96f);
    public static readonly Color TextMuted = new Color(0.62f, 0.66f, 0.73f);
    public static readonly Color Buff = new Color(0.33f, 0.87f, 0.33f);
    public static readonly Color TradeOff = new Color(1f, 0.4f, 0.4f);
    public static readonly Color Neutral = new Color(0.67f, 0.67f, 0.67f);

    public const string BuffHex = "#55dd55";
    public const string TradeOffHex = "#ff6666";
    public const string MutedHex = "#9ea8b8";
    public const string AccentHex = "#f2c14e";

    public static Color RarityColor(GateRarity rarity)
    {
        switch (rarity)
        {
            case GateRarity.Rare: return new Color(0.31f, 0.64f, 1f);
            case GateRarity.Epic: return new Color(0.78f, 0.49f, 1f);
            default: return new Color(0.69f, 0.72f, 0.77f);
        }
    }

    public static string RarityLabel(GateRarity rarity)
    {
        switch (rarity)
        {
            case GateRarity.Rare: return "LANGKA";
            case GateRarity.Epic: return "EPIK";
            default: return "BIASA";
        }
    }

    public static Color ThreatColor(ThreatLevel level)
    {
        ColorUtility.TryParseHtmlString(ThreatEstimator.LevelColorHex(level), out Color color);
        return color;
    }

    public static string StatLabel(StatType stat)
    {
        switch (stat)
        {
            case StatType.MaxHP: return "Max HP";
            case StatType.AttackRate: return "Attack Rate";
            case StatType.MoveSpeed: return "Move Speed";
            default: return stat.ToString();
        }
    }

    public static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
    }

    public static string FormatRatio(float ratio)
    {
        return float.IsInfinity(ratio) || float.IsNaN(ratio) ? "-" : ratio.ToString("0.00");
    }

    // ---------- Sprite prosedural ----------

    private static Sprite rounded;
    private static Sprite verticalFade;
    private static Sprite horizontalFade;
    private static Sprite vignette;
    private static Sprite circle;
    private static Sprite ring;
    private static Sprite burst;
    private static Sprite rays;

    // Lingkaran penuh (antialias).
    public static Sprite Circle => circle != null ? circle : circle = CreatePolarSprite("UIKit_Circle", 128, (r, a) => Edge(1f - r, 128));

    // Cincin tipis, dipakai sebagai track dan isi radial cooldown.
    public static Sprite Ring => ring != null ? ring : ring = CreatePolarSprite("UIKit_Ring", 128, (r, a) => Mathf.Min(Edge(1f - r, 128), Edge(r - 0.84f, 128)));

    // Ikon ledakan: bintang 8 sudut dengan ujung runcing.
    public static Sprite Burst => burst != null ? burst : burst = CreatePolarSprite("UIKit_Burst", 128, (r, a) =>
    {
        // Gelombang segitiga 8 kali per putaran -> ujung runcing seperti ledakan kartun.
        float u = Mathf.Repeat(a / (2f * Mathf.PI) * 8f, 1f);
        float spike = Mathf.Pow(1f - Mathf.Abs(u * 2f - 1f), 1.6f);
        float boundary = Mathf.Lerp(0.45f, 1f, spike);
        return Edge(boundary - r, 128);
    });

    // Sinar cahaya memancar dari tengah, memudar ke tepi. Dipakai pengumuman ronde.
    public static Sprite Rays => rays != null ? rays : rays = CreatePolarSprite("UIKit_Rays", 256, (r, a) =>
    {
        float beam = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 6f)), 10f);
        float falloff = 1f - SmoothStep01(0.1f, 1f, r);
        return Mathf.Clamp01(beam * falloff + (1f - SmoothStep01(0f, 0.35f, r)) * 0.6f);
    });

    // Cahaya lembut berbentuk lingkaran: pekat di tengah, memudar habis di tepi.
    private static Sprite glow;

    public static Sprite Glow => glow != null ? glow : glow = CreatePolarSprite("UIKit_Glow", 128, (r, a) =>
    {
        float fade = 1f - SmoothStep01(0.35f, 1f, r);
        return fade * fade;
    });

    // smoothstep ala GLSL (0 di edge0, 1 di edge1). Mathf.SmoothStep berbeda: ia menginterpolasi antar nilai.
    private static float SmoothStep01(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    // Tepi halus selebar ~1 piksel untuk jarak bertanda d (dalam satuan radius).
    private static float Edge(float d, int size)
    {
        return Mathf.Clamp01(d * size * 0.5f + 0.5f);
    }

    // Sprite dari fungsi alpha(radius 0-1, sudut radian).
    private static Sprite CreatePolarSprite(string spriteName, int size, System.Func<float, float, float> alpha)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = spriteName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);
                float value = radius > 1.02f ? 0f : Mathf.Clamp01(alpha(radius, angle));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(value * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    // Bingkai radial (transparan di tengah, pekat di tepi), dipakai efek layar merah saat MC terkena damage.
    public static Sprite Vignette
    {
        get
        {
            if (vignette == null)
                vignette = CreateVignetteSprite(128);

            return vignette;
        }
    }

    private static Sprite CreateVignetteSprite(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "UIKit_Vignette",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, distance));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "UIKit_Vignette";
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    // Kotak sudut membulat, dipakai sebagai sprite sliced untuk semua panel dan tombol.
    public static Sprite Rounded
    {
        get
        {
            if (rounded == null)
                rounded = CreateRoundedSprite(64, 14);

            return rounded;
        }
    }

    // Gradasi vertikal (transparan di atas, pekat di bawah), dipakai latar menu utama.
    public static Sprite VerticalFade
    {
        get
        {
            if (verticalFade == null)
                verticalFade = CreateVerticalFadeSprite(128);

            return verticalFade;
        }
    }

    // Gradasi horizontal (pekat di kiri, transparan di kanan), dipakai menu utama supaya karakter di kanan tetap terlihat.
    public static Sprite HorizontalFade
    {
        get
        {
            if (horizontalFade == null)
                horizontalFade = CreateHorizontalFadeSprite(256);

            return horizontalFade;
        }
    }

    private static Sprite CreateHorizontalFadeSprite(int width)
    {
        Texture2D texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            name = "UIKit_HorizontalFade",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        for (int x = 0; x < width; x++)
        {
            float t = x / (float)(width - 1);
            // Pekat sampai ~30% lebar, lalu memudar habis di ~65%.
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.65f, t));
            texture.SetPixel(x, 0, new Color(1f, 1f, 1f, alpha));
        }

        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, 1f), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "UIKit_HorizontalFade";
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    private static Sprite CreateRoundedSprite(int size, int radius)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "UIKit_Rounded",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float cx = Mathf.Clamp(px, radius, size - radius);
                float cy = Mathf.Clamp(py, radius, size - radius);
                float distance = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius - distance + 0.5f) * 255f);

                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));

        sprite.name = "UIKit_Rounded";
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    private static Sprite CreateVerticalFadeSprite(int height)
    {
        Texture2D texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
        {
            name = "UIKit_VerticalFade",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        for (int y = 0; y < height; y++)
        {
            float t = 1f - y / (float)(height - 1);
            texture.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 1f, t * t)));
        }

        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "UIKit_VerticalFade";
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    // ---------- RectTransform ----------

    public static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : 5;

        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    public static RectTransform Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    // Menempatkan rect pada satu titik jangkar dengan ukuran tetap.
    public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    // Mengisi rentang horizontal [from, to] (0-1) pada parent, setinggi penuh.
    public static void SetHorizontalSpan(RectTransform rect, float from, float to)
    {
        rect.anchorMin = new Vector2(Mathf.Clamp01(from), 0f);
        rect.anchorMax = new Vector2(Mathf.Clamp01(to), 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // ---------- Grafis ----------

    public static Image CreateImage(string name, Transform parent, Color color, bool roundedCorners = true)
    {
        RectTransform rect = CreateRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        if (roundedCorners)
        {
            image.sprite = Rounded;
            image.type = Image.Type.Sliced;
        }

        return image;
    }

    public static Image CreatePanel(string name, Transform parent, Color color, bool outline = true)
    {
        Image image = CreateImage(name, parent, color);

        if (outline)
        {
            Outline line = image.gameObject.AddComponent<Outline>();
            line.effectColor = OutlineColor;
            line.effectDistance = new Vector2(1.5f, -1.5f);
        }

        return image;
    }

    public static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        string text,
        float size,
        Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.Left,
        bool bold = false)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.raycastTarget = false;
        label.richText = true;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        return label;
    }

    // Tombol dengan label. Ukuran diatur LayoutElement supaya cocok di VerticalLayoutGroup.
    public static Button CreateButton(
        string name,
        Transform parent,
        string label,
        UnityAction onClick,
        float height = 64f,
        bool primary = false)
    {
        Image background = CreateImage(name, parent, Color.white);
        background.raycastTarget = true;

        Button button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;

        Color baseColor = primary ? Accent : PanelLight;
        ColorBlock colors = button.colors;
        colors.normalColor = baseColor;
        colors.highlightedColor = primary ? new Color(1f, 0.85f, 0.45f) : new Color(0.24f, 0.28f, 0.36f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = primary ? AccentDark : new Color(0.1f, 0.12f, 0.16f, 1f);
        colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        if (onClick != null)
            button.onClick.AddListener(onClick);

        TextMeshProUGUI text = CreateText("Label", background.transform, label, 26f,
            primary ? new Color(0.12f, 0.09f, 0.02f) : Text, TextAlignmentOptions.Center, true);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(text.rectTransform, 12f, 12f, 4f, 4f);

        LayoutElement layout = background.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;

        return button;
    }

    // ---------- Layout ----------

    public static VerticalLayoutGroup AddVertical(
        GameObject go,
        float spacing,
        RectOffset padding = null,
        TextAnchor alignment = TextAnchor.UpperLeft)
    {
        VerticalLayoutGroup group = go.AddComponent<VerticalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding ?? new RectOffset(0, 0, 0, 0);
        group.childAlignment = alignment;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        return group;
    }

    public static HorizontalLayoutGroup AddHorizontal(
        GameObject go,
        float spacing,
        RectOffset padding = null,
        TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        HorizontalLayoutGroup group = go.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding ?? new RectOffset(0, 0, 0, 0);
        group.childAlignment = alignment;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        return group;
    }

    // Tinggi mengikuti isi. Dipakai panel yang isinya berubah-ubah.
    public static ContentSizeFitter FitHeight(GameObject go)
    {
        ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return fitter;
    }

    public static LayoutElement Layout(Component target, float preferredHeight = -1f, float preferredWidth = -1f, float flexibleWidth = -1f)
    {
        LayoutElement layout = target.GetComponent<LayoutElement>();

        if (layout == null)
            layout = target.gameObject.AddComponent<LayoutElement>();

        if (preferredHeight >= 0f)
        {
            layout.minHeight = preferredHeight;
            layout.preferredHeight = preferredHeight;
        }

        if (preferredWidth >= 0f)
        {
            layout.minWidth = preferredWidth;
            layout.preferredWidth = preferredWidth;
        }

        if (flexibleWidth >= 0f)
            layout.flexibleWidth = flexibleWidth;

        return layout;
    }

    // Garis tipis pemisah di dalam VerticalLayoutGroup.
    public static Image CreateDivider(Transform parent)
    {
        Image line = CreateImage("Divider", parent, new Color(1f, 1f, 1f, 0.08f), false);
        Layout(line, 2f);
        return line;
    }

    // Bar horizontal: latar + isi yang lebarnya diatur lewat SetBarFill.
    public static RectTransform CreateBar(string name, Transform parent, Color background, Color fill, out Image fillImage)
    {
        Image back = CreateImage(name, parent, background);
        fillImage = CreateImage("Fill", back.transform, fill);
        SetBarFill(fillImage.rectTransform, 1f);
        return back.rectTransform;
    }

    public static void SetBarFill(RectTransform fill, float value)
    {
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
    }

    public static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
    }

    public static void ForceRebuild(RectTransform rect)
    {
        if (rect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    // Pembungkus aman untuk aksi tombol: error di satu handler tidak mengunci UI.
    public static UnityAction Safe(Action action)
    {
        return () =>
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        };
    }
}
