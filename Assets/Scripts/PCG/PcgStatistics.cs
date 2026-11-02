using System.Collections.Generic;
using System.Text;

// Challenge 6: statistik spawn.
// Mensimulasikan banyak undian musuh tanpa memunculkan apa pun, lalu menghitung hasil per role.
//   Harapan    bobot role / total bobot (setelah bias arena). Peluang teoretis.
//   Murni      setiap musuh diundi langsung dengan weighted random, tanpa Min/Max.
//              Makin besar sampel (100 -> 500 -> 1000), makin dekat ke Harapan (law of large numbers).
//   Generator  komposisi ronde dibuat berulang dengan PcgEnemyComposition (jumlah total, Min, Max, bias arena)
//              sampai jumlah musuh tercapai. Boleh menyimpang dari bobot karena aturan Min/Max ikut membentuk hasil.
// Memakai SeededRandom sendiri, jadi tidak mengganggu run yang sedang berjalan dan hasilnya bisa diulang.
public static class PcgStatistics
{
    public class RoleRow
    {
        public EnemyRole role;
        public int weight;
        public float expected;
        public int pure;
        public int generator;
    }

    public class Result
    {
        public readonly List<RoleRow> rows = new List<RoleRow>();
        public string label;
        public int sampleSize;
        public int seed;
        public int pureTotal;
        public int generatorTotal;
        public int compositions;
        // Difficulty Budget profil (0 = mati) dan total biaya semua komposisi Generator.
        public int budget;
        public int totalCost;
        public int minCost = int.MaxValue;
        public int maxCost;

        public float AverageCost => compositions > 0 ? (float)totalCost / compositions : 0f;

        public float PurePercent(RoleRow row) => pureTotal > 0 ? (float)row.pure / pureTotal : 0f;

        public float GeneratorPercent(RoleRow row) => generatorTotal > 0 ? (float)row.generator / generatorTotal : 0f;

        // Tabel teks untuk Console, mudah disalin ke laporan.
        public string ToTable()
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("PCG statistik ").Append(label).Append(", sampel ").Append(sampleSize)
                .Append(", seed ").Append(seed).Append('\n');
            builder.Append("Role\tBobot\tHarapan\tMurni\tGenerator\n");

            foreach (RoleRow row in rows)
            {
                builder.Append(row.role).Append('\t')
                    .Append(row.weight).Append('\t')
                    .Append(Percent(row.expected)).Append('\t')
                    .Append(Percent(PurePercent(row))).Append(" (").Append(row.pure).Append(")\t")
                    .Append(Percent(GeneratorPercent(row))).Append(" (").Append(row.generator).Append(")\n");
            }

            builder.Append("Murni ").Append(pureTotal).Append(" musuh, Generator ").Append(generatorTotal)
                .Append(" musuh dari ").Append(compositions).Append(" komposisi\n");
            builder.Append("Biaya per komposisi: rata-rata ").Append(AverageCost.ToString("0.0"))
                .Append(", min ").Append(compositions > 0 ? minCost : 0).Append(", max ").Append(maxCost)
                .Append(budget > 0 ? ", budget " + budget : ", budget mati");

            return builder.ToString();
        }
    }

    private class WeightOnly : IWeightedEntry
    {
        public int weight;

        public int Weight => weight;

        public bool IsSelectable => true;
    }

    public static string Percent(float value)
    {
        return (value * 100f).ToString("0.0") + "%";
    }

    public static Result Simulate(EnemyRoundProfile profile, IReadOnlyList<ArenaRoleBias> bias, int sampleSize, int seed, string label,
        IReadOnlyList<RoleCost> costs = null)
    {
        Result result = new Result { sampleSize = sampleSize, seed = seed, label = label };
        result.budget = profile != null ? profile.difficultyBudget : 0;

        if (profile == null || profile.roles == null || sampleSize <= 0)
            return result;

        // Satu baris per role, urut enum. Role ganda di tabel bobotnya dijumlahkan.
        Dictionary<EnemyRole, RoleRow> byRole = new Dictionary<EnemyRole, RoleRow>();

        foreach (EnemySpawnEntry entry in profile.roles)
        {
            if (entry == null)
                continue;

            RoleRow row = GetRow(byRole, entry.role);
            row.weight += PcgEnemyComposition.BiasedWeight(entry, bias);
        }

        foreach (EnemyRole role in System.Enum.GetValues(typeof(EnemyRole)))
        {
            if (byRole.TryGetValue(role, out RoleRow row))
                result.rows.Add(row);
        }

        int totalWeight = 0;

        foreach (RoleRow row in result.rows)
            totalWeight += row.weight;

        foreach (RoleRow row in result.rows)
            row.expected = totalWeight > 0 ? (float)row.weight / totalWeight : 0f;

        // Murni: satu undian weighted random per musuh.
        List<WeightOnly> entries = new List<WeightOnly>();

        foreach (RoleRow row in result.rows)
            entries.Add(new WeightOnly { weight = row.weight });

        SeededRandom pureRandom = new SeededRandom(seed);

        for (int i = 0; i < sampleSize; i++)
        {
            int index = WeightedSelection.PickIndex(entries, pureRandom);

            if (index < 0)
                break;

            result.rows[index].pure++;
            result.pureTotal++;
        }

        // Generator: komposisi ronde lengkap dibuat berulang sampai jumlah musuh tercapai.
        SeededRandom generatorRandom = new SeededRandom(seed ^ 0x5bd1e995);
        int guard = sampleSize * 4;

        while (result.generatorTotal < sampleSize && guard-- > 0)
        {
            WaveEntry[] wave = PcgEnemyComposition.Generate(profile, generatorRandom, bias, costs, out int cost);
            int added = 0;

            result.compositions++;
            result.totalCost += cost;
            result.minCost = System.Math.Min(result.minCost, cost);
            result.maxCost = System.Math.Max(result.maxCost, cost);

            foreach (WaveEntry entry in wave)
            {
                if (entry.count <= 0)
                    continue;

                if (!byRole.ContainsKey(entry.role))
                    result.rows.Add(GetRow(byRole, entry.role));

                byRole[entry.role].generator += entry.count;
                added += entry.count;
            }

            result.generatorTotal += added;

            if (added == 0)
                break;
        }

        return result;
    }

    private static RoleRow GetRow(Dictionary<EnemyRole, RoleRow> byRole, EnemyRole role)
    {
        if (!byRole.TryGetValue(role, out RoleRow row))
        {
            row = new RoleRow { role = role };
            byRole[role] = row;
        }

        return row;
    }
}
