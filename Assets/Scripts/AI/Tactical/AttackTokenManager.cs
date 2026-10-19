using System;
using System.Collections.Generic;
using UnityEngine;

// Pembatas jumlah penyerang aktif. Musuh yang ingin menyerang meminta token, yang tidak dapat menunggu (di slot).
// prioritas = w1 × (1 / jarak ke MC) + w2 × lama menunggu + w3 × bobot role
// Token dilepas saat serangan selesai, saat musuh mati, atau dicabut setelah timeout.
[Serializable]
public class AttackTokenManager : IAttackTokenProvider
{
    public class Holder
    {
        public EnemyController enemy;
        public float since;
    }

    public class Waiter
    {
        public EnemyController enemy;
        public float firstRequest;
        public float lastRequest;
        public float priority;
    }

    [Header("Limit")]
    [SerializeField] private int maxTokens = 2;
    // TODO(desain): batas per role adalah tafsiran supaya dua Charger di ronde 1 bergantian walau batas total 2.
    [SerializeField] private int maxChargerTokens = 1;
    [SerializeField] private int maxShooterTokens = 1;
    [SerializeField] private int maxGuardianTokens = 1;

    [Header("Timing")]
    [SerializeField] private float timeout = 3f;
    // Musuh yang tokennya dicabut tidak boleh langsung meminta lagi.
    [SerializeField] private float revokeCooldown = 1f;
    // Permintaan yang tidak diperbarui selama ini dianggap batal (musuh sudah tidak ingin menyerang).
    [SerializeField] private float requestExpiry = 0.5f;

    [Header("Priority Weights")]
    [SerializeField] private float distanceWeight = 1f;
    [SerializeField] private float waitWeight = 0.5f;
    [SerializeField] private float chargerWeight = 1f;
    [SerializeField] private float shooterWeight = 0.8f;
    [SerializeField] private float guardianWeight = 1.2f;

    [Header("Debug")]
    [SerializeField] private bool logTokens = true;

    private static readonly Comparison<Waiter> ByPriorityDescending = (a, b) => b.priority.CompareTo(a.priority);

    private readonly List<Holder> holders = new List<Holder>();
    private readonly List<Waiter> waiters = new List<Waiter>();
    private readonly Dictionary<EnemyController, float> cooldownUntil = new Dictionary<EnemyController, float>();

    private Vector3 playerPosition;

    public IReadOnlyList<Holder> Holders => holders;

    public IReadOnlyList<Waiter> Waiters => waiters;

    public int MaxTokens => maxTokens;

    public float Timeout => timeout;

    public bool Request(EnemyController requester)
    {
        if (requester == null)
            return false;

        if (Holds(requester))
            return true;

        float now = Time.time;

        if (cooldownUntil.TryGetValue(requester, out float until) && now < until)
            return false;

        Waiter waiter = FindWaiter(requester);

        if (waiter == null)
        {
            waiter = new Waiter { enemy = requester, firstRequest = now };
            waiters.Add(waiter);
        }

        waiter.lastRequest = now;
        waiter.priority = Priority(waiter, now);

        // Diberikan langsung jika ada slot dan tidak ada penunggu lain yang lebih berhak untuk slot yang sama.
        if (CanGrant(requester) && IsTopEligible(waiter))
            Grant(waiter, now);

        return Holds(requester);
    }

    public void Release(EnemyController holder)
    {
        for (int i = holders.Count - 1; i >= 0; i--)
        {
            if (holders[i].enemy == holder)
            {
                holders.RemoveAt(i);
                Log(holder.name + " melepas token");
            }
        }

        RemoveWaiter(holder);
    }

    public bool Holds(EnemyController enemy)
    {
        foreach (Holder holder in holders)
        {
            if (holder.enemy == enemy)
                return true;
        }

        return false;
    }

    public float HeldFor(EnemyController enemy)
    {
        foreach (Holder holder in holders)
        {
            if (holder.enemy == enemy)
                return Time.time - holder.since;
        }

        return 0f;
    }

    // Posisi antrean (1 = berikutnya), 0 jika tidak menunggu.
    public int QueuePosition(EnemyController enemy)
    {
        Waiter target = FindWaiter(enemy);

        if (target == null)
            return 0;

        int position = 1;

        foreach (Waiter waiter in waiters)
        {
            if (waiter != target && waiter.priority > target.priority)
                position++;
        }

        return position;
    }

    // Dipanggil TacticalCoordinator setiap loop.
    // timeoutMultiplier < 1 membuat penyerang lebih cepat bergantian (reaksi terhadap build).
    public void Tick(Vector3 currentPlayerPosition, float timeoutMultiplier = 1f)
    {
        playerPosition = currentPlayerPosition;
        float now = Time.time;
        float currentTimeout = timeout * timeoutMultiplier;

        for (int i = holders.Count - 1; i >= 0; i--)
        {
            Holder holder = holders[i];

            if (!IsValid(holder.enemy))
            {
                holders.RemoveAt(i);
                continue;
            }

            if (now - holder.since > currentTimeout)
            {
                Log(holder.enemy.name + " token dicabut (timeout " + currentTimeout.ToString("0.#") + " s)");

                cooldownUntil[holder.enemy] = now + revokeCooldown;
                holders.RemoveAt(i);
            }
        }

        for (int i = waiters.Count - 1; i >= 0; i--)
        {
            Waiter waiter = waiters[i];

            if (!IsValid(waiter.enemy) || now - waiter.lastRequest > requestExpiry)
            {
                waiters.RemoveAt(i);
                continue;
            }

            waiter.priority = Priority(waiter, now);
        }

        // Arbitrase: penunggu dengan prioritas tertinggi diberi token lebih dulu selama masih ada slot.
        waiters.Sort(ByPriorityDescending);

        for (int i = 0; i < waiters.Count && holders.Count < maxTokens; i++)
        {
            if (CanGrant(waiters[i].enemy))
            {
                Grant(waiters[i], now);
                i--;
            }
        }
    }

    public void Clear()
    {
        holders.Clear();
        waiters.Clear();
        cooldownUntil.Clear();
    }

    private void Grant(Waiter waiter, float now)
    {
        waiters.Remove(waiter);
        holders.Add(new Holder { enemy = waiter.enemy, since = now });

        Log(waiter.enemy.name + " dapat token (" + holders.Count + "/" + maxTokens + ", prioritas " + waiter.priority.ToString("0.00") + ")");
    }

    private bool CanGrant(EnemyController enemy)
    {
        if (holders.Count >= maxTokens)
            return false;

        int sameRole = 0;

        foreach (Holder holder in holders)
        {
            if (holder.enemy.Role == enemy.Role)
                sameRole++;
        }

        return sameRole < RoleLimit(enemy.Role);
    }

    private bool IsTopEligible(Waiter candidate)
    {
        foreach (Waiter other in waiters)
        {
            if (other == candidate || other.priority <= candidate.priority)
                continue;

            // Penunggu lain lebih berhak dan bersaing untuk role yang sama atau slot total terakhir.
            if (CanGrant(other.enemy) && (other.enemy.Role == candidate.enemy.Role || holders.Count == maxTokens - 1))
                return false;
        }

        return true;
    }

    private float Priority(Waiter waiter, float now)
    {
        Vector3 offset = waiter.enemy.transform.position - playerPosition;
        offset.y = 0f;

        float distance = Mathf.Max(0.1f, offset.magnitude);

        return distanceWeight / distance + waitWeight * (now - waiter.firstRequest) + RoleWeight(waiter.enemy.Role);
    }

    private int RoleLimit(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Shooter: return maxShooterTokens;
            case EnemyRole.Guardian: return maxGuardianTokens;
            default: return maxChargerTokens;
        }
    }

    private float RoleWeight(EnemyRole role)
    {
        switch (role)
        {
            case EnemyRole.Shooter: return shooterWeight;
            case EnemyRole.Guardian: return guardianWeight;
            default: return chargerWeight;
        }
    }

    private Waiter FindWaiter(EnemyController enemy)
    {
        foreach (Waiter waiter in waiters)
        {
            if (waiter.enemy == enemy)
                return waiter;
        }

        return null;
    }

    private void RemoveWaiter(EnemyController enemy)
    {
        for (int i = waiters.Count - 1; i >= 0; i--)
        {
            if (waiters[i].enemy == enemy)
                waiters.RemoveAt(i);
        }
    }

    private static bool IsValid(EnemyController enemy)
    {
        return enemy != null && enemy.isActiveAndEnabled && !enemy.Health.IsDead;
    }

    private void Log(string message)
    {
        if (logTokens)
            Debug.Log("Token: " + message);
    }
}
