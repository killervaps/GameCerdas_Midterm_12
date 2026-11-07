using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Alasan sebuah kandidat posisi ditolak. Dicatat per alasan supaya terlihat aturan mana yang paling ketat.
public enum PcgReject
{
    None,
    // Tidak ada lantai di bawah titik acak (di luar arena atau lubang).
    NoGround,
    // Rule 1.
    TooCloseToPlayer,
    // Rule 2.
    Obstacle,
    // Rule 3.
    TooCloseToOther,
    // Aturan tambahan: di atas menara.
    Elevated,
    // Aturan tambahan: tidak di NavMesh atau tidak terhubung ke titik masuk MC.
    Unreachable
}

public struct PcgRejectedCandidate
{
    public Vector3 position;
    public PcgReject reason;

    public PcgRejectedCandidate(Vector3 position, PcgReject reason)
    {
        this.position = position;
        this.reason = reason;
    }
}

// Catatan satu kali generasi: berapa yang diminta, berhasil, gagal, dan ditolak per aturan.
// Posisi yang ditolak juga disimpan (dibatasi) untuk ditampilkan di Gizmos dan AI Debug Mode.
public class PcgPlacementReport
{
    private const int MaxRecordedRejections = 512;

    public readonly List<Vector3> placed = new List<Vector3>();
    public readonly List<PcgRejectedCandidate> rejected = new List<PcgRejectedCandidate>();

    public int Requested { get; set; }
    public int Failed { get; private set; }
    public int Attempts { get; private set; }

    public int RejectedNoGround { get; private set; }
    public int RejectedPlayer { get; private set; }
    public int RejectedObstacle { get; private set; }
    public int RejectedSpacing { get; private set; }
    public int RejectedElevated { get; private set; }
    public int RejectedUnreachable { get; private set; }

    public int Placed => placed.Count;

    public int RejectedTotal => RejectedNoGround + RejectedPlayer + RejectedObstacle + RejectedSpacing + RejectedElevated + RejectedUnreachable;

    public void Clear()
    {
        placed.Clear();
        rejected.Clear();
        Requested = 0;
        Failed = 0;
        Attempts = 0;
        RejectedNoGround = 0;
        RejectedPlayer = 0;
        RejectedObstacle = 0;
        RejectedSpacing = 0;
        RejectedElevated = 0;
        RejectedUnreachable = 0;
    }

    public void CountAttempt()
    {
        Attempts++;
    }

    public void CountFailure()
    {
        Failed++;
    }

    public void Accept(Vector3 position)
    {
        placed.Add(position);
    }

    public void Reject(Vector3 position, PcgReject reason)
    {
        switch (reason)
        {
            case PcgReject.NoGround: RejectedNoGround++; break;
            case PcgReject.TooCloseToPlayer: RejectedPlayer++; break;
            case PcgReject.Obstacle: RejectedObstacle++; break;
            case PcgReject.TooCloseToOther: RejectedSpacing++; break;
            case PcgReject.Elevated: RejectedElevated++; break;
            case PcgReject.Unreachable: RejectedUnreachable++; break;
        }

        if (rejected.Count < MaxRecordedRejections)
            rejected.Add(new PcgRejectedCandidate(position, reason));
    }

    public string Summary()
    {
        return Placed + "/" + Requested + " berhasil, " + Failed + " gagal, " + RejectedTotal + " ditolak" +
               " (pemain " + RejectedPlayer + ", obstacle " + RejectedObstacle + ", jarak " + RejectedSpacing +
               ", menara " + RejectedElevated + ", tak terjangkau " + RejectedUnreachable + ", tanpa lantai " + RejectedNoGround + ")";
    }
}

// Generate-and-test dari modul praktikum:
//   ulangi sampai maxAttempts:
//       ambil posisi acak di area
//       terlalu dekat MC?            -> tolak
//       mengenai obstacle?           -> tolak
//       terlalu dekat objek lain?    -> tolak
//       valid                        -> pakai, simpan posisinya
// Pemilihan prefab (weighted random) dilakukan pemanggil setelah posisi didapat, sama seperti di modul.
public static class PcgPlacement
{
    // Sphere diangkat sedikit dari lantai supaya CheckSphere tidak menyentuh permukaan lantai itu sendiri.
    private const float SphereLift = 0.05f;

    public static bool TryPlace(
        PcgSpawnArea area,
        PcgSpawnRules rules,
        SeededRandom random,
        Vector3? playerPoint,
        List<Vector3> occupied,
        PcgPlacementReport report,
        out Vector3 position)
    {
        position = Vector3.zero;

        for (int attempt = 0; attempt < rules.maxAttemptsPerObject; attempt++)
        {
            report.CountAttempt();

            // Titik acak selalu diambil, walau ternyata tidak ada lantai, supaya urutan angka acak tetap sama.
            bool onGround = area.TrySampleCandidate(random, out Vector3 candidate);

            PcgReject reason = onGround ? PcgReject.None : PcgReject.NoGround;

            // Zona aman area (mis. Start room dungeon) dihitung sebagai Rule 1, safe zone pemain.
            if (reason == PcgReject.None && area.IsExcluded(candidate))
                reason = PcgReject.TooCloseToPlayer;

            // Titik dipindah ke NavMesh terdekat sebelum aturan lain dicek. NavMesh sudah dikikis sejauh radius agent
            // dari tembok, jadi posisi final tidak menempel atau menembus tembok walau collider tembok lebih tipis
            // dari tampilannya. Semua aturan dan Gizmos memakai posisi final ini.
            if (reason == PcgReject.None && rules.requireReachable)
            {
                if (TrySnapToNavMesh(candidate, rules, out Vector3 snapped))
                    candidate = snapped;
                else
                    reason = PcgReject.Unreachable;
            }

            if (reason == PcgReject.None)
                reason = Validate(candidate, rules, playerPoint, occupied);

            if (reason != PcgReject.None)
            {
                report.Reject(candidate, reason);
                continue;
            }

            occupied.Add(candidate);
            report.Accept(candidate);
            position = candidate;
            return true;
        }

        report.CountFailure();
        return false;
    }

    public static PcgReject Validate(Vector3 candidate, PcgSpawnRules rules, Vector3? playerPoint, List<Vector3> occupied)
    {
        // Rule 1: jarak datar dari MC.
        if (playerPoint.HasValue)
        {
            Vector3 offset = candidate - playerPoint.Value;
            offset.y = 0f;

            if (offset.magnitude < rules.minDistanceFromPlayer)
                return PcgReject.TooCloseToPlayer;
        }

        // Rule 2: tidak berada di dalam obstacle.
        Vector3 sphereCenter = candidate + Vector3.up * (rules.checkRadius + SphereLift);

        if (Physics.CheckSphere(sphereCenter, rules.checkRadius, rules.ObstacleMask, QueryTriggerInteraction.Ignore))
            return PcgReject.Obstacle;

        // Aturan tambahan: tidak di atas menara.
        if (!rules.allowElevated && ElevatedPlatform.Below(candidate + Vector3.up * 0.1f) != null)
            return PcgReject.Elevated;

        // Rule 3: tidak terlalu dekat objek procedural sebelumnya.
        if (occupied != null)
        {
            float minSqr = rules.minimumSpacing * rules.minimumSpacing;

            for (int i = 0; i < occupied.Count; i++)
            {
                Vector3 offset = candidate - occupied[i];
                offset.y = 0f;

                if (offset.sqrMagnitude < minSqr)
                    return PcgReject.TooCloseToOther;
            }
        }

        // Aturan tambahan, paling mahal jadi paling akhir: berada di NavMesh dan terhubung ke titik masuk MC.
        if (rules.requireReachable && !IsReachable(candidate, rules, playerPoint))
            return PcgReject.Unreachable;

        return PcgReject.None;
    }

    public static bool TrySnapToNavMesh(Vector3 point, PcgSpawnRules rules, out Vector3 snapped)
    {
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, rules.navMeshSnapDistance, rules.NavMeshAreaMask))
        {
            snapped = hit.position;
            return true;
        }

        snapped = point;
        return false;
    }

    // Dipakai ulang untuk semua pengecekan supaya tidak membuat objek baru setiap kandidat.
    private static NavMeshPath path;

    private static bool IsReachable(Vector3 candidate, PcgSpawnRules rules, Vector3? playerPoint)
    {
        if (path == null)
            path = new NavMeshPath();

        int areaMask = rules.NavMeshAreaMask;

        if (!NavMesh.SamplePosition(candidate, out NavMeshHit target, rules.navMeshSnapDistance, areaMask))
            return false;

        // Tanpa titik referensi cukup berada di NavMesh.
        if (!playerPoint.HasValue)
            return true;

        // Titik masuk MC boleh sedikit lebih jauh dari NavMesh (mis. sedikit di atas lantai).
        if (!NavMesh.SamplePosition(playerPoint.Value, out NavMeshHit origin, 2f, areaMask))
            return true;

        // PathPartial = jalur berhenti di tepi pulau NavMesh lain, artinya tidak terhubung.
        return NavMesh.CalculatePath(origin.position, target.position, areaMask, path) &&
               path.status == NavMeshPathStatus.PathComplete;
    }
}
