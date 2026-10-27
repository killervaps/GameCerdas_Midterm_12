using System.Collections.Generic;
using UnityEngine;

// Mengacak pilihan gate dari pool tanpa duplikat dalam satu roll.
// Tiap slot diacak dua tahap: rarity dulu sesuai peluangnya, lalu satu gate acak dari rarity itu.
// Jadi peluang di Inspector adalah peluang rarity per slot, berapa pun jumlah gate di rarity tersebut.
// Rarity yang tidak punya kandidat dilewati dan peluangnya dibagi ke rarity lain.
public class GateGenerator : MonoBehaviour
{
    [Header("Pool")]
    [SerializeField] private MathGateData[] pool;

    [Header("Rarity Chance per Slot (%)")]
    // Total tidak harus 100. Nilai dinormalisasi terhadap rarity yang punya kandidat.
    [SerializeField] private float commonChance = 50f;
    [SerializeField] private float rareChance = 25f;
    [SerializeField] private float veryRareChance = 13f;
    [SerializeField] private float epicChance = 7f;
    [SerializeField] private float legendaryChance = 3.5f;
    [SerializeField] private float sigmaChance = 1.5f;

    [Header("Rules")]
    // Gate yang ditawarkan di roll sebelumnya didahulukan untuk dihindari.
    // Jika pool tidak cukup, gate lama tetap boleh muncul lagi.
    [SerializeField] private bool avoidPreviousOffer = true;
    // Gate tanpa buff atau tanpa trade-off tidak ikut diacak.
    [SerializeField] private bool skipInvalidGates = true;

    [Header("Debug")]
    [SerializeField] private bool logRolls = true;

    private readonly List<MathGateData> candidates = new List<MathGateData>();
    private readonly List<MathGateData> previousOffer = new List<MathGateData>();
    private readonly int[] candidateCount = new int[GateRarityStyle.Count];

    private bool hasForcedRarity;
    private GateRarity forcedRarity;

    public IReadOnlyList<MathGateData> Pool => pool;

    public bool HasForcedRarity => hasForcedRarity;

    public GateRarity ForcedRarity => forcedRarity;

    // Hasil ditulis ke 'results' (dikosongkan dulu) supaya pemanggil bisa memakai ulang list yang sama.
    public void Roll(int count, List<MathGateData> results)
    {
        results.Clear();

        if (hasForcedRarity)
            PickForced(results);

        // Putaran pertama tanpa gate dari roll sebelumnya, putaran kedua mengisi kekurangan.
        FillCandidates(excludePrevious: avoidPreviousOffer, results);
        PickWeighted(count, results);

        if (results.Count < count && avoidPreviousOffer)
        {
            FillCandidates(excludePrevious: false, results);
            PickWeighted(count, results);
        }

        if (results.Count < count)
            Debug.LogWarning("GateGenerator: pool hanya cukup untuk " + results.Count + " gate.", this);

        // Gate paksaan ditaruh di tengah supaya mudah ditunjukkan saat demo.
        if (hasForcedRarity && results.Count >= 3)
        {
            MathGateData forced = results[0];
            results[0] = results[1];
            results[1] = forced;
        }

        hasForcedRarity = false;

        previousOffer.Clear();
        previousOffer.AddRange(results);

        if (logRolls)
            LogRoll(results);
    }

    public void ClearHistory()
    {
        previousOffer.Clear();
    }

    // Roll berikutnya dijamin berisi satu gate dari rarity ini (jika ada di pool). Dipakai panel debug saat demo.
    public void ForceRarityOnNextRoll(GateRarity rarity)
    {
        hasForcedRarity = true;
        forcedRarity = rarity;
    }

    public float ChanceSetting(GateRarity rarity)
    {
        switch (rarity)
        {
            case GateRarity.Rare: return Mathf.Max(0f, rareChance);
            case GateRarity.VeryRare: return Mathf.Max(0f, veryRareChance);
            case GateRarity.Epic: return Mathf.Max(0f, epicChance);
            case GateRarity.Legendary: return Mathf.Max(0f, legendaryChance);
            case GateRarity.Sigma: return Mathf.Max(0f, sigmaChance);
            default: return Mathf.Max(0f, commonChance);
        }
    }

    // Peluang nyata rarity ini untuk satu slot (0-1), hanya menghitung rarity yang punya gate valid di pool.
    public float ChanceOf(GateRarity rarity)
    {
        float total = 0f;
        float mine = 0f;

        for (int i = 0; i < GateRarityStyle.Count; i++)
        {
            GateRarity r = (GateRarity)i;

            if (CountInPool(r) == 0)
                continue;

            total += ChanceSetting(r);

            if (r == rarity)
                mine = ChanceSetting(r);
        }

        return total > 0f ? mine / total : 0f;
    }

    public int CountInPool(GateRarity rarity)
    {
        int count = 0;

        if (pool == null)
            return 0;

        foreach (MathGateData gate in pool)
        {
            if (gate != null && gate.Rarity == rarity && (!skipInvalidGates || gate.IsValid))
                count++;
        }

        return count;
    }

    private void PickForced(List<MathGateData> results)
    {
        FillCandidates(excludePrevious: false, results);

        int matching = candidateCount[(int)forcedRarity];

        if (matching == 0)
        {
            Debug.LogWarning("GateGenerator: tidak ada gate " + GateRarityStyle.Label(forcedRarity) + " di pool, paksaan diabaikan.", this);
            return;
        }

        results.Add(PickFromRarity(forcedRarity, Random.Range(0, matching)));
    }

    private void FillCandidates(bool excludePrevious, List<MathGateData> alreadyPicked)
    {
        candidates.Clear();

        for (int i = 0; i < candidateCount.Length; i++)
            candidateCount[i] = 0;

        if (pool == null)
            return;

        foreach (MathGateData gate in pool)
        {
            if (gate == null)
                continue;

            if (skipInvalidGates && !gate.IsValid)
                continue;

            if (candidates.Contains(gate) || alreadyPicked.Contains(gate))
                continue;

            if (excludePrevious && previousOffer.Contains(gate))
                continue;

            candidates.Add(gate);
            candidateCount[(int)gate.Rarity]++;
        }
    }

    private void PickWeighted(int count, List<MathGateData> results)
    {
        while (results.Count < count && candidates.Count > 0)
        {
            GateRarity rarity = RollRarity();
            int index = Random.Range(0, candidateCount[(int)rarity]);

            results.Add(PickFromRarity(rarity, index));
        }
    }

    // Rarity acak berbobot, hanya dari rarity yang masih punya kandidat.
    private GateRarity RollRarity()
    {
        float total = 0f;
        GateRarity fallback = GateRarity.Common;

        for (int i = 0; i < GateRarityStyle.Count; i++)
        {
            if (candidateCount[i] == 0)
                continue;

            fallback = (GateRarity)i;
            total += ChanceSetting((GateRarity)i);
        }

        // Semua peluang nol, pakai rarity apa pun yang masih ada.
        if (total <= 0f)
            return fallback;

        float roll = Random.value * total;

        for (int i = 0; i < GateRarityStyle.Count; i++)
        {
            if (candidateCount[i] == 0)
                continue;

            roll -= ChanceSetting((GateRarity)i);

            if (roll <= 0f)
                return (GateRarity)i;
        }

        return fallback;
    }

    // Mengambil kandidat ke-index dari rarity ini dan mengeluarkannya dari daftar kandidat.
    private MathGateData PickFromRarity(GateRarity rarity, int index)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Rarity != rarity)
                continue;

            if (index-- > 0)
                continue;

            MathGateData gate = candidates[i];
            candidates.RemoveAt(i);
            candidateCount[(int)rarity]--;
            return gate;
        }

        // Tidak seharusnya terjadi karena index selalu di bawah jumlah kandidat rarity ini.
        MathGateData last = candidates[candidates.Count - 1];
        candidates.RemoveAt(candidates.Count - 1);
        candidateCount[(int)last.Rarity]--;
        return last;
    }

    private void LogRoll(List<MathGateData> results)
    {
        string text = "GateGenerator: roll";

        foreach (MathGateData gate in results)
            text += "  [" + GateRarityStyle.Label(gate.Rarity) + "] " + gate.DisplayName;

        Debug.Log(text, this);
    }
}
