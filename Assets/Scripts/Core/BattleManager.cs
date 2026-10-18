using System;
using System.Collections.Generic;
using UnityEngine;

// Mengawasi satu pertempuran. Menang ronde jika semua musuh mati, kalah jika MC mati.
// Berbasis event (Died dari EnemyHealth dan PlayerHealth), tidak mengecek kondisi setiap frame.
public class BattleManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Debug")]
    [SerializeField] private bool logEvents = true;

    private readonly List<EnemyHealth> alive = new List<EnemyHealth>();
    private readonly List<EnemyHealth> tracked = new List<EnemyHealth>();

    public bool IsBattleActive { get; private set; }

    public int AliveCount => alive.Count;

    public int TotalCount => tracked.Count;

    public float BattleStartTime { get; private set; }

    public event Action<EnemyHealth> EnemyDied;

    public event Action AllEnemiesDead;

    public event Action PlayerDied;

    private void OnEnable()
    {
        if (playerHealth != null)
            playerHealth.Died += HandlePlayerDied;
    }

    private void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.Died -= HandlePlayerDied;

        Untrack();
    }

    public void BeginBattle(IReadOnlyList<EnemyHealth> enemies)
    {
        Untrack();

        foreach (EnemyHealth enemy in enemies)
        {
            if (enemy == null)
                continue;

            tracked.Add(enemy);
            enemy.Died += HandleEnemyDied;

            if (!enemy.IsDead)
                alive.Add(enemy);
        }

        IsBattleActive = true;
        BattleStartTime = Time.time;

        Log("pertempuran mulai, " + alive.Count + " musuh");

        // Wave kosong langsung dianggap bersih.
        if (alive.Count == 0)
            FinishWithVictory();
    }

    // Menghentikan pertempuran tanpa hasil, mis. saat retry di tengah ronde.
    public void EndBattle()
    {
        IsBattleActive = false;
        Untrack();
    }

    private void HandleEnemyDied(EnemyHealth enemy)
    {
        if (!IsBattleActive || !alive.Remove(enemy))
            return;

        Log(enemy.name + " mati, sisa " + alive.Count);

        EnemyDied?.Invoke(enemy);

        if (alive.Count == 0)
            FinishWithVictory();
    }

    private void HandlePlayerDied()
    {
        if (!IsBattleActive)
            return;

        IsBattleActive = false;
        Untrack();

        Log("MC mati, ronde gagal");

        PlayerDied?.Invoke();
    }

    private void FinishWithVictory()
    {
        IsBattleActive = false;
        Untrack();

        Log("semua musuh mati, ronde bersih");

        AllEnemiesDead?.Invoke();
    }

    private void Untrack()
    {
        foreach (EnemyHealth enemy in tracked)
        {
            if (enemy != null)
                enemy.Died -= HandleEnemyDied;
        }

        tracked.Clear();
        alive.Clear();
    }

    private void Log(string message)
    {
        if (logEvents)
            Debug.Log("BattleManager: " + message, this);
    }
}
