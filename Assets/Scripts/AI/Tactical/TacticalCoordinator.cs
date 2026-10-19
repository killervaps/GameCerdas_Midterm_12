using System.Collections.Generic;
using UnityEngine;

// Koordinator kelompok musuh, satu per arena. Brain tidak saling tahu, koordinasi lewat sini.
// Saat wave muncul: daftar musuh, pasangkan Guardian dengan Shooter (anchor = Shooter).
// Loop tiap 0,25 s: perbarui GroupBlackboard, hitung dan bagikan slot, arbitrasi token, cabut token yang timeout.
// Saat musuh mati: lepas token dan slot. Jika yang mati anchor Guardian, GuardianBrain masuk mode marah sendiri.
public class TacticalCoordinator : MonoBehaviour
{
    [Header("References")]
    // Opsional. Jika diisi, pairing dan loop langsung dijalankan begitu wave muncul.
    [SerializeField] private EnemySpawner spawner;

    [Header("Loop")]
    [SerializeField] private float tickInterval = 0.25f;

    [Header("Attack Tokens")]
    [SerializeField] private AttackTokenManager tokens = new AttackTokenManager();

    [Header("Slots")]
    [SerializeField] private SlotManager slots = new SlotManager();

    [Header("Pairing")]
    [SerializeField] private bool pairGuardianWithShooter = true;

    [Header("Build Reactions")]
    [SerializeField] private BuildReactions reactions = new BuildReactions();

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logEvents = true;

    private readonly List<EnemyController> registry = new List<EnemyController>();
    private readonly GroupBlackboard group = new GroupBlackboard();

    private float nextTickTime;

    public static TacticalCoordinator Current { get; private set; }

    public AttackTokenManager Tokens => tokens;

    public SlotManager Slots => slots;

    public GroupBlackboard Group => group;

    public BuildReactions Reactions => reactions;

    public IReadOnlyList<EnemyController> Registry => registry;

    private void OnEnable()
    {
        Current = this;
        AttackTokens.Provider = tokens;

        EnemyHealth.AnyDied += HandleEnemyDied;

        if (spawner != null)
            spawner.WaveSpawned += HandleWaveSpawned;
    }

    private void OnDisable()
    {
        if (Current == this)
            Current = null;

        if (AttackTokens.Provider == tokens)
            AttackTokens.Provider = null;

        EnemyHealth.AnyDied -= HandleEnemyDied;

        if (spawner != null)
            spawner.WaveSpawned -= HandleWaveSpawned;

        tokens.Clear();
    }

    private void Update()
    {
        if (Time.time < nextTickTime)
            return;

        nextTickTime = Time.time + tickInterval;
        RunLoop();
    }

    private void HandleWaveSpawned(IReadOnlyList<EnemyHealth> enemies)
    {
        tokens.Clear();

        Log("wave baru, " + enemies.Count + " musuh terdaftar");

        RunLoop();
    }

    private void HandleEnemyDied(EnemyHealth health)
    {
        EnemyController enemy = health.GetComponent<EnemyController>();

        if (enemy == null)
            return;

        tokens.Release(enemy);
        enemy.ClearSlot();
        registry.Remove(enemy);

        foreach (EnemyController other in registry)
        {
            if (other.Brain is GuardianBrain guardian && guardian.Anchor == enemy.transform)
                Log(other.name + " kehilangan anchor " + enemy.name + ", mode marah");
        }
    }

    private void RunLoop()
    {
        RebuildRegistry();

        bool anyAware = false;

        foreach (EnemyController enemy in registry)
        {
            if (enemy.Blackboard.IsAware)
            {
                anyAware = true;
                break;
            }
        }

        group.Update(PlayerHealth.Current, anyAware);

        // Ancaman live memakai rumus yang sama dengan label kartu gate.
        if (ThreatEstimator.Current != null && group.Stats != null)
            group.SetThreat(ThreatEstimator.Current.EvaluateLive(registry, group.Stats.GetSnapshot()));

        if (pairGuardianWithShooter)
            PairGuardians();

        BuildIndicators build = group.Indicators;

        slots.Compute(registry, group, reactions.PincerBonusMultiplier(build));
        PushSlots();

        tokens.Tick(group.PlayerPosition, reactions.TokenTimeoutMultiplier(build));
    }

    private void RebuildRegistry()
    {
        registry.Clear();

        foreach (EnemyController enemy in EnemyController.Active)
        {
            if (enemy != null && !enemy.Health.IsDead)
                registry.Add(enemy);
        }
    }

    // Guardian tanpa pasangan dijadikan bodyguard Shooter terdekat yang belum dijaga.
    // Guardian yang sudah marah tidak dipasangkan lagi.
    private void PairGuardians()
    {
        foreach (EnemyController enemy in registry)
        {
            if (!(enemy.Brain is GuardianBrain guardian) || guardian.IsBodyguard || guardian.IsEnraged)
                continue;

            EnemyController shooter = FindUnguardedShooter(enemy.transform.position);

            if (shooter == null)
                continue;

            guardian.SetAnchor(shooter.transform);
            Log(enemy.name + " menjadi bodyguard " + shooter.name);
        }
    }

    private EnemyController FindUnguardedShooter(Vector3 from)
    {
        EnemyController best = null;
        float bestDistance = float.PositiveInfinity;

        foreach (EnemyController candidate in registry)
        {
            if (candidate.Role != EnemyRole.Shooter || IsGuarded(candidate))
                continue;

            float distance = (candidate.transform.position - from).sqrMagnitude;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private bool IsGuarded(EnemyController shooter)
    {
        foreach (EnemyController enemy in registry)
        {
            if (enemy.Brain is GuardianBrain guardian && guardian.Anchor == shooter.transform)
                return true;
        }

        return false;
    }

    private void PushSlots()
    {
        foreach (EnemyController enemy in registry)
        {
            if (slots.TryGetAssignment(enemy, out SlotManager.Assignment assignment))
                enemy.AssignSlot(assignment.position, assignment.angle);
            else
                enemy.ClearSlot();
        }
    }

    private void Log(string message)
    {
        if (logEvents)
            Debug.Log("Tactical: " + message, this);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying || group.Player == null)
            return;

        Vector3 lift = Vector3.up * 0.05f;

        // Kandidat slot sebagai titik kecil, ring per role.
        for (int r = 0; r < slots.RequestCount; r++)
        {
            SlotManager.Candidate[] candidates = slots.GetCandidates(r);

            if (candidates == null)
                continue;

            foreach (SlotManager.Candidate candidate in candidates)
            {
                Gizmos.color = candidate.valid ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 0f, 0f, 0.35f);
                Gizmos.DrawSphere(candidate.position + lift, 0.04f);
            }
        }

        // Slot terpilih: bola warna role dan garis dari musuh ke slot.
        foreach (SlotManager.Assignment assignment in slots.Assignments)
        {
            if (assignment.enemy == null)
                continue;

            Color color = RoleColors.Get(assignment.enemy.Role);

            Gizmos.color = color;
            Gizmos.DrawSphere(assignment.position + lift, 0.12f);
            Gizmos.DrawLine(assignment.enemy.transform.position + lift, assignment.position + lift);

            Gizmos.color = new Color(color.r, color.g, color.b, 0.4f);
            Gizmos.DrawLine(group.PlayerPosition + lift, assignment.position + lift);
        }

        // Pemegang token: bola kuning di atas kepala dan garis ke MC.
        foreach (AttackTokenManager.Holder holder in tokens.Holders)
        {
            if (holder.enemy == null)
                continue;

            Vector3 head = holder.enemy.Perception.EyePosition + Vector3.up * 0.2f;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(head, 0.15f);
            Gizmos.DrawLine(head, group.Player.AimPoint);
        }
    }
}
