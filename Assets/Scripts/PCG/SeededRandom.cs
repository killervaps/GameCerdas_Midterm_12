using UnityEngine;

// Aliran angka acak ber-seed milik PCG.
// Sengaja tidak memakai UnityEngine.Random: AI, gate, dan pemilihan arena juga memanggil Random global setiap saat,
// sehingga Random.InitState(seed) tidak menjamin hasil spawn yang sama. Setiap aliran di sini punya generator sendiri,
// jadi seed sama + parameter sama selalu menghasilkan urutan angka yang sama (reproducible).
public enum PcgStream
{
    EnemyComposition = 1,
    EnemyPlacement = 2,
    ItemSelection = 3,
    ItemPlacement = 4,
    Arena = 5,
    Preview = 99
}

public class SeededRandom
{
    private readonly System.Random random;

    public int Seed { get; }

    public SeededRandom(int seed)
    {
        Seed = seed;
        random = new System.Random(seed);
    }

    // Seed turunan untuk satu ronde dan satu aliran, dari seed run. Ronde 2 tidak memakai urutan angka ronde 1,
    // dan urutan posisi musuh tidak bergeser walau jumlah item diubah.
    public static int Derive(int runSeed, int round, PcgStream stream)
    {
        unchecked
        {
            uint h = (uint)runSeed;
            h = Mix(h ^ ((uint)round * 0x9E3779B9u));
            h = Mix(h ^ ((uint)stream * 0x85EBCA6Bu));
            return (int)(h & 0x7FFFFFFF);
        }
    }

    // Integer acak di [min, max). Sama dengan Random.Range versi int.
    public int Range(int minInclusive, int maxExclusive)
    {
        return maxExclusive <= minInclusive ? minInclusive : random.Next(minInclusive, maxExclusive);
    }

    // Float acak di [min, max].
    public float Range(float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    // Float acak di [0, 1).
    public float Value => (float)random.NextDouble();

    public Vector2 InsideRect(Vector2 size)
    {
        return new Vector2(Range(-size.x * 0.5f, size.x * 0.5f), Range(-size.y * 0.5f, size.y * 0.5f));
    }

    // Pengaduk bit (finalizer MurmurHash3) supaya seed yang berdekatan menghasilkan seed turunan yang jauh berbeda.
    private static uint Mix(uint h)
    {
        unchecked
        {
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }
    }
}
