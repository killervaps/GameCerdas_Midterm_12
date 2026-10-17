using System.Collections.Generic;
using UnityEngine;

// Memilih aksi dengan skor tertinggi. 
// Pasangan SelectorNode pada Behavior Tree, tetapi memilih berdasarkan skor, bukan urutan.
public class UtilitySelector
{
    private readonly List<UtilityAction> actions;
    private readonly float decisionInterval;
    private readonly float inertiaBonus;

    private float nextDecisionTime;

    public UtilityAction Current { get; private set; }

    public IReadOnlyList<UtilityAction> Actions => actions;

    public UtilitySelector(
        List<UtilityAction> actions,
        float decisionInterval,
        float inertiaBonus)
    {
        this.actions = actions;
        this.decisionInterval = decisionInterval;
        this.inertiaBonus = inertiaBonus;
    }

    // Dipanggil setiap frame: keputusan diperbarui tiap decisionInterval, sedangkan aksi terpilih dijalankan setiap frame.
    public void Tick()
    {
        if (Current == null || Time.time >= nextDecisionTime)
        {
            Decide();

            nextDecisionTime = Time.time + decisionInterval;
        }

        Current.Execute();
    }

    // Memaksa keputusan baru pada Tick berikutnya.
    public void RequestDecision()
    {
        nextDecisionTime = 0f;
    }

    private void Decide()
    {
        UtilityAction best = null;
        float bestScore = float.NegativeInfinity;

        foreach (UtilityAction action in actions)
        {
            float score = action.Evaluate();

            // Inertia: aksi yang sedang berjalan sedikit diuntungkan agar keputusan tidak berkedip di sekitar titik silang skor.
            if (action == Current)
                score += inertiaBonus;

            if (score > bestScore)
            {
                best = action;
                bestScore = score;
            }
        }

        Current = best;
    }
}
