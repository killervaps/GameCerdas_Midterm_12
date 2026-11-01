using System.Collections.Generic;

// Entri tabel weighted random. Bobot 0 atau entri tanpa isi tidak pernah terpilih.
public interface IWeightedEntry
{
    int Weight { get; }

    bool IsSelectable { get; }
}

// Weighted random: bobot dijumlahkan, satu angka diacak di [0, total), lalu dicari entri yang memuat angka itu.
//   Contoh bobot 60 : 30 : 10  ->  angka 0-59 entri A, 60-89 entri B, 90-99 entri C.
// Sama dengan SelectWeightedPrefab di modul praktikum, tetapi memakai SeededRandom dan bisa untuk tabel apa pun.
public static class WeightedSelection
{
    // Index entri terpilih, atau -1 jika tidak ada entri yang bisa dipilih.
    public static int PickIndex<T>(IReadOnlyList<T> entries, SeededRandom random) where T : IWeightedEntry
    {
        int total = TotalWeight(entries);

        if (total <= 0)
            return -1;

        int roll = random.Range(0, total);
        int cumulative = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            T entry = entries[i];

            if (entry == null || !entry.IsSelectable || entry.Weight <= 0)
                continue;

            cumulative += entry.Weight;

            if (roll < cumulative)
                return i;
        }

        return -1;
    }

    public static int TotalWeight<T>(IReadOnlyList<T> entries) where T : IWeightedEntry
    {
        int total = 0;

        if (entries == null)
            return 0;

        for (int i = 0; i < entries.Count; i++)
        {
            T entry = entries[i];

            if (entry != null && entry.IsSelectable && entry.Weight > 0)
                total += entry.Weight;
        }

        return total;
    }

    // Peluang teoretis entri ini (0-1), untuk debug panel dan tabel eksperimen.
    public static float Chance<T>(IReadOnlyList<T> entries, int index) where T : IWeightedEntry
    {
        int total = TotalWeight(entries);

        if (total <= 0 || index < 0 || index >= entries.Count)
            return 0f;

        T entry = entries[index];
        return entry != null && entry.IsSelectable && entry.Weight > 0 ? (float)entry.Weight / total : 0f;
    }
}
