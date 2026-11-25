using System;
using System.Collections.Generic;
using UnityEngine;

// Menjalankan objective ronde yang sedang berlangsung.
//   - Ronde mulai: objective dibuat dari RunManager.CurrentObjective (hasil PCG di Safezone), lalu Begin.
//   - Selama Battle: Tick tiap frame. Saat IsComplete, musuh yang masih hidup di-despawn (bukan kill, tidak
//     mengubah statistik DDA), lalu ronde ditutup lewat RunManager.CompleteRound, jalur yang sama dengan ronde bersih.
//   - State keluar dari Battle (bersih, kalah, retry, kembali ke menu): objective diakhiri dengan End.
// RunManager menambahkan komponen ini otomatis jika belum ada di scene.
[DisallowMultipleComponent]
public class ObjectiveRunner : MonoBehaviour
{
    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private RunManager runManager;
    [SerializeField] private BattleManager battle;
    [SerializeField] private EnemySpawner spawner;

    [Header("Despawn")]
    // Lama efek mengecil sebelum musuh sisa dihilangkan saat objective selesai.
    [SerializeField, Min(0f)] private float despawnDuration = 0.4f;

    [Header("Debug")]
    [SerializeField] private bool logEvents = true;

    private readonly List<EnemyHealth> leftovers = new List<EnemyHealth>();
    private IWaveObjective active;

    public static ObjectiveRunner Current { get; private set; }

    // Objective ronde yang sedang berjalan, null di luar Battle.
    public IWaveObjective Active => active;

    // Exterminate jika tidak ada objective yang berjalan, sama dengan perilaku sebelum ada objective.
    public ObjectiveType ActiveType => active != null ? active.Type : ObjectiveType.Exterminate;

    public event Action<IWaveObjective> ObjectiveStarted;

    public event Action<IWaveObjective> ObjectiveCompleted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
    }

    // Objective yang sudah punya implementasi. Tipe lain tidak dipilih PCG.
    public static bool IsImplemented(ObjectiveType type)
    {
        return type == ObjectiveType.Exterminate;
    }

    public static IWaveObjective Create(ObjectiveType type)
    {
        switch (type)
        {
            default:
                return new ExterminateObjective();
        }
    }

    private void Awake()
    {
        if (runManager == null)
            runManager = GetComponent<RunManager>() != null ? GetComponent<RunManager>() : FindAnyObjectByType<RunManager>();

        if (battle == null)
            battle = FindAnyObjectByType<BattleManager>();

        if (spawner == null)
            spawner = FindAnyObjectByType<EnemySpawner>();

        if (runManager == null || battle == null)
            Debug.LogError("ObjectiveRunner: RunManager atau BattleManager tidak ditemukan.", this);
    }

    private void OnEnable()
    {
        if (Current != null && Current != this)
            Debug.LogWarning("ObjectiveRunner: ada lebih dari satu di scene, yang terakhir aktif dipakai.", this);

        Current = this;

        if (runManager != null)
        {
            runManager.RoundStarted += HandleRoundStarted;
            runManager.StateChanged += HandleStateChanged;
        }
    }

    private void OnDisable()
    {
        if (runManager != null)
        {
            runManager.RoundStarted -= HandleRoundStarted;
            runManager.StateChanged -= HandleStateChanged;
        }

        EndActive();

        if (Current == this)
            Current = null;
    }

    private void HandleRoundStarted(int round)
    {
        EndActive();

        ObjectiveType type = runManager.CurrentObjective;

        if (!IsImplemented(type))
        {
            Debug.LogWarning("ObjectiveRunner: objective " + type + " belum ada, ronde " + round + " memakai Exterminate.", this);
            type = ObjectiveType.Exterminate;
        }

        active = Create(type);
        active.Begin(new ObjectiveContext
        {
            Run = runManager,
            Battle = battle,
            Spawner = spawner,
            Dungeon = runManager.Mode == GameMode.Dungeon ? runManager.Dungeon : null,
            Wave = runManager.CurrentWave,
            Round = round,
            Random = PcgDirector.Stream(round, PcgStream.Objective)
        });

        Log("ronde " + round + " objective " + active.Type + " (" + ObjectiveInfo.Goal(active.Type) + ")");

        ObjectiveStarted?.Invoke(active);
    }

    private void HandleStateChanged(RunState state)
    {
        if (state != RunState.Battle)
            EndActive();
    }

    private void Update()
    {
        if (active == null || runManager == null || runManager.State != RunState.Battle)
            return;

        active.Tick(Time.deltaTime);

        if (active.IsComplete)
            Complete();
    }

    private void Complete()
    {
        IWaveObjective finished = active;

        DespawnLeftovers();

        Log("objective " + finished.Type + " selesai, " + finished.HudText);

        ObjectiveCompleted?.Invoke(finished);

        // CompleteRound mengubah state, jadi EndActive dipanggil lewat HandleStateChanged.
        runManager.CompleteRound();
        EndActive();
    }

    // Musuh sisa dilepas dari BattleManager dulu, jadi kematian mereka tidak dihitung sebagai kill.
    private void DespawnLeftovers()
    {
        if (battle == null || battle.AliveCount == 0)
            return;

        leftovers.Clear();
        leftovers.AddRange(battle.Alive);
        battle.EndBattle();

        foreach (EnemyHealth enemy in leftovers)
        {
            if (enemy != null && enemy.gameObject.activeInHierarchy)
                EnemyDespawn.Begin(enemy, despawnDuration);
        }

        Log(leftovers.Count + " musuh sisa di-despawn");
        leftovers.Clear();
    }

    private void EndActive()
    {
        if (active == null)
            return;

        IWaveObjective ending = active;
        active = null;
        ending.End();
    }

    private void Log(string message)
    {
        if (logEvents)
            Debug.Log("ObjectiveRunner: " + message, this);
    }
}
