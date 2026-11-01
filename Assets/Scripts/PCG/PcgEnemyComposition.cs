using System.Collections.Generic;

// Tahap pertama PCG musuh: menentukan role apa saja dan berapa banyak, sebelum ada posisi.
// Dijalankan di Safezone supaya kartu gate dan preview musuh sudah tahu wave berikutnya.
//   1. jumlah total = acak di [Min Enemies, Max Enemies]
//   2. isi jatah Min tiap role
//   3. selama belum mencapai jumlah total: undi role dengan weighted random, role yang sudah Max dilewati
//   4. jika semua role sudah Max, berhenti lebih awal (jumlah total tidak dipaksakan)
// Arena yang sudah terpilih boleh memberi bias (ArenaRoleBias): bobot dikali dan batas Max ditambah untuk role tertentu,
// jadi komposisi menyesuaikan tema arena (PCG yang sadar konteks).
public static class PcgEnemyComposition
{
    private class Slot : IWeightedEntry
    {
        public EnemySpawnEntry entry;
        public int count;
        public int weight;
        public int max;

        public int Weight => weight;

        public bool IsSelectable => count < max;
    }

    public static WaveEntry[] Generate(EnemyRoundProfile profile, SeededRandom random, IReadOnlyList<ArenaRoleBias> bias = null)
    {
        List<Slot> slots = new List<Slot>();
        int total = 0;

        if (profile != null && profile.roles != null)
        {
            foreach (EnemySpawnEntry entry in profile.roles)
            {
                if (entry == null)
                    continue;

                Slot slot = new Slot { entry = entry, weight = BiasedWeight(entry, bias), max = BiasedMax(entry, bias) };
                slot.count = System.Math.Min(entry.min, slot.max);
                slots.Add(slot);
                total += slot.count;
            }
        }

        int minEnemies = profile != null ? profile.minEnemies : 0;
        int maxEnemies = profile != null ? System.Math.Max(profile.minEnemies, profile.maxEnemies) : 0;

        // Undian jumlah selalu dilakukan supaya urutan angka acak sama walau jatah minimum diubah.
        int target = System.Math.Max(total, random.Range(minEnemies, maxEnemies + 1));

        while (total < target)
        {
            int index = WeightedSelection.PickIndex(slots, random);

            if (index < 0)
                break;

            slots[index].count++;
            total++;
        }

        return Merge(slots);
    }

    public static int BiasedWeight(EnemySpawnEntry entry, IReadOnlyList<ArenaRoleBias> bias)
    {
        if (entry.weight <= 0 || !TryGetBias(entry.role, bias, out ArenaRoleBias found))
            return entry.weight;

        if (found.weightMultiplier <= 0f)
            return 0;

        // Bobot asli tidak pernah turun ke 0 hanya karena pembulatan.
        return System.Math.Max(1, (int)System.Math.Round(entry.weight * found.weightMultiplier));
    }

    public static int BiasedMax(EnemySpawnEntry entry, IReadOnlyList<ArenaRoleBias> bias)
    {
        if (entry.max <= 0 || !TryGetBias(entry.role, bias, out ArenaRoleBias found))
            return entry.max;

        return entry.max + found.maxBonus;
    }

    private static bool TryGetBias(EnemyRole role, IReadOnlyList<ArenaRoleBias> bias, out ArenaRoleBias found)
    {
        if (bias != null)
        {
            for (int i = 0; i < bias.Count; i++)
            {
                if (bias[i] != null && bias[i].role == role)
                {
                    found = bias[i];
                    return true;
                }
            }
        }

        found = null;
        return false;
    }

    // Satu baris per role, urut sesuai enum, supaya preview dan log rapi walau tabel punya role ganda.
    private static WaveEntry[] Merge(List<Slot> slots)
    {
        Dictionary<EnemyRole, int> counts = new Dictionary<EnemyRole, int>();

        foreach (Slot slot in slots)
        {
            if (slot.count <= 0)
                continue;

            counts.TryGetValue(slot.entry.role, out int current);
            counts[slot.entry.role] = current + slot.count;
        }

        List<WaveEntry> result = new List<WaveEntry>();

        foreach (EnemyRole role in System.Enum.GetValues(typeof(EnemyRole)))
        {
            if (counts.TryGetValue(role, out int count))
                result.Add(new WaveEntry(role, count));
        }

        return result.ToArray();
    }
}
