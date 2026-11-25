using UnityEngine;

// Objective bawaan: ronde selesai saat semua musuh wave mati. Perilakunya sama dengan sebelum ada objective.
public class ExterminateObjective : IWaveObjective
{
    private BattleManager battle;
    // Jumlah musuh terbanyak yang pernah dilacak ronde ini. BattleManager mengosongkan daftarnya saat ronde bersih.
    private int total;

    public ObjectiveType Type => ObjectiveType.Exterminate;

    public bool IsComplete => battle == null || battle.AliveCount == 0;

    public float Progress01 => total > 0 ? 1f - (float)Alive / total : IsComplete ? 1f : 0f;

    public string HudText => "Brainrot tersisa " + Alive + "/" + total;

    private int Alive => battle != null ? battle.AliveCount : 0;

    public void Begin(ObjectiveContext context)
    {
        battle = context.Battle;
        total = 0;
    }

    public void Tick(float deltaTime)
    {
        if (battle != null)
            total = Mathf.Max(total, battle.TotalCount);
    }

    public void End()
    {
        battle = null;
    }
}
