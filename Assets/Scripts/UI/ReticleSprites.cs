using System;
using UnityEngine;

// Sprite procedural untuk crosshair lock-on. Semua putih (diwarnai lewat Image.color) dan punya versi "ink"
// yang lebih tebal untuk garis tinta gelap di belakangnya, sesuai gaya UI stiker.
// Dibuat sekali saat pertama dipakai, tanpa aset gambar.
public static class ReticleSprites
{
    private const int Size = 64;
    // Tebal lengan siku dan tebal garis tinta di sekelilingnya, dalam piksel tekstur.
    private const float Thickness = 9f;
    private const float Border = 4f;

    private static Sprite corner;
    private static Sprite cornerInk;
    private static Sprite dashedRing;
    private static Sprite dashedRingInk;
    private static Sprite triangle;
    private static Sprite triangleInk;
    private static Sprite dottedRing;
    private static Sprite longDashRing;
    private static Sprite diamond;
    private static Sprite diamondInk;

    // Siku di pojok kiri atas: lengan tegak di kiri dan lengan datar di atas. Diputar untuk pojok lain.
    public static Sprite Corner => corner != null ? corner : corner = CreateCorner("Reticle_Corner", Border, Thickness);

    public static Sprite CornerInk => cornerInk != null ? cornerInk : cornerInk = CreateCorner("Reticle_CornerInk", 0f, Thickness + Border * 2f);

    // Cincin putus-putus delapan ruas (crosshair lock).
    public static Sprite DashedRing => dashedRing != null ? dashedRing : dashedRing = CreateRing("Reticle_Ring", 0.84f, 0.97f, 8, 0.62f);

    public static Sprite DashedRingInk => dashedRingInk != null ? dashedRingInk : dashedRingInk = CreateRing("Reticle_RingInk", 0.79f, 1f, 8, 0.7f);

    // Sigil auto-target di kaki musuh: cincin luar titik-titik dan cincin dalam putus panjang.
    public static Sprite DottedRing => dottedRing != null ? dottedRing : dottedRing = CreateRing("Reticle_DottedRing", 0.93f, 1f, 28, 0.28f);

    public static Sprite LongDashRing => longDashRing != null ? longDashRing : longDashRing = CreateRing("Reticle_LongDashRing", 0.81f, 0.86f, 6, 0.7f);

    // Permata kecil (belah ketupat) di cincin sigil.
    public static Sprite Diamond => diamond != null ? diamond : diamond = CreateDiamond("Reticle_Diamond", 0f);

    public static Sprite DiamondInk => diamondInk != null ? diamondInk : diamondInk = CreateDiamond("Reticle_DiamondInk", Border);

    // Segitiga yang menunjuk ke bawah. Diputar supaya menunjuk ke tengah crosshair.
    public static Sprite Triangle => triangle != null ? triangle : triangle = CreateTriangle("Reticle_Triangle", 0f);

    public static Sprite TriangleInk => triangleInk != null ? triangleInk : triangleInk = CreateTriangle("Reticle_TriangleInk", Border);

    private static Sprite CreateCorner(string name, float inset, float thickness)
    {
        float far = Size - inset;

        return Create(name, Size, (x, y) =>
        {
            // Koordinat dengan y dari atas, supaya lengan datar ada di baris atas.
            float top = Size - y;

            float vertical = Mathf.Min(Edge(x - inset), Edge(inset + thickness - x), Edge(top - inset), Edge(far - top));
            float horizontal = Mathf.Min(Edge(top - inset), Edge(inset + thickness - top), Edge(x - inset), Edge(far - x));

            return Mathf.Max(vertical, horizontal);
        });
    }

    // Belah ketupat di tengah tekstur. Versi ink diperbesar 'grow' piksel ke segala arah.
    private static Sprite CreateDiamond(string name, float grow)
    {
        float center = Size * 0.5f;
        float radius = Size * 0.36f;

        return Create(name, Size, (x, y) =>
        {
            // Jarak Manhattan dibagi akar 2 supaya tepi halus selebar satu piksel di sisi miring.
            float d = (Mathf.Abs(x - center) + Mathf.Abs(y - center) - radius) / 1.4142f;
            return Edge(grow - d);
        });
    }

    private static Sprite CreateRing(string name, float inner, float outer, int dashes, float dashFill)
    {
        return Create(name, Size * 2, (x, y) =>
        {
            float dx = (x - Size) / Size;
            float dy = (y - Size) / Size;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float pixel = 1f / Size;

            float band = Mathf.Min(Mathf.Clamp01((r - inner) / pixel + 0.5f), Mathf.Clamp01((outer - r) / pixel + 0.5f));

            // Posisi di dalam satu ruas (0-1). Ruas terisi sebagian, sisanya celah.
            float angle = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.5f;
            float segment = Mathf.Repeat(angle * dashes + dashFill * 0.5f, 1f);
            float arcPixels = r * Mathf.PI * 2f * Size / dashes;
            float dash = Mathf.Min(Mathf.Clamp01(segment * arcPixels + 0.5f), Mathf.Clamp01((dashFill - segment) * arcPixels + 0.5f));

            return band * dash;
        });
    }

    private static Sprite CreateTriangle(string name, float grow)
    {
        // Titik sudut dalam piksel (y ke atas). Ujung runcing di bawah.
        Vector2 a = new Vector2(10f, 52f);
        Vector2 b = new Vector2(54f, 52f);
        Vector2 c = new Vector2(32f, 14f);

        return Create(name, Size, (x, y) =>
        {
            Vector2 p = new Vector2(x, y);
            float d = Mathf.Min(EdgeDistance(a, b, p), Mathf.Min(EdgeDistance(b, c, p), EdgeDistance(c, a, p)));
            return Edge(d + grow);
        });
    }

    // Jarak bertanda ke garis dari 'from' ke 'to'. Positif di sisi dalam segitiga (titik sudut urut searah jarum jam).
    private static float EdgeDistance(Vector2 from, Vector2 to, Vector2 p)
    {
        Vector2 edge = to - from;
        Vector2 normal = new Vector2(edge.y, -edge.x).normalized;
        return Vector2.Dot(p - from, normal);
    }

    // Tepi halus selebar satu piksel untuk jarak bertanda d (piksel).
    private static float Edge(float d)
    {
        return Mathf.Clamp01(d + 0.5f);
    }

    private static Sprite Create(string name, int size, Func<float, float, float> alpha)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float a = Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = name;
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }
}
