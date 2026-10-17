using System.Collections.Generic;
using UnityEngine;

// Mengacak pilihan gate dari pool, berbobot rarity, tanpa duplikat dalam satu roll.
public class GateGenerator : MonoBehaviour
{
    [Header("Pool")]
    [SerializeField] private MathGateData[] pool;

    [Header("Rarity Weights")]
    [SerializeField] private float commonWeight = 60f;
    [SerializeField] private float rareWeight = 30f;
    [SerializeField] private float epicWeight = 10f;

    [Header("Rules")]
    // Gate yang ditawarkan di roll sebelumnya didahulukan untuk dihindari.
    // Jika pool tidak cukup, gate lama tetap boleh muncul lagi.
    [SerializeField] private bool avoidPreviousOffer = true;
    // Gate tanpa buff atau tanpa trade-off tidak ikut diacak.
    [SerializeField] private bool skipInvalidGates = true;

    private readonly List<MathGateData> candidates = new List<MathGateData>();
    private readonly List<MathGateData> previousOffer = new List<MathGateData>();

    public IReadOnlyList<MathGateData> Pool => pool;

    // Hasil ditulis ke 'results' (dikosongkan dulu) supaya pemanggil bisa memakai ulang list yang sama.
    public void Roll(int count, List<MathGateData> results)
    {
        results.Clear();

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

        previousOffer.Clear();
        previousOffer.AddRange(results);
    }

    public void ClearHistory()
    {
        previousOffer.Clear();
    }

    private void FillCandidates(bool excludePrevious, List<MathGateData> alreadyPicked)
    {
        candidates.Clear();

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
        }
    }

    private void PickWeighted(int count, List<MathGateData> results)
    {
        while (results.Count < count && candidates.Count > 0)
        {
            float total = 0f;

            foreach (MathGateData gate in candidates)
                total += GetWeight(gate.Rarity);

            int chosen = candidates.Count - 1;

            if (total > 0f)
            {
                float roll = Random.value * total;

                for (int i = 0; i < candidates.Count; i++)
                {
                    roll -= GetWeight(candidates[i].Rarity);

                    if (roll <= 0f)
                    {
                        chosen = i;
                        break;
                    }
                }
            }
            else
            {
                chosen = Random.Range(0, candidates.Count);
            }

            results.Add(candidates[chosen]);
            candidates.RemoveAt(chosen);
        }
    }

    private float GetWeight(GateRarity rarity)
    {
        switch (rarity)
        {
            case GateRarity.Rare: return Mathf.Max(0f, rareWeight);
            case GateRarity.Epic: return Mathf.Max(0f, epicWeight);
            default: return Mathf.Max(0f, commonWeight);
        }
    }
}
