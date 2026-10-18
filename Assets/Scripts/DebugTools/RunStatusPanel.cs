using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// Panel OnGUI sementara untuk alur run: ronde, status, sisa musuh, preview wave berikutnya,
// dan layar hasil menang/kalah dengan tombol main lagi. Diganti uGUI di fase UI.
public class RunStatusPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RunManager runManager;
    [SerializeField] private BattleManager battle;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerStats playerStats;

    [Header("Layout")]
    [SerializeField] private bool showPanel = true;
    [SerializeField] private Vector2 position = new Vector2(10f, 10f);
    [SerializeField] private float uiScale = 1f;
    [SerializeField] private float refreshInterval = 0.1f;

    private readonly StringBuilder builder = new StringBuilder();

    private string statusText = string.Empty;
    private string resultText = string.Empty;
    private float nextRefresh;
    private bool retryRequested;
    private GUIStyle boxStyle;
    private GUIStyle resultStyle;

    private bool ShowingResult =>
        runManager != null &&
        (runManager.State == RunState.Victory || runManager.State == RunState.Defeat);

    private void OnEnable()
    {
        if (runManager != null)
            runManager.StateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        if (runManager != null)
            runManager.StateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (runManager == null)
            return;

        // Tombol ditunda ke Update supaya isi OnGUI tidak berubah di tengah event Layout/Repaint.
        bool enterPressed = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;

        if (ShowingResult && (retryRequested || enterPressed))
            runManager.StartRun();

        retryRequested = false;

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            statusText = BuildStatus();
        }
    }

    private void HandleStateChanged(RunState state)
    {
        resultText = state == RunState.Victory || state == RunState.Defeat
            ? BuildResult(state)
            : string.Empty;

        statusText = BuildStatus();
    }

    private string BuildStatus()
    {
        builder.Clear();
        builder.Append("<b>Ronde ").Append(Mathf.Max(1, runManager.CurrentRound))
            .Append('/').Append(runManager.TotalRounds).Append("</b>  ").Append(StateLabel(runManager.State)).Append('\n');

        switch (runManager.State)
        {
            case RunState.Preparing:
                builder.Append("Pilih gate di Safezone (tahan E)\n");
                builder.Append("Musuh berikutnya  ");
                AppendWave(runManager.CurrentWave);
                builder.Append('\n');
                break;

            case RunState.Battle:
                if (battle != null)
                    builder.Append("Musuh tersisa  ").Append(battle.AliveCount).Append('/').Append(battle.TotalCount).Append('\n');
                break;

            case RunState.RoundCleared:
                builder.Append("Ronde bersih, kembali ke Safezone...\n");
                break;
        }

        if (playerHealth != null)
        {
            builder.Append("HP ").Append(playerHealth.CurrentHealth.ToString("0"))
                .Append('/').Append(playerHealth.MaxHealth.ToString("0")).Append('\n');
        }

        builder.Append("Waktu ").Append(runManager.RunDuration.ToString("0.0")).Append(" s");

        return builder.ToString();
    }

    private string BuildResult(RunState state)
    {
        builder.Clear();

        if (state == RunState.Victory)
            builder.Append("<size=28><b><color=#66ff66>MENANG</color></b></size>\n");
        else
            builder.Append("<size=28><b><color=#ff6666>KALAH</color></b></size>\n");

        builder.Append("Ronde ").Append(runManager.CurrentRound).Append('/').Append(runManager.TotalRounds)
            .Append("   Waktu ").Append(runManager.RunDuration.ToString("0.0")).Append(" s\n");

        if (playerHealth != null)
        {
            builder.Append("Sisa HP ").Append(playerHealth.CurrentHealth.ToString("0"))
                .Append('/').Append(playerHealth.MaxHealth.ToString("0")).Append('\n');
        }

        // TODO(desain): rumus skor belum ditentukan. Sementara ditampilkan waktu dan sisa HP saja.

        if (playerStats != null)
        {
            builder.Append("\n<b>Build</b>\n");

            if (playerStats.SelectedGates.Count == 0)
                builder.Append("-\n");

            foreach (MathGateData gate in playerStats.SelectedGates)
                builder.Append("- ").Append(gate.DisplayName).Append('\n');
        }

        builder.Append("\nTekan Enter atau tombol di bawah untuk main lagi");

        return builder.ToString();
    }

    private void AppendWave(WaveData wave)
    {
        if (wave == null)
        {
            builder.Append('-');
            return;
        }

        bool first = true;

        foreach (WaveEntry entry in wave.Entries)
        {
            if (entry.count <= 0)
                continue;

            if (!first)
                builder.Append(", ");

            builder.Append(entry.count).Append(" <color=").Append(RoleColors.Hex(entry.role)).Append('>')
                .Append(entry.role).Append("</color>");

            first = false;
        }
    }

    private static string StateLabel(RunState state)
    {
        switch (state)
        {
            case RunState.Preparing: return "<color=#66ccff>Persiapan</color>";
            case RunState.Battle: return "<color=#ffaa33>Bertarung</color>";
            case RunState.RoundCleared: return "<color=#66ff66>Bersih</color>";
            case RunState.Victory: return "<color=#66ff66>Menang</color>";
            case RunState.Defeat: return "<color=#ff6666>Kalah</color>";
            default: return "-";
        }
    }

    private void OnGUI()
    {
        if (!showPanel || runManager == null)
            return;

        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box) { richText = true, alignment = TextAnchor.UpperLeft, wordWrap = true };
            resultStyle = new GUIStyle(GUI.skin.box) { richText = true, alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 16 };
        }

        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        GUI.Box(new Rect(position.x, position.y, 280f, 100f), statusText, boxStyle);

        if (!ShowingResult)
            return;

        float width = 360f;
        float height = 280f;
        float x = (Screen.width / uiScale - width) * 0.5f;
        float y = (Screen.height / uiScale - height) * 0.5f;

        GUI.Box(new Rect(x, y, width, height), resultText, resultStyle);

        if (GUI.Button(new Rect(x + width * 0.5f - 70f, y + height + 8f, 140f, 32f), "Main lagi"))
            retryRequested = true;
    }
}
