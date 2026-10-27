using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// Shooter. Utility AI, semua skor dihitung paralel lalu dipilih yang tertinggi (inertia mencegah kedip):
//   Flee        kurva dari rasio HP, tinggi di bawah 30%
//   Kite        TooClose × KiteEffect
//   Reposition  TooClose × (1 − KiteEffect) × coverValue, + bonus jika titiknya dekat Guardian
//   Hold        konstanta
//   Search      meluruh mengikuti sisa memori
// TooClose = clamp01((ideal − jarak) / ideal), ideal = Range MC + offset.
// KiteEffect = clamp01(1,5 − Mobilitas). Mundur efektif melawan MC lambat, melawan MC cepat lebih baik pindah posisi.
// Menembak berjalan paralel dengan aksi gerak jika MC terlihat, cooldown siap, dan dapat token.
public class ShooterBrain : EnemyBrain
{
    private struct Candidate
    {
        public Vector3 position;
        public float score;
        public bool valid;
    }

    [Header("Shooting")]
    // ID ProjectilePool peluru musuh di scene.
    [SerializeField] private string projectilePoolId = "EnemyBolt";
    [SerializeField] private Vector3 muzzleOffset = new Vector3(0f, 1.5f, 0.6f);

    [Header("Reposition")]
    // Jumlah titik kandidat di lingkaran ideal sekitar MC (12 berarti tiap 30 derajat).
    [SerializeField] private int candidateCount = 12;
    // Titik yang lebih jauh dari ini dari posisi sekarang dianggap terlalu mahal untuk dikejar.
    [SerializeField] private float maxTravelDistance = 6f;
    [SerializeField] private float navMeshSampleRadius = 1f;

    [Header("Jump Escape")]
    // Shooter di tempat tinggi (puncak menara) melompat turun lewat NavMeshLink area lompat jika MC mendekat,
    // tidak harus lewat tangga yang mungkin sedang dihalangi MC.
    [SerializeField] private bool useJumpEscape = true;
    // MC sedekat ini, lompatan dipertimbangkan (saat Kite, Reposition, atau Flee).
    [SerializeField] private float jumpTriggerDistance = 5f;
    // Jarak mendatar maksimal dari Shooter ke ujung awal link, supaya link dianggap ada di platform yang sama.
    [SerializeField] private float jumpLinkSearchRadius = 8f;
    // Selisih tinggi maksimal antara kaki Shooter dan ujung awal link.
    [SerializeField] private float jumpLinkHeightTolerance = 1f;
    // Setelah memilih lompatan, Shooter tetap menuju link selama ini walaupun skor aksi berubah.
    [SerializeField] private float jumpCommitDuration = 2.5f;
    // Shooter yang berdiri di ElevatedPlatform (menara) bertahan di sana dan menembak ke bawah.
    // Kite, Reposition, dan Search dimatikan selama MC tidak ikut naik ke platform yang sama.
    // Begitu MC naik (termasuk tangga di dalam objek menara), Shooter lompat turun lewat link.
    [SerializeField] private bool holdHighGround = true;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // Arah kabur yang dicoba berurutan, lurus menjauh lalu menyamping kalau terhalang.
    private static readonly float[] FleeAngles = { 0f, 45f, -45f, 90f, -90f };

    private readonly StringBuilder builder = new StringBuilder();

    private UtilitySelector selector;
    private UtilityAction fleeAction;
    private UtilityAction kiteAction;
    private UtilityAction repositionAction;
    private UtilityAction holdAction;
    private UtilityAction searchAction;

    private SearchRoutine search;
    private ProjectilePool pool;
    private PlayerStats playerStats;
    private PlayerHealth cachedPlayer;
    private Candidate[] candidates;
    private int obstacleMask;
    private float nextShotTime;
    // Tembakan yang animasinya sudah mulai tapi peluru belum keluar.
    private bool shotPending;
    private float shotReleaseTime;

    // Konteks yang dihitung ulang setiap tick, dipakai skor dan debug.
    private float idealDistance;
    private float tooClose;
    private float mobility = 1f;
    private float kiteEffect;
    private float coverValue;
    private bool nearGuardian;
    private Vector3 repositionPoint;
    private bool hasRepositionPoint;
    private Vector3 fleePoint;

    private static NavMeshLink[] sceneLinks;
    private static float sceneLinksCachedAt = float.NegativeInfinity;

    private ElevatedPlatform platform;
    private bool playerOnPlatform;

    private bool jumpCommitted;
    private float jumpCommitUntil;
    private Vector3 jumpLanding;

    public override string BrainName => "Utility/Shooter";

    public override bool IsScanning => search != null && search.CurrentPhase == SearchRoutine.Phase.Scan;

    public float IdealDistance => idealDistance;

    // Dibaca AI Debug overlay.
    public bool HasRepositionPoint => hasRepositionPoint;

    public Vector3 RepositionPoint => repositionPoint;

    public Vector3 FleePoint => fleePoint;

    public override string DebugDetail
    {
        get
        {
            if (selector == null)
                return string.Empty;

            builder.Clear();
            AppendScore("K", kiteAction).Append(' ');
            AppendScore("R", repositionAction).Append(' ');
            AppendScore("H", holdAction).Append('\n');
            AppendScore("F", fleeAction).Append(' ');
            AppendScore("S", searchAction);
            builder.Append("  mob ").Append(mobility.ToString("0.00"));

            return builder.ToString();
        }
    }

    public override void Initialize(EnemyController controller)
    {
        base.Initialize(controller);

        search = new SearchRoutine(Motor, transform);
        Motor.Landed += HandleLanded;
        candidates = new Candidate[Mathf.Max(4, candidateCount)];
        obstacleMask = GameLayers.ObstacleMask;

        fleeAction = new UtilityAction(EnemyAction.Flee, ScoreFlee, ExecuteFlee);
        kiteAction = new UtilityAction(EnemyAction.Kite, ScoreKite, ExecuteKite);
        repositionAction = new UtilityAction(EnemyAction.Reposition, ScoreReposition, ExecuteReposition);
        holdAction = new UtilityAction(EnemyAction.Hold, ScoreHold, ExecuteHold);
        searchAction = new UtilityAction(EnemyAction.Search, ScoreSearch, ExecuteSearch);

        selector = new UtilitySelector(
            new List<UtilityAction> { fleeAction, kiteAction, repositionAction, holdAction, searchAction },
            Data.UtilityDecisionInterval,
            Data.UtilityInertia
        );
    }

    public override void Tick(float deltaTime)
    {
        if (Controller.Health.IsDead)
            return;

        UpdateContext();

        selector.Tick();

        UpdateShooting();
    }

    public override void OnDied()
    {
        search.Cancel();
        jumpCommitted = false;
        shotPending = false;
        base.OnDied();
    }

    // Peluru dilepas per frame supaya tepat waktu, walau brain hanya tick 0,1-0,2 s.
    private void Update()
    {
        if (!shotPending || Time.time < shotReleaseTime)
            return;

        shotPending = false;

        PlayerHealth player = Blackboard != null ? Blackboard.Player : null;

        if (Controller.Health.IsDead || player == null || player.IsDead)
            return;

        // Dibidik ke posisi MC saat peluru keluar. MC yang bergerak selama animasi masih bisa menghindar.
        Fire(player);
    }

    private void OnDestroy()
    {
        if (Controller != null && Motor != null)
            Motor.Landed -= HandleLanded;
    }

    // ---------- Konteks ----------

    private void UpdateContext()
    {
        PlayerHealth player = Blackboard.Player;

        if (player != cachedPlayer)
        {
            cachedPlayer = player;
            playerStats = player != null ? player.GetComponent<PlayerStats>() : null;
        }

        platform = holdHighGround ? Controller.Perception.Platform : null;
        playerOnPlatform = platform != null && player != null &&
                           ElevatedPlatform.Below(player.transform.position) == platform;

        float playerRange = playerStats != null ? playerStats.Get(StatType.Range) : 8f;

        // DayaSerang MC tinggi membuat Shooter menjaga jarak lebih jauh.
        idealDistance = playerRange + Data.IdealDistanceOffset * Reactions.ShooterIdealOffsetMultiplier(Build);

        float distance = Blackboard.DistanceToPlayer;
        tooClose = Blackboard.IsAware ? TooClose(distance, idealDistance) : 0f;

        // Mobilitas dari indikator build di GroupBlackboard. Tanpa coordinator, dihitung langsung dari stat.
        if (TacticalCoordinator.Current != null)
            mobility = Build.mobility;
        else
            mobility = BuildIndicators.From(playerStats).mobility;

        kiteEffect = KiteEffect(mobility);

        if (Blackboard.CanSeePlayer && tooClose > 0f)
            EvaluateRepositionPoints(player.transform.position);
        else
            hasRepositionPoint = false;
    }

    // Titik kandidat di lingkaran ideal sekitar MC. Skor: ada LOS ke MC, bergeser ke samping (bukan mundur lurus),
    // dan tidak terlalu jauh dari posisi sekarang. coverValue = skor kandidat terbaik.
    private void EvaluateRepositionPoints(Vector3 playerPosition)
    {
        Vector3 fromPlayer = transform.position - playerPosition;
        fromPlayer.y = 0f;

        float currentBearing = Mathf.Atan2(fromPlayer.x, fromPlayer.z) * Mathf.Rad2Deg;

        int best = -1;

        for (int i = 0; i < candidates.Length; i++)
        {
            float bearing = currentBearing + 360f * i / candidates.Length;
            Vector3 point = playerPosition + Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * idealDistance;

            candidates[i].valid = NavMesh.SamplePosition(point, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas);

            if (!candidates[i].valid)
                continue;

            candidates[i].position = hit.position;

            float los = HasLineOfSightFrom(hit.position, cachedPlayer) ? 1f : 0f;

            float angle = Mathf.Abs(Mathf.DeltaAngle(currentBearing, bearing));
            float sidestep = 1f - Mathf.Abs(angle - 90f) / 90f;

            float travel = 1f - Mathf.Clamp01(Vector3.Distance(hit.position, transform.position) / maxTravelDistance);

            candidates[i].score = 0.4f * los + 0.35f * Mathf.Clamp01(sidestep) + 0.25f * travel;

            if (best < 0 || candidates[i].score > candidates[best].score)
                best = i;
        }

        hasRepositionPoint = best >= 0;
        coverValue = hasRepositionPoint ? candidates[best].score : 0f;

        if (hasRepositionPoint)
        {
            repositionPoint = candidates[best].position;
            nearGuardian = IsNearGuardian(repositionPoint);
        }
        else
        {
            nearGuardian = false;
        }
    }

    private bool IsNearGuardian(Vector3 point)
    {
        float sqrRadius = Data.GuardianBonusRadius * Data.GuardianBonusRadius;

        foreach (EnemyController ally in EnemyController.Active)
        {
            if (ally == Controller || ally.Role != EnemyRole.Guardian || ally.Health.IsDead)
                continue;

            if ((ally.transform.position - point).sqrMagnitude <= sqrRadius)
                return true;
        }

        return false;
    }

    private bool HasLineOfSightFrom(Vector3 point, PlayerHealth player)
    {
        if (player == null)
            return false;

        Vector3 eye = point + Vector3.up * Data.EyeHeight * transform.lossyScale.y;
        return !Physics.Linecast(eye, player.AimPoint, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    // ---------- Skor ----------

    // Rumus dibuat static supaya bisa diuji di Editor terhadap contoh di dokumen desain.
    public static float TooClose(float distance, float ideal)
    {
        return ideal > 0f ? Mathf.Clamp01((ideal - distance) / ideal) : 0f;
    }

    public static float KiteEffect(float mobility)
    {
        return Mathf.Clamp01(1.5f - mobility);
    }

    public static float KiteScore(float tooClose, float mobility)
    {
        return tooClose * KiteEffect(mobility);
    }

    public static float RepositionScore(float tooClose, float mobility, float coverValue)
    {
        return tooClose * (1f - KiteEffect(mobility)) * coverValue;
    }

    private float ScoreFlee()
    {
        if (!Blackboard.IsAware)
            return 0f;

        // Melawan DayaSerang tinggi Shooter lebih cepat kabur, melawan DayaSerang rendah lebih sabar.
        float shiftedHealth = Mathf.Clamp01(Controller.Health.HealthRatio - Reactions.ShooterFleeShift(Build));

        return Data.FleeByHealth.Evaluate(shiftedHealth);
    }

    // Di menara dan MC masih di bawah: tetap di tempat, jangan turun mengejar jarak ideal.
    private bool StayOnHighGround => platform != null && !playerOnPlatform;

    private float ScoreKite()
    {
        if (StayOnHighGround)
            return 0f;

        return Blackboard.CanSeePlayer ? KiteScore(tooClose, mobility) : 0f;
    }

    private float ScoreReposition()
    {
        if (StayOnHighGround)
            return 0f;

        if (!Blackboard.CanSeePlayer || !hasRepositionPoint)
            return 0f;

        float bonus = nearGuardian && tooClose > 0f ? Data.GuardianProximityBonus : 0f;

        return RepositionScore(tooClose, mobility, coverValue) + bonus;
    }

    private float ScoreHold()
    {
        return Data.HoldScore;
    }

    private float ScoreSearch()
    {
        if (StayOnHighGround)
            return 0f;

        if (Blackboard.CanSeePlayer || !Blackboard.HasLastKnownPosition || Blackboard.MemoryDuration <= 0f)
            return 0f;

        // Kesabaran mencari meluruh. Di bawah skor Hold, Shooter berhenti mencari dan diam.
        return 0.8f * Blackboard.MemoryRemaining / Blackboard.MemoryDuration;
    }

    // ---------- Eksekusi ----------

    private void ExecuteFlee()
    {
        search.Cancel();
        SetAction(EnemyAction.Flee);

        PlayerHealth player = Blackboard.Player;
        Vector3 threat = player != null ? player.transform.position : Blackboard.LastKnownPosition;

        if (TryJumpEscape(threat))
            return;

        Vector3 away = transform.position - threat;
        away.y = 0f;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;

        foreach (float offset in FleeAngles)
        {
            Vector3 point = transform.position + Quaternion.Euler(0f, offset, 0f) * away * Data.FleeDistance;

            if (NavMesh.SamplePosition(point, out NavMeshHit hit, navMeshSampleRadius * 2f, NavMesh.AllAreas))
            {
                fleePoint = hit.position;
                break;
            }
        }

        Motor.ClearFacing();
        Motor.MoveTo(fleePoint, 1.1f, 0.2f);
    }

    private void ExecuteKite()
    {
        search.Cancel();

        // MC hilang di antara dua keputusan, minta keputusan baru.
        if (!Blackboard.CanSeePlayer)
        {
            selector.RequestDecision();
            return;
        }

        SetAction(EnemyAction.Kite);

        Vector3 playerPosition = Blackboard.Player.transform.position;

        if (TryJumpEscape(playerPosition))
            return;
        Vector3 away = transform.position - playerPosition;
        away.y = 0f;

        if (away.sqrMagnitude < 0.0001f)
            away = -transform.forward;

        Vector3 target = playerPosition + away.normalized * idealDistance;

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, navMeshSampleRadius * 2f, NavMesh.AllAreas))
            Motor.MoveTo(hit.position, 1f, 0.2f);

        // Mundur sambil tetap menghadap MC supaya bisa menembak.
        Motor.FacePosition(playerPosition);
    }

    private void ExecuteReposition()
    {
        search.Cancel();

        if (!Blackboard.CanSeePlayer)
        {
            selector.RequestDecision();
            return;
        }

        SetAction(EnemyAction.Reposition);

        if (TryJumpEscape(Blackboard.Player.transform.position))
            return;

        if (hasRepositionPoint)
            Motor.MoveTo(repositionPoint, 1f, 0.2f);

        Motor.FacePosition(Blackboard.Player.transform.position);
    }

    private void ExecuteHold()
    {
        search.Cancel();

        if (!Blackboard.CanSeePlayer)
        {
            SetAction(EnemyAction.Idle);
            Motor.Stop();
            Motor.ClearFacing();
            return;
        }

        Vector3 playerPosition = Blackboard.Player.transform.position;

        // Di menara: tahan posisi dan tembak ke bawah, atau lompat turun jika MC ikut naik.
        if (platform != null)
        {
            if (TryJumpEscape(playerPosition))
                return;

            SetAction(EnemyAction.Hold);
            Motor.Stop();
            Motor.FacePosition(playerPosition);
            return;
        }

        SetAction(EnemyAction.Hold);

        // Dengan coordinator, posisi tahan adalah slot di ring Shooter (dibagi supaya tidak bertumpuk dengan rekan).
        if (Controller.HasSlot)
        {
            Motor.MoveTo(Controller.SlotPosition, 1f, 0.2f);
        }
        // Terlalu jauh untuk menekan MC, maju sampai jarak ideal.
        else if (Blackboard.DistanceToPlayer > idealDistance + 1.5f)
        {
            Vector3 toShooter = transform.position - playerPosition;
            toShooter.y = 0f;
            Motor.MoveTo(playerPosition + toShooter.normalized * idealDistance, 1f, 0.2f);
        }
        else
        {
            Motor.Stop();
        }

        Motor.FacePosition(playerPosition);
    }

    private void ExecuteSearch()
    {
        if (!Blackboard.HasLastKnownPosition)
            return;

        if (!search.IsActive || (search.Target - Blackboard.LastKnownPosition).sqrMagnitude > 4f)
        {
            if (!search.Begin(Blackboard.LastKnownPosition, Data))
            {
                Blackboard.ClearLastKnownPosition();
                return;
            }
        }

        SetAction(EnemyAction.Search);

        if (search.Tick() != NodeState.Running)
        {
            Blackboard.ClearLastKnownPosition();
            selector.RequestDecision();
        }
    }

    // ---------- Lompat turun ----------

    // Jika MC dekat dan Shooter berdiri di platform yang punya link lompat, pilih link yang mendaratkan
    // Shooter paling jauh dari MC lalu jalan ke sana. Path lewat link karena biaya area lompat dibuat murah.
    private bool TryJumpEscape(Vector3 threat)
    {
        if (!useJumpEscape || !Motor.CanUseJumpLinks)
            return false;

        if (!jumpCommitted || Time.time > jumpCommitUntil)
        {
            jumpCommitted = !Motor.IsBusyJumping && FindJumpEscape(threat, out jumpLanding);

            if (!jumpCommitted)
                return false;

            jumpCommitUntil = Time.time + jumpCommitDuration;
        }

        SetAction(EnemyAction.Flee);
        fleePoint = jumpLanding;

        Motor.ClearFacing();
        Motor.MoveTo(jumpLanding, 1.1f, 0.2f, true);

        return true;
    }

    private bool FindJumpEscape(Vector3 threat, out Vector3 landing)
    {
        landing = Vector3.zero;

        // Di menara bertanda ElevatedPlatform: lompat hanya jika MC naik ke platform yang sama.
        // Tanpa penanda: lompat jika MC sudah sedekat Jump Trigger Distance.
        bool threatened = platform != null
            ? playerOnPlatform
            : Blackboard.DistanceToPlayer <= jumpTriggerDistance;

        if (!threatened)
            return false;

        Vector3 position = transform.position;
        float currentThreatDistance = FlatDistance(position, threat);
        float bestScore = float.NegativeInfinity;
        bool found = false;

        foreach (NavMeshLink link in SceneLinks())
        {
            if (link == null || !link.isActiveAndEnabled || link.area != Motor.JumpArea)
                continue;

            GetLinkEnds(link, out Vector3 start, out Vector3 end);

            found |= ConsiderJump(start, end, position, threat, currentThreatDistance, ref bestScore, ref landing);

            if (link.bidirectional)
                found |= ConsiderJump(end, start, position, threat, currentThreatDistance, ref bestScore, ref landing);
        }

        return found;
    }

    private bool ConsiderJump(Vector3 from, Vector3 to, Vector3 position, Vector3 threat,
        float currentThreatDistance, ref float bestScore, ref Vector3 landing)
    {
        // Ujung awal harus di platform yang sama, dan lompatan harus turun (bukan naik).
        if (Mathf.Abs(from.y - position.y) > jumpLinkHeightTolerance || to.y > position.y + 0.1f)
            return false;

        float walk = FlatDistance(from, position);

        if (walk > jumpLinkSearchRadius)
            return false;

        // Titik mendarat harus lebih jauh dari MC daripada posisi sekarang, dan jalan ke link tidak melewati MC.
        float landingThreatDistance = FlatDistance(to, threat);

        if (landingThreatDistance <= currentThreatDistance || FlatDistance(from, threat) < walk * 0.5f)
            return false;

        float score = landingThreatDistance - walk * 0.5f;

        if (score <= bestScore)
            return false;

        if (!NavMesh.SamplePosition(to, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
            return false;

        bestScore = score;
        landing = hit.position;
        return true;
    }

    private void HandleLanded()
    {
        jumpCommitted = false;
        selector.RequestDecision();
    }

    private static NavMeshLink[] SceneLinks()
    {
        // Dicari ulang tiap beberapa detik supaya arena yang baru aktif ikut terbaca.
        if (sceneLinks == null || Time.time - sceneLinksCachedAt > 5f || Time.time < sceneLinksCachedAt)
        {
            sceneLinks = FindObjectsByType<NavMeshLink>();
            sceneLinksCachedAt = Time.time;
        }

        return sceneLinks;
    }

    private static void GetLinkEnds(NavMeshLink link, out Vector3 start, out Vector3 end)
    {
        Transform t = link.transform;

        start = link.startTransform != null ? link.startTransform.position : t.position + t.rotation * link.startPoint;
        end = link.endTransform != null ? link.endTransform.position : t.position + t.rotation * link.endPoint;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ---------- Menembak ----------

    private void UpdateShooting()
    {
        // Token dipegang selama MC terlihat. Saat kehilangan MC atau kabur, token dilepas untuk musuh lain.
        if (!Blackboard.CanSeePlayer || CurrentAction == EnemyAction.Flee || Motor.IsBusyJumping)
        {
            if (Controller.HasAttackToken)
                Controller.ReleaseAttackToken();

            return;
        }

        if (shotPending || Time.time < nextShotTime)
            return;

        if (!Controller.RequestAttackToken())
            return;

        StartShot();
        nextShotTime = Time.time + Data.AttackCooldown;
    }

    private void Fire(PlayerHealth player)
    {
        if (pool == null && !ProjectilePool.TryGet(projectilePoolId, out pool))
        {
            Debug.LogWarning(name + ": ProjectilePool dengan ID '" + projectilePoolId + "' tidak ditemukan di scene.", this);
            nextShotTime = Time.time + 2f;
            return;
        }

        Projectile projectile = pool.Spawn();

        if (projectile == null)
            return;

        Vector3 origin = transform.TransformPoint(muzzleOffset);

        // Peluru tidak tertahan lantai menara tempat Shooter berdiri.
        ElevatedPlatform standingOn = Controller.Perception.Platform;

        projectile.Launch(
            pool,
            origin,
            player.AimPoint - origin,
            player,
            Data.Damage,
            gameObject,
            DamageKind.Projectile,
            standingOn != null ? standingOn.transform : null
        );
    }

    // Animasi tembak dimulai sekarang, peluru menyusul setelah Shot Release Delay.
    private void StartShot()
    {
        NotifyAttack();

        if (Data.ShotReleaseDelay <= 0f)
        {
            Fire(Blackboard.Player);
            return;
        }

        shotPending = true;
        shotReleaseTime = Time.time + Data.ShotReleaseDelay;
    }

    private StringBuilder AppendScore(string label, UtilityAction action)
    {
        bool chosen = selector.Current == action;

        if (chosen)
            builder.Append("<b><color=#ffee55>");

        builder.Append(label).Append(action.Score.ToString(".00"));

        if (chosen)
            builder.Append("</color></b>");

        return builder;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !Application.isPlaying || Controller == null)
            return;

        PlayerHealth player = Blackboard.Player;

        if (player != null && Blackboard.IsAware)
        {
            // Lingkaran jarak ideal di sekitar MC.
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
            GizmoShapes.Circle(player.transform.position + Vector3.up * 0.05f, idealDistance);
        }

        if (hasRepositionPoint && candidates != null)
        {
            foreach (Candidate candidate in candidates)
            {
                if (!candidate.valid)
                    continue;

                Gizmos.color = Color.Lerp(Color.red, Color.green, candidate.score);
                Gizmos.DrawSphere(candidate.position + Vector3.up * 0.05f, 0.08f);
            }

            Gizmos.color = nearGuardian ? Color.green : Color.cyan;
            Gizmos.DrawWireSphere(repositionPoint + Vector3.up * 0.05f, 0.25f);
        }

        if (CurrentAction == EnemyAction.Flee)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, fleePoint + Vector3.up * 0.1f);
        }
    }
}
