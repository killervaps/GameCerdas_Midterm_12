using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

// Ikon tombol gamepad bergaya Xbox: A/B/X/Y lingkaran gelap berhuruf warna, bumper LB/RB, trigger LT/RT,
// Menu (tiga garis), View, stik L/R, dan D-pad. Gambarnya ada di satu atlas (Fonts/Resources/ControllerGlyphs.png).
// Dipakai dua cara:
//   - Di dalam teks TMP lewat <sprite name="Y"> (Tag). Label harus memakai sprite asset ini (Apply).
//   - Sebagai Sprite untuk Image (Get), mis. lencana Aura Burst di HUD dan tombol di Pengaturan.
public static class ControlGlyphs
{
    private const string TextureResource = "ControllerGlyphs";

    // Bagian bawah ikon sedikit di bawah garis dasar teks supaya terlihat sejajar dengan huruf.
    private const float BaselineRatio = 0.82f;
    // Ikon sedikit lebih tinggi dari huruf kapital.
    private const float TextScale = 1.2f;

    private readonly struct Entry
    {
        public readonly string Name;
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public Entry(string name, int x, int y, int width, int height)
        {
            Name = name;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    // Posisi tiap ikon di atlas (piksel, y dari bawah). Dihasilkan bersama ControllerGlyphs.png.
    private static readonly Entry[] Table =
    {
        new Entry("A", 8, 392, 112, 112),
        new Entry("B", 128, 392, 112, 112),
        new Entry("X", 248, 392, 112, 112),
        new Entry("Y", 368, 392, 112, 112),
        new Entry("LB", 488, 392, 156, 112),
        new Entry("RB", 652, 392, 156, 112),
        new Entry("LT", 816, 392, 120, 112),
        new Entry("RT", 8, 272, 120, 112),
        new Entry("Menu", 136, 272, 112, 112),
        new Entry("View", 256, 272, 112, 112),
        new Entry("LS", 376, 272, 112, 112),
        new Entry("RS", 496, 272, 112, 112),
        new Entry("Dpad", 616, 272, 112, 112),
        new Entry("DpadUp", 736, 272, 112, 112),
        new Entry("DpadDown", 856, 272, 112, 112),
        new Entry("DpadLeft", 8, 152, 112, 112),
        new Entry("DpadRight", 128, 152, 112, 112),
    };

    private static Texture2D texture;
    private static TMP_SpriteAsset asset;
    private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    private static bool failed;

    public static TMP_SpriteAsset Asset
    {
        get
        {
            Load();
            return asset;
        }
    }

    // Nama ikon untuk kontrol gamepad (bagian path sesudah layout), mis. "buttonNorth" -> "Y". Null jika tidak ada ikonnya.
    public static string ForControl(string control)
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
            case "dpad": return "Dpad";
            case "dpad/up": return "DpadUp";
            case "dpad/down": return "DpadDown";
            case "dpad/left": return "DpadLeft";
            case "dpad/right": return "DpadRight";
            default: return null;
        }
    }

    public static bool Has(string name)
    {
        return !string.IsNullOrEmpty(name) && Asset != null && sprites.ContainsKey(name);
    }

    // Tag rich text TMP untuk ikon ini.
    public static string Tag(string name)
    {
        return "<sprite name=\"" + name + "\">";
    }

    // Sprite untuk Image. Null jika atlas belum ada.
    public static Sprite Get(string name)
    {
        Load();
        return name != null && sprites.TryGetValue(name, out Sprite sprite) ? sprite : null;
    }

    // Memasang sprite asset ikon ke label supaya tag <sprite name=...> bisa digambar.
    public static void Apply(TMP_Text text)
    {
        if (text == null)
            return;

        TMP_SpriteAsset spriteAsset = Asset;

        if (spriteAsset != null && text.spriteAsset != spriteAsset)
            text.spriteAsset = spriteAsset;
    }

    private static void Load()
    {
        // Objek DontSave bertahan antar sesi Play (Domain Reload mati), jadi cukup dibuat sekali.
        if (asset != null || failed)
            return;

        texture = Resources.Load<Texture2D>(TextureResource);

        if (texture == null)
        {
            failed = true;
            Debug.LogWarning("ControlGlyphs: tekstur '" + TextureResource + "' tidak ditemukan di folder Resources. Petunjuk gamepad memakai teks.");
            return;
        }

        Material material = CreateMaterial(texture);

        if (material == null)
        {
            failed = true;
            Debug.LogWarning("ControlGlyphs: shader 'TextMeshPro/Sprite' tidak ditemukan. Petunjuk gamepad memakai teks.");
            return;
        }

        TMP_SpriteAsset spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        spriteAsset.name = "ControllerGlyphs";
        spriteAsset.hideFlags = HideFlags.DontSave;
        spriteAsset.spriteSheet = texture;
        // Daftar lama dikosongkan dan versi diisi, supaya TMP tidak menganggap aset ini format lama lalu mengosongkan tabelnya.
        spriteAsset.spriteInfoList = new List<TMP_Sprite>();
        MarkCurrentVersion(spriteAsset);

        sprites.Clear();

        for (int i = 0; i < Table.Length; i++)
        {
            Entry entry = Table[i];

            GlyphMetrics metrics = new GlyphMetrics(entry.Width, entry.Height, 2f, entry.Height * BaselineRatio, entry.Width + 4f);
            TMP_SpriteGlyph glyph = new TMP_SpriteGlyph((uint)i, metrics, new GlyphRect(entry.X, entry.Y, entry.Width, entry.Height), 1f, 0);
            spriteAsset.spriteGlyphTable.Add(glyph);

            TMP_SpriteCharacter character = new TMP_SpriteCharacter(0xE000u + (uint)i, glyph)
            {
                name = entry.Name,
                scale = TextScale
            };
            spriteAsset.spriteCharacterTable.Add(character);

            Sprite sprite = Sprite.Create(texture, new Rect(entry.X, entry.Y, entry.Width, entry.Height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "Glyph_" + entry.Name;
            sprite.hideFlags = HideFlags.DontSave;
            sprites[entry.Name] = sprite;
        }

        spriteAsset.material = material;
        spriteAsset.UpdateLookupTables();
        asset = spriteAsset;
    }

    private static Material CreateMaterial(Texture2D sheet)
    {
        // Salin material sprite bawaan TMP supaya shader-nya pasti ikut di build.
        Material source = TMP_Settings.defaultSpriteAsset != null ? TMP_Settings.defaultSpriteAsset.material : null;
        Material material;

        if (source != null)
        {
            material = new Material(source);
        }
        else
        {
            Shader shader = Shader.Find("TextMeshPro/Sprite");

            if (shader == null)
                return null;

            material = new Material(shader);
        }

        material.name = "ControllerGlyphs Material";
        material.hideFlags = HideFlags.DontSave;
        material.SetTexture(ShaderUtilities.ID_MainTex, sheet);
        return material;
    }

    private static void MarkCurrentVersion(TMP_SpriteAsset spriteAsset)
    {
        FieldInfo field = typeof(TMP_Asset).GetField("m_Version", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(spriteAsset, "1.1.0");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        failed = false;
    }
}
