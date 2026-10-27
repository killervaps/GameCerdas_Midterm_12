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

    // Gaya "tinta tebal" (arah visual UI): panel solid gelap, garis tinta hitam tebal, bayangan keras tanpa blur.
    public static readonly Color Backdrop = new Color(0.04f, 0.05f, 0.08f, 0.82f);
    public static readonly Color Ink = new Color(0.04f, 0.05f, 0.06f, 1f);
    // Isi panel cukup terang supaya garis tinta hitam terbaca sebagai tepi. Versi sebelumnya
    // (0.10, 0.12, 0.17) hampir sama gelap dengan tinta, sehingga panel tampak seperti lempeng hitam.
    public static readonly Color Panel = new Color(0.17f, 0.20f, 0.29f, 1f);
    // Tombol sekunder, label status arena.
    public static readonly Color PanelLight = new Color(0.26f, 0.31f, 0.42f, 1f);
    // Sumur di dalam panel: baris efek, kotak ringkasan, token yang belum aktif.
    public static readonly Color Well = new Color(0.10f, 0.125f, 0.19f, 1f);
    // Krem kertas stiker: token ronde aktif, label Safezone, penanda meter.
    public static readonly Color Cream = new Color(0.96f, 0.95f, 0.91f, 1f);
    // Emas aksen hanya untuk aksi dan status siap (tombol utama, progress pilih gate, skill siap,
    // momen menang). Digeser ke emas yang lebih hangat supaya tidak kembar dengan kuning ancaman "Sedang" (#ffcc33).
    public static readonly Color Accent = new Color(0.93f, 0.66f, 0.22f);
    public static readonly Color AccentHighlight = new Color(1f, 0.78f, 0.38f);
    public static readonly Color AccentDark = new Color(0.55f, 0.37f, 0.1f);
    public static readonly Color Text = new Color(0.93f, 0.94f, 0.96f);
    public static readonly Color TextMuted = new Color(0.62f, 0.66f, 0.73f);
    public static readonly Color Buff = new Color(0.33f, 0.87f, 0.33f);
    public static readonly Color TradeOff = new Color(1f, 0.4f, 0.4f);
    public static readonly Color Neutral = new Color(0.67f, 0.67f, 0.67f);
    // Bingkai fokus tombol. Terang supaya kontras minimal 3:1 terhadap backdrop gelap dan panel.
    public static readonly Color Focus = new Color(0.97f, 0.97f, 1f, 0.95f);

    public const string BuffHex = "#55dd55";
    public const string TradeOffHex = "#ff6666";
    public const string MutedHex = "#9ea8b8";
    public const string AccentHex = "#eda838";

    // ---------- Font ----------

    // Font judul dan angka besar (Fredoka, arah visual UI). Dimuat dari folder Resources karena GameUI
    // dibuat otomatis saat Play, jadi tidak ada field Inspector yang bisa diisi.
    private const string DisplayFontResource = "Fredoka-Bold SDF";

    private static TMP_FontAsset displayFont;
    private static bool displayFontLoaded;

    // Project ini mematikan Domain Reload (Enter Play Mode Options), jadi nilai statis terbawa antar sesi
    // Play. Tanpa reset ini, hasil "font tidak ditemukan" dari sesi lama tetap dipakai walaupun aset font
    // sudah dipindah ke folder Resources.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetFontCache()
    {
        displayFont = null;
        displayFontLoaded = false;
    }

    // Null jika aset belum ada di folder Resources. Teks lalu tetap memakai font TMP bawaan.
    public static TMP_FontAsset DisplayFont
    {
        get
        {
            if (!displayFontLoaded)
            {
                displayFontLoaded = true;
                displayFont = Resources.Load<TMP_FontAsset>(DisplayFontResource);

                if (displayFont == null)
                {
                    Debug.LogWarning("UIKit: font '" + DisplayFontResource + "' tidak ditemukan di folder Resources. Judul memakai font TMP bawaan.");
                }
                else if (!displayFont.HasCharacter('A') || !displayFont.HasCharacter('a'))
                {
                    // Atlas tanpa huruf membuat semua huruf diambil dari font cadangan tanpa bold, sehingga
                    // judul tampak tipis. Lebih baik tetap memakai font bawaan sampai atlas dibuat ulang.
                    Debug.LogWarning("UIKit: atlas '" + DisplayFontResource + "' hanya berisi " + displayFont.characterTable.Count +
                                     " karakter dan tidak punya huruf A-Z. Buat ulang dengan Character Set = Custom Range " +
                                     "(32-126,160-255,8594,8722). Sementara judul memakai font TMP bawaan.", displayFont);
                    displayFont = null;
                }
            }

            return displayFont;
        }
    }

    // Memakai font display untuk judul, ronde, timer, dan angka besar.
    // Fredoka-Bold sudah tebal, jadi bold sintetis TMP dimatikan supaya hurufnya tidak menggumpal.
    public static TextMeshProUGUI UseDisplayFont(TextMeshProUGUI label)
    {
        TMP_FontAsset font = DisplayFont;

        if (font != null)
        {
            label.font = font;
            label.fontStyle &= ~FontStyles.Bold;
        }

        return label;
    }

    // Untuk SIGMA ini warna sesaat dari putaran RGB. Elemen yang perlu terus berputar memakai SigmaRainbow.
    public static Color RarityColor(GateRarity rarity)
    {
        return GateRarityStyle.Color(rarity);
    }

    public static string RarityLabel(GateRarity rarity)
    {
        return GateRarityStyle.Label(rarity);
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

    // Radius bertingkat sebagai penanda jenis elemen: panel paling bulat, tombol dan baris sedang,
    // bar, pill, dan ikon kecil hampir siku supaya tidak semuanya berbentuk pil.
    private static Sprite roundedMedium;
    private static Sprite roundedSmall;

    // Radius 14, untuk panel dan kartu (default CreateImage).
    public static Sprite Rounded
    {
        get
        {
            if (rounded == null)
                rounded = CreateRoundedSprite("UIKit_Rounded", 64, 14);

            return rounded;
        }
    }

    // Radius 8, untuk tombol, baris daftar, dan kotak ringkasan.
    public static Sprite RoundedMedium
    {
        get
        {
            if (roundedMedium == null)
                roundedMedium = CreateRoundedSprite("UIKit_RoundedMedium", 64, 8);

            return roundedMedium;
        }
    }

    // Radius 4, untuk bar, track, pill, titik, dan ikon kecil.
    public static Sprite RoundedSmall
    {
        get
        {
            if (roundedSmall == null)
                roundedSmall = CreateRoundedSprite("UIKit_RoundedSmall", 64, 4);

            return roundedSmall;
        }
    }

    // Mengganti radius sudut sebuah Image yang dibuat CreateImage/CreatePanel.
    public static Image SetCorners(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        return image;
    }

    // ---------- Sprite tinta ----------
    // Isi sprite putih dan garis tepinya hitam. Image.color hanya mewarnai isi, karena hitam dikali
    // warna apa pun tetap hitam, jadi satu Image cukup untuk isi berwarna + garis tinta.

    public const float InkWidth = 4f;
    public static readonly Vector2 PanelShadow = new Vector2(5f, -6f);
    public static readonly Vector2 ButtonShadow = new Vector2(4f, -5f);

    private static Sprite inkPanel;
    private static Sprite inkMedium;
    private static Sprite inkSmall;
    private static Sprite inkCircle;
    private static Sprite inkTopBand;

    // Panel dan kartu: radius 14, tinta 4.
    public static Sprite InkPanel => inkPanel != null ? inkPanel : inkPanel = CreateInkSprite("UIKit_InkPanel", 64, 14, 14, 4, 4, 4, true);

    // Tombol, baris, kotak ringkasan: radius 8, tinta 3.
    public static Sprite InkMedium => inkMedium != null ? inkMedium : inkMedium = CreateInkSprite("UIKit_InkMedium", 64, 8, 8, 3, 3, 3, true);

    // Elemen kecil (tutup tombol keyboard, lencana role): radius 4, tinta 2.
    public static Sprite InkSmall => inkSmall != null ? inkSmall : inkSmall = CreateInkSprite("UIKit_InkSmall", 64, 4, 4, 2, 2, 2, true);

    // Lingkaran bertinta untuk ikon operator dan lingkaran skill. Dipakai sebagai Image Simple.
    public static Sprite InkCircle => inkCircle != null ? inkCircle : inkCircle = CreateInkSprite("UIKit_InkCircle", 128, 64, 64, 10, 10, 10, false);

    // Pita kepala kartu: sudut atas mengikuti bagian dalam panel (14 - 4), bawah siku dengan garis tinta.
    public static Sprite InkTopBand => inkTopBand != null ? inkTopBand : inkTopBand = CreateInkSprite("UIKit_InkTopBand", 64, 10, 0, 0, 0, 4, true);

    public static Image SetCircle(Image image)
    {
        image.sprite = InkCircle;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        return image;
    }

    // Warna teks gelap untuk di atas isi berwarna: versi gelap dari warna yang sama, bukan hitam polos.
    public static Color DarkOf(Color fill)
    {
        return Color.Lerp(fill, Ink, 0.8f);
    }

    // Stiker label bertinta (cap ancaman, label status, chip build). Lebar mengikuti teks.
    // Harus diletakkan di dalam HorizontalLayoutGroup supaya tidak ikut melebar.
    public static Image CreateStamp(string name, Transform parent, float fontSize, float tilt, out TextMeshProUGUI label)
    {
        Image stamp = SetCorners(CreateImage(name, parent, Cream), InkMedium);
        AddHardShadow(stamp, new Vector2(3f, -4f));
        AddHorizontal(stamp.gameObject, 0f, new RectOffset(12, 12, 4, 6), TextAnchor.MiddleCenter);
        stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);

        label = UseDisplayFont(CreateText("Label", stamp.transform, string.Empty, fontSize, Ink, TextAlignmentOptions.Center, true));
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return stamp;
    }

    // Stiker yang diletakkan bebas (bukan di dalam layout group), mis. logo judul. Ukurannya mengikuti
    // teks lewat ContentSizeFitter, posisinya diatur pemanggil.
    public static Image CreateFreeStamp(string name, Transform parent, string text, float fontSize, Color fill, float tilt, out TextMeshProUGUI label)
    {
        Image stamp = CreateStamp(name, parent, fontSize, tilt, out label);
        SetStamp(stamp, label, text, fill);

        ContentSizeFitter fitter = stamp.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return stamp;
    }

    // Mengubah jarak bayangan keras yang sudah dipasang, mis. bayangan lebih jauh untuk stiker besar.
    public static void SetShadowDistance(Graphic graphic, Vector2 distance)
    {
        Shadow shadow = graphic.GetComponent<Shadow>();

        if (shadow != null)
            shadow.effectDistance = distance;
    }

    public static void SetStamp(Image stamp, TextMeshProUGUI label, string text, Color fill)
    {
        stamp.color = fill;
        label.text = text;
        label.color = DarkOf(fill);
    }

    // Token bulat bertinta berisi huruf atau angka (token ronde, token musuh).
    public static Image CreateToken(string name, Transform parent, float size, Color fill, string text, out TextMeshProUGUI label)
    {
        Image token = SetCircle(CreateImage(name, parent, fill));
        Layout(token, size, size, 0f);

        label = UseDisplayFont(CreateText("Label", token.transform, text, size * 0.55f, DarkOf(fill), TextAlignmentOptions.Center, true));
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(label.rectTransform, 0f, 0f, 0f, size * 0.06f);
        return token;
    }

    // Singkatan stat untuk chip build, mis. "÷2 DMG".
    public static string StatShort(StatType stat)
    {
        switch (stat)
        {
            case StatType.MaxHP: return "HP";
            case StatType.Damage: return "DMG";
            case StatType.AttackRate: return "ASPD";
            case StatType.Range: return "RNG";
            case StatType.Defense: return "DEF";
            case StatType.MoveSpeed: return "SPD";
            default: return stat.ToString();
        }
    }

    // Bayangan keras (offset tanpa blur) di belakang sebuah Graphic, ciri gaya stiker/komik.
    public static Shadow AddHardShadow(Graphic graphic, Vector2 distance)
    {
        Shadow shadow = graphic.gameObject.AddComponent<Shadow>();
        shadow.effectColor = Ink;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
        return shadow;
    }

    private static Sprite CreateInkSprite(string spriteName, int size, float radiusTop, float radiusBottom,
        int inkTop, int inkSides, int inkBottom, bool sliced)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = spriteName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        Color32[] pixels = new Color32[size * size];
        float innerTop = Mathf.Max(0f, radiusTop - Mathf.Max(inkTop, inkSides));
        float innerBottom = Mathf.Max(0f, radiusBottom - Mathf.Max(inkBottom, inkSides));

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                // Tekstur y = 0 ada di bawah, jadi "atas" adalah y besar.
                float outer = BoxCoverage(px, py, 0f, 0f, size, size, radiusTop, radiusBottom);
                float inner = BoxCoverage(px, py, inkSides, inkBottom, size - inkSides, size - inkTop, innerTop, innerBottom);

                byte value = (byte)Mathf.RoundToInt(inner * 255f);
                byte alpha = (byte)Mathf.RoundToInt(outer * 255f);
                pixels[y * size + x] = new Color32(value, value, value, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        Vector4 border = Vector4.zero;

        if (sliced)
        {
            float side = Mathf.Ceil(Mathf.Max(Mathf.Max(radiusTop, radiusBottom), inkSides)) + 1f;
            float top = Mathf.Ceil(Mathf.Max(radiusTop, inkTop)) + 1f;
            float bottom = Mathf.Ceil(Mathf.Max(radiusBottom, inkBottom)) + 1f;
            border = new Vector4(side, bottom, side, top);
        }

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, border);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    // Cakupan piksel oleh kotak membulat (signed distance), dengan radius atas dan bawah terpisah.
    private static float BoxCoverage(float px, float py, float x0, float y0, float x1, float y1, float radiusTop, float radiusBottom)
    {
        float cx = (x0 + x1) * 0.5f;
        float cy = (y0 + y1) * 0.5f;
        float r = py >= cy ? radiusTop : radiusBottom;
        float qx = Mathf.Abs(px - cx) - ((x1 - x0) * 0.5f - r);
        float qy = Mathf.Abs(py - cy) - ((y1 - y0) * 0.5f - r);
        float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
        float distance = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        return Mathf.Clamp01(0.5f - distance);
    }

    private static Sprite roundedOutline;

    // Garis tepi kotak membulat (tengah transparan), dipakai bingkai fokus tombol.
    public static Sprite RoundedOutline
    {
        get
        {
            if (roundedOutline == null)
                // Radius tombol (8) + jarak bingkai (6), supaya bingkai sejajar dengan sudut tombol.
                roundedOutline = CreateRoundedOutlineSprite(64, 14, 3);

            return roundedOutline;
        }
    }

    private static Sprite CreateRoundedOutlineSprite(int size, int radius, int thickness)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "UIKit_RoundedOutline",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        Color32[] pixels = new Color32[size * size];
        int innerRadius = radius - thickness;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                // Cakupan kotak luar dikurangi cakupan kotak dalam (diinset setebal garis).
                float ox = Mathf.Clamp(px, radius, size - radius);
                float oy = Mathf.Clamp(py, radius, size - radius);
                float outer = Mathf.Clamp01(radius - Vector2.Distance(new Vector2(px, py), new Vector2(ox, oy)) + 0.5f);

                float ix = Mathf.Clamp(px, thickness + innerRadius, size - thickness - innerRadius);
                float iy = Mathf.Clamp(py, thickness + innerRadius, size - thickness - innerRadius);
                float inner = Mathf.Clamp01(innerRadius - Vector2.Distance(new Vector2(px, py), new Vector2(ix, iy)) + 0.5f);

                byte alpha = (byte)Mathf.RoundToInt(outer * (1f - inner) * 255f);
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

        sprite.name = "UIKit_RoundedOutline";
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
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

    private static Sprite CreateRoundedSprite(string spriteName, int size, int radius)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = spriteName,
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

        sprite.name = spriteName;
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

    // Panel bertinta. raised = true menambah bayangan keras (panel yang melayang di atas arena);
    // false untuk panel di dalam panel lain.
    public static Image CreatePanel(string name, Transform parent, Color color, bool raised = true)
    {
        Image image = SetCorners(CreateImage(name, parent, color), InkPanel);

        if (raised)
            AddHardShadow(image, PanelShadow);

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
        Image background = SetCorners(CreateImage(name, parent, Color.white), InkMedium);
        background.raycastTarget = true;
        AddHardShadow(background, ButtonShadow);

        Button button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;

        Color baseColor = primary ? Accent : PanelLight;
        ColorBlock colors = button.colors;
        colors.normalColor = baseColor;
        colors.highlightedColor = primary ? AccentHighlight : new Color(0.34f, 0.40f, 0.53f, 1f);
        // Fokus ditandai bingkai (UIFocusFrame), bukan warna hover, supaya tombol yang dipilih keyboard
        // tidak tertukar dengan tombol yang sedang disentuh mouse.
        colors.selectedColor = baseColor;
        colors.pressedColor = primary ? AccentDark : new Color(0.1f, 0.12f, 0.16f, 1f);
        colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        if (onClick != null)
            button.onClick.AddListener(onClick);

        // Label tombol memakai font display, sesuai mockup menu gaya stiker.
        TextMeshProUGUI text = UseDisplayFont(CreateText("Label", background.transform, label, 26f,
            primary ? new Color(0.12f, 0.09f, 0.02f) : Text, TextAlignmentOptions.Center, true));
        text.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(text.rectTransform, 12f, 12f, 4f, 4f);

        // Bingkai fokus di luar tepi tombol, hanya tampil saat tombol dipilih.
        Image frame = CreateImage("FocusFrame", background.transform, Focus, false);
        frame.sprite = RoundedOutline;
        frame.type = Image.Type.Sliced;
        Stretch(frame.rectTransform, -6f, -6f, -6f, -6f);
        background.gameObject.AddComponent<UIFocusFrame>().Bind(frame);

        // Suara klik saat ditekan.
        background.gameObject.AddComponent<UIButtonFeedback>();

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

    // Garis tinta pemisah di dalam VerticalLayoutGroup, sama dengan garis tepi panel.
    public static Image CreateDivider(Transform parent)
    {
        Image line = CreateImage("Divider", parent, Ink, false);
        Layout(line, 3f);
        return line;
    }

    // Bar horizontal: latar + isi yang lebarnya diatur lewat SetBarFill.
    public static RectTransform CreateBar(string name, Transform parent, Color background, Color fill, out Image fillImage)
    {
        Image back = SetCorners(CreateImage(name, parent, background), RoundedSmall);
        fillImage = SetCorners(CreateImage("Fill", back.transform, fill), RoundedSmall);
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
