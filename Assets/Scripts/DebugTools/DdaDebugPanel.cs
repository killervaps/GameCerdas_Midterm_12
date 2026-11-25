using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// Panel OnGUI untuk DDA di kanan atas: mode, faktor, skor ronde terakhir, knob yang berlaku, dan riwayat faktor.
// Hotkey (F5 dan F6 sudah dipakai PcgDebugPanel):
//   F4  ganti mode Off -> Adaptive -> ForceEasy -> ForceHard
//   F7  MC menerima 25 damage, untuk mensimulasikan pemain yang kesulitan
//   F8  reset death streak
public class DdaDebugPanel : MonoBehaviour
{
    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private PlayerPerformanceTracker tracker;
    [SerializeField] private RunManager runManager;

    [Header("Layout")]
    [SerializeField] private bool showPanel = true;
    // Aktif = panel hanya tampil saat AI Debug Mode menyala (F1).
    [SerializeField] private bool followDebugToggle;
    [SerializeField] private Vector2 margin = new Vector2(10f, 10f);
    [SerializeField] private float width = 330f;
    [SerializeField] private float uiScale = 1f;
    [SerializeField] private float refreshInterval = 0.1f;
    // AIDebugPanel dan GateRarityDebugPanel juga di kanan atas saat F1 menyala. Panel DDA digeser ke kiri selebar ini.
    [SerializeField, Min(0f)] private float shiftWhenAIDebug = 570f;

    [Header("Hotkey")]
    [SerializeField] private bool hotkeys = true;
    [SerializeField, Min(0f)] private float debugDamage = 25f;

    private const float LineHeight = 17f;
    private const float BarHeight = 12f;

    private readonly StringBuilder builder = new StringBuilder();

    private string headerText = string.Empty;
    private string bodyText = string.Empty;
    private int headerLines;
    private int bodyLines;
    private float nextRefresh;
    private float modeHintUntil;
    private GUIStyle style;

    private bool Visible => showPanel && (!followDebugToggle || AIDebugSettings.Enabled);

    private void Awake()
    {
        if (tracker == null)
            tracker = FindAnyObjectByType<PlayerPerformanceTracker>();

        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();
    }

    private void Update()
    {
        DifficultyModel model = DifficultyModel.Current;

        if (hotkeys && Keyboard.current != null && model != null)
        {
            if (Keyboard.current.f4Key.wasPressedThisFrame)
            {
                model.CycleMode();
                modeHintUntil = Time.unscaledTime + 5f;
            }
            else if (Keyboard.current.f7Key.wasPressedThisFrame)
                DamagePlayer();
            else if (Keyboard.current.f8Key.wasPressedThisFrame)
                model.ResetDeathStreak();
        }

        if (Visible && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            BuildText(model);
        }
    }

    private void DamagePlayer()
    {
        PlayerHealth player = PlayerHealth.Current;

        if (player == null || player.IsDead)
            return;

        float dealt = player.TakeDamage(new DamageInfo(debugDamage, null, player.AimPoint, DamageKind.Generic));
        Debug.Log("DdaDebugPanel: F7, MC menerima " + dealt.ToString("0.#") + " damage (simulasi pemain kesulitan)", this);
    }

    private void BuildText(DifficultyModel model)
    {
        builder.Clear();

        if (model == null)
        {
            headerText = "<b>DDA</b>  <color=#ff6655>DifficultyModel tidak ada di scene</color>";
            headerLines = 1;
            bodyText = string.Empty;
            bodyLines = 0;
            return;
        }

        builder.Append("<b>DDA</b>  mode ").Append(ModeLabel(model.Mode))
            .Append("  <size=10><color=#aaaaaa>F4 mode | F7 damage | F8 reset streak</color></size>\n");
        builder.Append("Faktor <b>").Append(model.Factor.ToString("0.00")).Append("</b>  <color=#aaaaaa>(")
            .Append(model.MinFactor.ToString("0.00")).Append(" - ").Append(model.MaxFactor.ToString("0.00"))
            .Append("), death streak ").Append(model.DeathStreak).Append("</color>");
        headerText = builder.ToString();
        headerLines = 2;

        builder.Clear();
        int lines = 0;

        IReadOnlyList<WavePerformance> history = model.History;

        if (history.Count > 0)
        {
            WavePerformance last = history[history.Count - 1];
            builder.Append("Ronde ").Append(last.round).Append(' ').Append(last.objective).Append(last.died ? " <color=#ff6655>(mati)</color>" : string.Empty)
                .Append("  skor <b>").Append(last.score.ToString("0.00")).Append("</b>  EMA ").Append(model.SmoothedScore.ToString("0.00")).Append('\n');
            builder.Append("  HP ").Append(last.hpScore.ToString("0.00"))
                .Append("  damage ").Append(last.damageScore.ToString("0.00"))
                .Append("  waktu ").Append(last.objective == ObjectiveType.Survival ? "-" : last.timeScore.ToString("0.00"))
                .Append("  hampir mati ").Append(last.nearDeathCount).Append('\n');
            lines += 2;
        }
        else
        {
            builder.Append("<color=#aaaaaa>Belum ada ronde yang dinilai</color>\n");
            lines++;
        }

        if (model.Active)
        {
            int bonus = model.ItemBonus;
            builder.Append("Knob  musuh x").Append(model.EnemyMultiplier.ToString("0.00"))
                .Append(", item ").Append(bonus >= 0 ? "+" : string.Empty).Append(bonus)
                .Append(", heal ").Append(Mathf.RoundToInt(model.HealRatio * 100f)).Append('%')
                .Append(", token ").Append(model.MaxTokens)
                .Append(", waypoint ").Append(model.WaypointDelay.ToString("0")).Append(" s\n");
        }
        else
        {
            builder.Append("Knob  <color=#aaaaaa>default (DDA mati), waypoint ").Append(model.WaypointDelay.ToString("0")).Append(" s</color>\n");
        }

        lines++;

        EnemyGroupWaypoint waypoint = EnemyGroupWaypoint.Current;
        GoalIndicator goal = GoalIndicator.Current;

        if (waypoint != null && waypoint.IsTracking)
            builder.Append("Waypoint  ").Append(waypoint.DebugStatus).Append('\n');
        else if (goal != null && goal.IsTracking)
            builder.Append("Waypoint  ").Append(goal.DebugStatus).Append('\n');
        else
            builder.Append("Waypoint  <color=#aaaaaa>tidak aktif</color>\n");

        lines++;

        builder.Append("Riwayat ");
        IReadOnlyList<float> factors = model.FactorHistory;

        for (int i = 0; i < factors.Count; i++)
        {
            if (i > 0)
                builder.Append(" -> ");

            builder.Append(factors[i].ToString("0.00"));
        }

        if (factors.Count == 0)
            builder.Append('-');

        lines++;

        if (tracker != null && tracker.Recording)
        {
            WavePerformance live = tracker.Live;
            builder.Append("\n<color=#ffaa33>Ronde ").Append(live.round).Append(" berjalan</color>  kill ")
                .Append(live.kills).Append('/').Append(live.enemyCount)
                .Append(", damage ").Append(live.damageTaken.ToString("0"))
                .Append(", ").Append(live.duration.ToString("0")).Append(" s")
                .Append(", hampir mati ").Append(live.nearDeathCount);
            lines++;
        }

        // Komposisi ronde berikutnya sudah dibuat saat masuk Safezone, jadi mode baru baru terlihat setelah regenerate.
        if (runManager != null && runManager.State == RunState.Preparing && Time.unscaledTime < modeHintUntil)
        {
            builder.Append("\n<size=10><color=#ffdd66>Mode diganti di Safezone. Tekan F5 (panel PCG, F1 menyala) supaya preview musuh ikut berubah.</color></size>");
            lines += 2;
        }

        bodyText = builder.ToString();
        bodyLines = lines;
    }

    private static string ModeLabel(DdaMode mode)
    {
        switch (mode)
        {
            case DdaMode.Adaptive: return "<color=#66ff88>Adaptive</color>";
            case DdaMode.ForceEasy: return "<color=#66ccff>ForceEasy</color>";
            case DdaMode.ForceHard: return "<color=#ff6655>ForceHard</color>";
            default: return "<color=#aaaaaa>Off</color>";
        }
    }

    private void OnGUI()
    {
        if (!Visible)
            return;

        if (style == null)
            style = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 12 };

        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        float headerHeight = headerLines * LineHeight;
        float bodyHeight = bodyLines * LineHeight;
        float height = 8f + headerHeight + 4f + BarHeight + 6f + bodyHeight + 8f;
        float x = Screen.width / uiScale - width - margin.x - (AIDebugSettings.Enabled ? shiftWhenAIDebug : 0f);
        float y = margin.y;

        GUI.Box(new Rect(x, y, width, height), GUIContent.none);

        float inner = width - 16f;
        float cursor = y + 6f;

        GUI.Label(new Rect(x + 8f, cursor, inner, headerHeight + 2f), headerText, style);
        cursor += headerHeight + 4f;

        DifficultyModel model = DifficultyModel.Current;

        if (model != null)
            DrawFactorBar(new Rect(x + 8f, cursor, inner, BarHeight), model);

        cursor += BarHeight + 6f;

        GUI.Label(new Rect(x + 8f, cursor, inner, bodyHeight + 4f), bodyText, style);
    }

    // Bar faktor dari Min Factor ke Max Factor. Garis putih = faktor 1, isi = faktor sekarang.
    private static void DrawFactorBar(Rect rect, DifficultyModel model)
    {
        Color previous = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        float t = Mathf.InverseLerp(model.MinFactor, model.MaxFactor, model.Factor);
        float neutral = Mathf.InverseLerp(model.MinFactor, model.MaxFactor, 1f);

        GUI.color = !model.Active ? new Color(0.6f, 0.6f, 0.6f) : model.Factor < 1f ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.45f, 0.35f);
        GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, (rect.width - 2f) * t, rect.height - 2f), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(rect.x + rect.width * neutral - 1f, rect.y - 2f, 2f, rect.height + 4f), Texture2D.whiteTexture);

        GUI.color = previous;
    }
}
