using System.Text;
using UnityEngine;

// Panel debug pemilihan arena: arena ronde ini, peluangnya saat dipilih, dan peluang tiap arena untuk ronde berikutnya.
// Mengikuti AI Debug Mode (F1). Script terpisah supaya tidak bentrok dengan UI yang sedang dikerjakan.
public class ArenaDebugPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ArenaSelector selector;
    // Opsional, untuk menampilkan nomor ronde.
    [SerializeField] private RunManager runManager;

    [Header("Layout")]
    [SerializeField] private bool showPanel = true;
    // Aktif = panel hanya tampil saat AI Debug Mode menyala (F1).
    [SerializeField] private bool followDebugToggle = true;
    [SerializeField] private Vector2 position = new Vector2(10f, 120f);
    [SerializeField] private float width = 280f;
    [SerializeField] private float uiScale = 1f;
    [SerializeField] private float refreshInterval = 0.25f;

    private readonly StringBuilder builder = new StringBuilder();

    private string text = string.Empty;
    private int lineCount = 1;
    private float nextRefresh;
    private GUIStyle style;

    private bool Visible => showPanel && selector != null && (!followDebugToggle || AIDebugSettings.Enabled);

    private void Awake()
    {
        if (selector == null)
            selector = FindAnyObjectByType<ArenaSelector>();

        if (runManager == null)
            runManager = FindAnyObjectByType<RunManager>();
    }

    private void Update()
    {
        if (!Visible || Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + refreshInterval;
        BuildText();
    }

    private void BuildText()
    {
        builder.Clear();
        lineCount = 1;

        builder.Append("<b>ARENA</b>");

        if (runManager != null && runManager.CurrentRound > 0)
            builder.Append("  <color=#aaaaaa>ronde ").Append(runManager.CurrentRound).Append("</color>");

        Arena current = selector.Current;

        AppendLine("Sekarang: ");

        if (current != null)
        {
            builder.Append("<color=#ffee55>").Append(current.DisplayName).Append("</color>")
                .Append(" (peluang ").Append(Percent(selector.CurrentPickChance)).Append(")");
        }
        else
        {
            builder.Append("-");
        }

        AppendLine("<color=#aaaaaa>Peluang ronde berikutnya (ulang x")
            .Append(selector.RepeatPenalty.ToString("0.##")).Append("):</color>");

        if (selector.Arenas != null)
        {
            foreach (Arena arena in selector.Arenas)
            {
                if (arena == null)
                    continue;

                AppendLine(arena == current ? "  <color=#ff8866>" : "  ")
                    .Append(arena.DisplayName).Append("  ").Append(Percent(selector.ChanceOf(arena)));

                if (arena == current)
                    builder.Append("</color>");
            }
        }

        text = builder.ToString();
    }

    private StringBuilder AppendLine(string value)
    {
        lineCount++;
        return builder.Append('\n').Append(value);
    }

    private static string Percent(float value)
    {
        return (value * 100f).ToString("0.#") + "%";
    }

    private void OnGUI()
    {
        if (!Visible || string.IsNullOrEmpty(text))
            return;

        if (style == null)
            style = new GUIStyle(GUI.skin.box) { richText = true, alignment = TextAnchor.UpperLeft, wordWrap = true };

        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
        GUI.Box(new Rect(position.x, position.y, width, 10f + lineCount * 18f), text, style);
    }
}
