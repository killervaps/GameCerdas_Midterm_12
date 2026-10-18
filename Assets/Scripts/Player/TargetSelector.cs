using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Memilih target MC.
// 1. Kandidat = musuh hidup dalam Range.
// 2. Target lock yang masih hidup dan dalam Range dipakai lebih dulu.
// 3. Jika tidak ada lock, auto-target = musuh terdekat yang punya line of sight.
// Tombol CycleTarget mengunci target saat ini, lalu berpindah ke kandidat berikutnya (urut jarak), dan setelah kandidat terakhir lock dilepas.
[RequireComponent(typeof(PlayerStats))]
public class TargetSelector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMotor motor;
    [SerializeField] private PlayerHealth health;

    [Header("Targeting")]
    [SerializeField] private float rescanInterval = 0.1f;
    [SerializeField] private bool requireLineOfSight = true;
    // Tinggi titik pandang MC untuk raycast LOS.
    [SerializeField] private float eyeHeight = 1f;
    // Lock baru lepas jika target keluar dari Range × nilai ini, supaya tidak berkedip di batas Range.
    [SerializeField] private float lockBreakRangeMultiplier = 1.15f;

    [Header("Input")]
    [SerializeField] private string cycleActionName = "Player/CycleTarget";

    [Header("Markers (opsional)")]
    [SerializeField] private Transform autoTargetMarker;
    [SerializeField] private Transform lockMarker;
    [SerializeField] private float markerHeight = 0.05f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool logChanges = false;

    private readonly List<EnemyHealth> candidates = new List<EnemyHealth>();

    private PlayerStats stats;
    private Comparison<EnemyHealth> byDistance;
    private InputAction cycleAction;
    private int obstacleMask;
    private float nextScanTime;

    public EnemyHealth CurrentTarget { get; private set; }

    public EnemyHealth LockedTarget { get; private set; }

    public bool IsLocked => LockedTarget != null;

    public float Range => stats.Get(StatType.Range);

    public IReadOnlyList<EnemyHealth> Candidates => candidates;

    public event Action<EnemyHealth> TargetChanged;

    private void Awake()
    {
        stats = GetComponent<PlayerStats>();

        if (motor == null)
            motor = GetComponent<PlayerMotor>();

        if (health == null)
            health = GetComponent<PlayerHealth>();

        obstacleMask = GameLayers.ObstacleMask;

        byDistance = (a, b) =>
            SqrDistance(a).CompareTo(SqrDistance(b));

        cycleAction = InputSystem.actions != null
            ? InputSystem.actions.FindAction(cycleActionName)
            : null;

        if (cycleAction == null)
            Debug.LogError("TargetSelector: action '" + cycleActionName + "' tidak ditemukan.", this);
    }

    private void OnEnable()
    {
        cycleAction?.Enable();
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            Unlock();
            SetCurrent(null);
            return;
        }

        if (cycleAction != null && cycleAction.WasPressedThisFrame())
            Cycle();

        // Scan lebih cepat jika target saat ini sudah tidak valid.
        if (Time.time >= nextScanTime || !IsAlive(CurrentTarget))
        {
            Rescan();
            nextScanTime = Time.time + rescanInterval;
        }
    }

    private void LateUpdate()
    {
        PlaceMarker(autoTargetMarker, IsLocked ? null : CurrentTarget);
        PlaceMarker(lockMarker, LockedTarget);
    }

    public void Rescan()
    {
        GatherCandidates();

        if (IsLocked && !IsLockStillValid(LockedTarget))
        {
            Log("lock lepas, " + LockedTarget.name + " mati atau keluar Range");
            Unlock();
        }

        if (IsLocked)
        {
            SetCurrent(LockedTarget);
            return;
        }

        EnemyHealth best = null;

        // Kandidat sudah urut jarak, ambil yang pertama terlihat.
        foreach (EnemyHealth candidate in candidates)
        {
            if (!requireLineOfSight || HasLineOfSight(candidate))
            {
                best = candidate;
                break;
            }
        }

        SetCurrent(best);
    }

    public void Cycle()
    {
        GatherCandidates();

        if (candidates.Count == 0)
        {
            Unlock();
            Rescan();
            return;
        }

        if (!IsLocked)
        {
            LockOn(IsAlive(CurrentTarget) ? CurrentTarget : candidates[0]);
            return;
        }

        int next = candidates.IndexOf(LockedTarget) + 1;

        if (next >= candidates.Count)
        {
            Log("lock dilepas, kembali ke auto-target");
            Unlock();
            Rescan();
            return;
        }

        LockOn(candidates[next]);
    }

    public void LockOn(EnemyHealth enemy)
    {
        if (!IsAlive(enemy))
            return;

        LockedTarget = enemy;
        Log("lock ke " + enemy.name);

        SetCurrent(enemy);
    }

    public void Unlock()
    {
        LockedTarget = null;
    }

    public bool HasLineOfSight(EnemyHealth enemy)
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;

        return !Physics.Linecast(eye, enemy.AimPoint, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    private void GatherCandidates()
    {
        candidates.Clear();

        float range = Range;
        float sqrRange = range * range;

        IReadOnlyList<EnemyHealth> all = EnemyHealth.Active;

        for (int i = 0; i < all.Count; i++)
        {
            EnemyHealth enemy = all[i];

            if (!IsAlive(enemy))
                continue;

            if (SqrDistance(enemy) <= sqrRange)
                candidates.Add(enemy);
        }

        candidates.Sort(byDistance);
    }

    private bool IsLockStillValid(EnemyHealth enemy)
    {
        if (!IsAlive(enemy))
            return false;

        float breakRange = Range * lockBreakRangeMultiplier;

        return SqrDistance(enemy) <= breakRange * breakRange;
    }

    private void SetCurrent(EnemyHealth enemy)
    {
        if (motor != null)
            motor.FacingTarget = enemy != null ? enemy.transform : null;

        if (enemy == CurrentTarget)
            return;

        CurrentTarget = enemy;

        Log(enemy != null ? "target " + enemy.name : "tidak ada target");

        TargetChanged?.Invoke(enemy);
    }

    private static bool IsAlive(EnemyHealth enemy)
    {
        return enemy != null && enemy.isActiveAndEnabled && !enemy.IsDead;
    }

    // Jarak datar di bidang XZ, supaya beda tinggi pivot tidak memengaruhi Range.
    private float SqrDistance(EnemyHealth enemy)
    {
        Vector3 offset = enemy.transform.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private void PlaceMarker(Transform marker, EnemyHealth enemy)
    {
        if (marker == null)
            return;

        bool show = IsAlive(enemy);

        if (marker.gameObject.activeSelf != show)
            marker.gameObject.SetActive(show);

        if (show)
            marker.position = enemy.transform.position + Vector3.up * markerHeight;
    }

    private void Log(string message)
    {
        if (logChanges)
            Debug.Log("TargetSelector: " + message, this);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
            return;

        Vector3 center = transform.position + Vector3.up * 0.05f;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        // Di Edit Mode stats belum diisi Awake, jadi diambil langsung.
        PlayerStats source = stats != null ? stats : GetComponent<PlayerStats>();

        if (source != null)
            DrawCircle(center, source.Get(StatType.Range));

        if (!Application.isPlaying)
            return;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;

        // Kandidat tanpa LOS ditandai merah supaya terlihat kenapa tidak dipilih.
        foreach (EnemyHealth candidate in candidates)
        {
            if (!IsAlive(candidate) || candidate == CurrentTarget)
                continue;

            Gizmos.color = HasLineOfSight(candidate) ? new Color(1f, 1f, 1f, 0.25f) : Color.red;
            Gizmos.DrawLine(eye, candidate.AimPoint);
        }

        if (IsAlive(CurrentTarget))
        {
            Gizmos.color = IsLocked ? Color.magenta : Color.green;
            Gizmos.DrawLine(eye, CurrentTarget.AimPoint);
            Gizmos.DrawWireSphere(CurrentTarget.AimPoint, IsLocked ? 0.6f : 0.4f);
        }
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        const int segments = 48;

        Vector3 previous = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
