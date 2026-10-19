using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Titik posisi di sekitar MC per role. Menghasilkan flank dan pincer.
// Kandidat dibuat tiap Angle Step derajat pada ring role:
//   Charger  ring = lungeRange × Charger Ring Factor (jarak siap lunge)
//   Shooter  ring = Range MC + Ideal Distance Offset (tepat di luar jangkauan MC)
//   Guardian tidak memakai slot ring, posisinya titik interpose dari GuardianBrain.
// Skor kandidat:
//   dibuang jika tidak ada NavMesh (titik di dalam dinding),
//   bonus Shooter jika ada LOS ke MC,
//   bonus jika selisih sudut dengan slot rekan >= Pincer Angle (pincer muncul dari sini),
//   penalti jarak dari posisi musuh sekarang.
// Assign greedy: musuh dengan skor terbaik memilih dulu, slot yang sudah diambil tidak bisa dipakai lagi.
[Serializable]
public class SlotManager
{
    public struct Candidate
    {
        public bool valid;
        public Vector3 position;
        public float angle;
    }

    public struct Assignment
    {
        public EnemyController enemy;
        public Vector3 position;
        public float angle;
        public float score;
        public float ringRadius;
    }

    private class Request
    {
        public EnemyController enemy;
        public float ringRadius;
        public Candidate[] candidates;
        public bool assigned;
    }

    [Header("Ring")]
    [SerializeField] private float angleStep = 30f;
    [SerializeField, Range(0.2f, 1f)] private float chargerRingFactor = 0.5f;
    // Radius pencarian NavMesh untuk tiap kandidat, relatif terhadap radius ring.
    [SerializeField, Range(0.05f, 0.5f)] private float sampleRadiusFactor = 0.25f;

    [Header("Scoring")]
    [SerializeField] private float pincerAngle = 120f;
    [SerializeField] private float pincerBonus = 0.5f;
    [SerializeField] private float travelWeight = 1f;
    [SerializeField] private float shooterLineOfSightBonus = 0.5f;

    private readonly List<Request> requests = new List<Request>();
    private readonly List<Assignment> assignments = new List<Assignment>();
    private int requestCount;
    private float currentPincerMultiplier = 1f;
    private int obstacleMask = -1;

    public IReadOnlyList<Assignment> Assignments => assignments;

    public int RequestCount => requestCount;

    // Kandidat terakhir per musuh, untuk gizmo.
    public Candidate[] GetCandidates(int requestIndex)
    {
        return requestIndex < requestCount ? requests[requestIndex].candidates : null;
    }

    public bool TryGetAssignment(EnemyController enemy, out Assignment assignment)
    {
        foreach (Assignment a in assignments)
        {
            if (a.enemy == enemy)
            {
                assignment = a;
                return true;
            }
        }

        assignment = default;
        return false;
    }

    public void Compute(IReadOnlyList<EnemyController> enemies, GroupBlackboard group, float pincerMultiplier = 1f)
    {
        currentPincerMultiplier = pincerMultiplier;

        if (obstacleMask < 0)
            obstacleMask = GameLayers.ObstacleMask;

        assignments.Clear();
        requestCount = 0;

        if (group.Player == null || group.Player.IsDead)
            return;

        int slotsPerRing = Mathf.Max(4, Mathf.RoundToInt(360f / Mathf.Max(5f, angleStep)));

        foreach (EnemyController enemy in enemies)
        {
            if (!NeedsSlot(enemy))
                continue;

            Request request = NextRequest(slotsPerRing);
            request.enemy = enemy;
            request.ringRadius = RingRadius(enemy, group);
            request.assigned = false;

            BuildCandidates(request, group.PlayerPosition);
        }

        AssignGreedy(group);
    }

    private bool NeedsSlot(EnemyController enemy)
    {
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.Health.IsDead || enemy.RoleData == null)
            return false;

        // Musuh yang tidak tahu posisi MC tidak ikut mengepung.
        if (!enemy.Blackboard.IsAware)
            return false;

        return enemy.Role == EnemyRole.Charger || enemy.Role == EnemyRole.Shooter;
    }

    private float RingRadius(EnemyController enemy, GroupBlackboard group)
    {
        EnemyRoleData data = enemy.RoleData;

        return enemy.Role == EnemyRole.Shooter
            ? group.PlayerRange + data.IdealDistanceOffset
            : data.LungeRange * chargerRingFactor;
    }

    private void BuildCandidates(Request request, Vector3 center)
    {
        int count = request.candidates.Length;
        float sampleRadius = Mathf.Max(0.1f, request.ringRadius * sampleRadiusFactor);

        for (int i = 0; i < count; i++)
        {
            float angle = 360f * i / count;
            Vector3 point = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * request.ringRadius;

            bool valid = NavMesh.SamplePosition(point, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas);

            request.candidates[i].valid = valid;
            request.candidates[i].position = valid ? hit.position : point;
            request.candidates[i].angle = angle;
        }
    }

    private void AssignGreedy(GroupBlackboard group)
    {
        for (int round = 0; round < requestCount; round++)
        {
            Request bestRequest = null;
            int bestIndex = -1;
            float bestScore = float.NegativeInfinity;

            for (int r = 0; r < requestCount; r++)
            {
                Request request = requests[r];

                if (request.assigned)
                    continue;

                for (int i = 0; i < request.candidates.Length; i++)
                {
                    if (!request.candidates[i].valid || IsTaken(request.candidates[i].position, request.ringRadius))
                        continue;

                    float score = Score(request, request.candidates[i], group);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRequest = request;
                        bestIndex = i;
                    }
                }
            }

            if (bestRequest == null)
                break;

            bestRequest.assigned = true;

            assignments.Add(new Assignment
            {
                enemy = bestRequest.enemy,
                position = bestRequest.candidates[bestIndex].position,
                angle = bestRequest.candidates[bestIndex].angle,
                score = bestScore,
                ringRadius = bestRequest.ringRadius
            });
        }
    }

    private float Score(Request request, Candidate candidate, GroupBlackboard group)
    {
        EnemyController enemy = request.enemy;

        // Penalti jarak, supaya musuh tidak menyeberang arena demi slot yang sedikit lebih bagus.
        float travel = Vector3.Distance(enemy.transform.position, candidate.position);
        float score = travelWeight * (1f - Mathf.Clamp01(travel / (request.ringRadius * 2f + 0.01f)));

        if (enemy.Role == EnemyRole.Shooter && HasLineOfSight(enemy, candidate.position, group.Player))
            score += shooterLineOfSightBonus;

        // Pincer: makin jauh sudutnya dari slot rekan yang sudah dipilih, makin tinggi bonusnya.
        if (assignments.Count > 0)
        {
            float minDifference = 180f;

            foreach (Assignment other in assignments)
                minDifference = Mathf.Min(minDifference, Mathf.Abs(Mathf.DeltaAngle(other.angle, candidate.angle)));

            score += pincerBonus * currentPincerMultiplier * Mathf.Clamp01(minDifference / pincerAngle);
        }

        return score;
    }

    private bool IsTaken(Vector3 position, float ringRadius)
    {
        // Slot dianggap terpakai jika terlalu dekat dengan slot rekan (setengah jarak antar kandidat).
        float separation = 2f * Mathf.PI * ringRadius / Mathf.Max(1, 360f / angleStep) * 0.5f;
        float sqrSeparation = separation * separation;

        foreach (Assignment other in assignments)
        {
            if ((other.position - position).sqrMagnitude < sqrSeparation)
                return true;
        }

        return false;
    }

    private bool HasLineOfSight(EnemyController enemy, Vector3 point, PlayerHealth player)
    {
        Vector3 eye = point + Vector3.up * enemy.RoleData.EyeHeight * enemy.transform.lossyScale.y;
        return !Physics.Linecast(eye, player.AimPoint, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    private Request NextRequest(int slotsPerRing)
    {
        if (requestCount == requests.Count)
            requests.Add(new Request());

        Request request = requests[requestCount++];

        if (request.candidates == null || request.candidates.Length != slotsPerRing)
            request.candidates = new Candidate[slotsPerRing];

        return request;
    }
}
