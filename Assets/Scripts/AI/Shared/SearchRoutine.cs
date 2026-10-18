using UnityEngine;
using UnityEngine.AI;

// Rutinitas Search yang dipakai ulang semua brain.
// Move: berjalan ke posisi terakhir MC (di-snap ke NavMesh). Scan: berhenti dan menoleh kanan-tengah-kiri-tengah.
// Selesai (Success) setelah scan habis atau timeout, gagal (Failure) jika tujuan tidak ada di NavMesh.
public class SearchRoutine
{
    public enum Phase
    {
        None,
        Move,
        Scan
    }

    private readonly EnemyMotor motor;
    private readonly Transform transform;

    private EnemyRoleData data;
    private float startTime;
    private float scanStartTime;
    private float scanBaseYaw;

    public Phase CurrentPhase { get; private set; } = Phase.None;

    public bool IsActive => CurrentPhase != Phase.None;

    public Vector3 Target { get; private set; }

    public float ScanProgress =>
        CurrentPhase == Phase.Scan && data != null && data.SearchScanDuration > 0f
            ? Mathf.Clamp01((Time.time - scanStartTime) / data.SearchScanDuration)
            : 0f;

    public SearchRoutine(EnemyMotor motor, Transform transform)
    {
        this.motor = motor;
        this.transform = transform;
    }

    public bool Begin(Vector3 lastKnownPosition, EnemyRoleData roleData)
    {
        data = roleData;

        // Posisi MC bisa sedikit di luar NavMesh, jadi dicari titik NavMesh terdekat.
        if (!NavMesh.SamplePosition(lastKnownPosition, out NavMeshHit hit, data.NavMeshSampleDistance, NavMesh.AllAreas))
        {
            Cancel();
            return false;
        }

        Target = hit.position;
        startTime = Time.time;
        CurrentPhase = Phase.Move;

        motor.ClearFacing();
        motor.MoveTo(Target, data.SearchSpeedMultiplier, 0.2f);

        return true;
    }

    public void Cancel()
    {
        if (CurrentPhase == Phase.Scan)
            motor.ClearFacing();

        CurrentPhase = Phase.None;
    }

    public NodeState Tick()
    {
        if (CurrentPhase == Phase.None || data == null)
            return NodeState.Failure;

        if (CurrentPhase == Phase.Move)
        {
            // Pengaman jika tujuan tidak pernah tercapai.
            if (Time.time - startTime >= data.SearchTimeout)
            {
                Cancel();
                return NodeState.Success;
            }

            if (!motor.HasArrived)
                return NodeState.Running;

            motor.Stop();

            CurrentPhase = Phase.Scan;
            scanStartTime = Time.time;
            scanBaseYaw = transform.eulerAngles.y;
        }

        float elapsed = Time.time - scanStartTime;

        if (elapsed >= data.SearchScanDuration)
        {
            Cancel();
            return NodeState.Success;
        }

        // Satu siklus sinus selama durasi scan. Motor menghaluskan rotasi per frame.
        float yawOffset = Mathf.Sin(elapsed / data.SearchScanDuration * Mathf.PI * 2f) * data.SearchScanAngle;

        motor.FaceDirection(Quaternion.Euler(0f, scanBaseYaw + yawOffset, 0f) * Vector3.forward);

        return NodeState.Running;
    }
}
