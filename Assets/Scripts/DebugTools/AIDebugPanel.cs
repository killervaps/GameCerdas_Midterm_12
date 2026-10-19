using System.Text;
using UnityEngine;

// Panel layar AI Debug Mode (tampil saat F1 aktif). Isinya:
//   ronde dan status run, indikator build MC, ThreatRatio live, reaksi build yang aktif,
//   token (pemegang dan antrean), tabel semua musuh (aksi, HP, persepsi, token/slot, skor), dan legenda warna overlay.
public class AIDebugPanel : MonoBehaviour
{
    [Header("References (opsional)")]
    [SerializeField] private RunManager runManager;
    [SerializeField] private AIDebugOverlay overlay;

    [Header("Layout")]
    [SerializeField] private float width = 560f;
    [SerializeField] private float margin = 10f;
    [SerializeField] private float uiScale = 1f;
    [SerializeField] private bool showLegend = true;
    [SerializeField] private float refreshInterval = 0.1f;

    private readonly StringBuilder builder = new StringBuilder();

    private string text = string.Empty;
    private readonly GUIContent content = new GUIContent();
    private float nextRefresh;
    private GUIStyle style;

    private void Update()
    {
        if (!AIDebugSettings.Enabled || Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + refreshInterval;
        text = BuildText();
    }

    private string BuildText()
    {
        builder.Clear();

        builder.Append("<b>AI DEBUG MODE</b>  <size=11><color=#aaaaaa>F1 tutup  |  F2 slow motion");

        if (overlay != null && overlay.IsSlowMotion)
            builder.Append(" <color=#ffcc33>(aktif)</color>");

        builder.Append("</color></size>\n");

        AppendRun();
        AppendGroup();
        AppendTokens();
        AppendEnemies();

        if (showLegend)
            AppendLegend();

        return builder.ToString();
    }

    private void AppendRun()
    {
        if (runManager == null)
            return;

        builder.Append("Ronde ").Append(Mathf.Max(1, runManager.CurrentRound)).Append('/').Append(runManager.TotalRounds)
            .Append("  ").Append(runManager.State).Append('\n');
    }

    private void AppendGroup()
    {
        TacticalCoordinator coordinator = TacticalCoordinator.Current;

        if (coordinator == null)
        {
            builder.Append("<color=#ff8888>TacticalCoordinator tidak ada di scene</color>\n");
            return;
        }

        GroupBlackboard group = coordinator.Group;
        BuildIndicators build = group.Indicators;

        builder.Append("<b>Build MC</b>  Mob ").Append(Indicator(build.mobility))
            .Append("  Tahan ").Append(Indicator(build.durability))
            .Append("  Serang ").Append(Indicator(build.offense))
            .Append("  Jangkau ").Append(Indicator(build.reach)).Append('\n');

        if (group.HasThreat)
        {
            ThreatResult threat = group.Threat;

            builder.Append("<b>Ancaman live</b>  <color=").Append(threat.ColorHex).Append("><b>").Append(threat.Label)
                .Append("</b></color> ").Append(threat.ratio.ToString("0.00"))
                .Append("  <color=#aaaaaa>(Power musuh ").Append(threat.enemyPower.ToString("0"))
                .Append(" / MC ").Append(threat.playerPower.ToString("0")).Append(")</color>\n");
        }

        builder.Append("<b>Reaksi</b>  <color=#cccccc>").Append(coordinator.Reactions.Describe(build)).Append("</color>\n");
    }

    private void AppendTokens()
    {
        TacticalCoordinator coordinator = TacticalCoordinator.Current;

        if (coordinator == null)
            return;

        AttackTokenManager tokens = coordinator.Tokens;

        builder.Append("<b>Token</b> ").Append(tokens.Holders.Count).Append('/').Append(tokens.MaxTokens).Append("  ");

        foreach (AttackTokenManager.Holder holder in tokens.Holders)
        {
            if (holder.enemy == null)
                continue;

            builder.Append("<color=#ffee55>").Append(holder.enemy.name).Append(' ')
                .Append((Time.time - holder.since).ToString("0.0")).Append("s</color>  ");
        }

        if (tokens.Waiters.Count > 0)
        {
            builder.Append(" antre ");

            foreach (AttackTokenManager.Waiter waiter in tokens.Waiters)
            {
                if (waiter.enemy == null)
                    continue;

                builder.Append(waiter.enemy.name).Append(" (p ").Append(waiter.priority.ToString("0.0")).Append(")  ");
            }
        }

        builder.Append('\n');
    }

    private void AppendEnemies()
    {
        builder.Append("\n<b>Musuh</b>\n");

        int count = 0;

        foreach (EnemyController enemy in EnemyController.Active)
        {
            if (enemy == null || enemy.RoleData == null)
                continue;

            count++;

            EnemyBrain brain = enemy.Brain;
            EnemyBlackboard bb = enemy.Blackboard;

            builder.Append("<color=").Append(RoleColors.Hex(enemy.Role)).Append("><b>").Append(enemy.name).Append("</b></color> ");

            if (brain != null)
                builder.Append("<color=#aaaaaa>[").Append(brain.BrainName).Append("]</color> <b>").Append(brain.CurrentAction).Append("</b>");

            builder.Append("  HP ").Append(enemy.Health.CurrentHealth.ToString("0")).Append('/').Append(enemy.Health.MaxHealth.ToString("0"));

            if (enemy.Health.IsDead)
                builder.Append(" <color=#888888>mati</color>");
            else if (bb.CanSeePlayer)
                builder.Append("  <color=#ff5555>LIHAT</color> ").Append(bb.DistanceToPlayer.ToString("0.0")).Append("m");
            else if (bb.HasLastKnownPosition)
                builder.Append("  <color=#ffaa33>INGAT ").Append(bb.MemoryRemaining.ToString("0.0")).Append("s</color>");
            else
                builder.Append("  <color=#888888>tidak tahu</color>");

            AppendTactical(enemy);

            if (brain != null)
            {
                string detail = brain.DebugDetail;

                if (!string.IsNullOrEmpty(detail))
                    builder.Append("\n    <size=11>").Append(detail.Replace("\n", "  /  ")).Append("</size>");
            }

            builder.Append('\n');
        }

        if (count == 0)
            builder.Append("<color=#888888>tidak ada musuh aktif</color>\n");
    }

    private void AppendTactical(EnemyController enemy)
    {
        TacticalCoordinator coordinator = TacticalCoordinator.Current;

        if (coordinator == null || enemy.Health.IsDead)
            return;

        if (enemy.HasAttackToken)
        {
            builder.Append("  <color=#ffee55>TOKEN</color>");
        }
        else
        {
            int queue = coordinator.Tokens.QueuePosition(enemy);

            if (queue > 0)
                builder.Append("  antre #").Append(queue);
        }

        if (enemy.HasSlot)
            builder.Append("  slot ").Append(enemy.SlotAngle.ToString("0")).Append("°");
    }

    private void AppendLegend()
    {
        builder.Append("\n<size=11><color=#aaaaaa>")
            .Append("Cone kuning = tidak melihat, merah = melihat MC.  LOS hijau = terlihat, merah = terhalang.\n")
            .Append("Silang oranye = LKP (pudar mendekati lupa).  Garis putih tipis = jalur NavMesh.\n")
            .Append("Bola warna role = slot.  Garis kuning = pemegang token.  Lingkaran oranye Charger = lungeRange.\n")
            .Append("Ring biru di MC = jarak ideal Shooter.  Guardian: oranye aggro, biru leash (merah saat pulang), silang kuning interpose.\n")
            .Append("Cyan di MC = Range target, kuning = Aura Burst siap.")
            .Append("</color></size>");
    }

    // Hijau di atas build dasar, merah di bawah, putih jika netral.
    private static string Indicator(float value)
    {
        string color = value > 1.05f ? "#66ff66" : value < 0.95f ? "#ff7777" : "#ffffff";
        return "<color=" + color + ">" + value.ToString("0.00") + "</color>";
    }

    private void OnGUI()
    {
        if (!AIDebugSettings.Enabled)
            return;

        if (style == null)
            style = new GUIStyle(GUI.skin.box) { richText = true, alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 13 };

        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        float screenWidth = Screen.width / uiScale;
        float screenHeight = Screen.height / uiScale;

        content.text = text;
        float height = Mathf.Min(style.CalcHeight(content, width), screenHeight - margin * 2f);

        GUI.Box(new Rect(screenWidth - width - margin, margin, width, height), content, style);
    }
}
