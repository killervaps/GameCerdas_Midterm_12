using System.Text;
using UnityEngine;

// Panel debug rarity gate untuk demo. Menampilkan peluang tiap rarity per slot dan jumlah gate di pool,
// serta tombol "Paksa" yang mengacak ulang pilihan dengan satu gate rarity tertentu di gate tengah.
// Tampil hanya saat memilih gate di Safezone dan AI Debug Mode menyala (F1).
public class GateRarityDebugPanel : MonoBehaviour
{
    [Header("References (dicari otomatis jika kosong)")]
    [SerializeField] private GateSelectionRoom gateRoom;

    [Header("Layout")]
    [SerializeField] private bool showPanel = true;
    // Aktif = panel hanya tampil saat AI Debug Mode menyala (F1).
    [SerializeField] private bool followDebugToggle = true;
    // Jarak dari pojok kanan atas layar.
    [SerializeField] private Vector2 margin = new Vector2(10f, 10f);
    [SerializeField] private float width = 250f;
    [SerializeField] private float uiScale = 1f;

    private readonly StringBuilder builder = new StringBuilder();
    private readonly string[] buttonLabels = new string[GateRarityStyle.Count];

    private GUIStyle labelStyle;
    private string chanceText = string.Empty;
    private float nextRefresh;

    // Aksi tombol ditunda ke Update. Mengubah isi panel di tengah OnGUI membuat IMGUI melempar error.
    private bool hasPendingForce;
    private GateRarity pendingForce;
    private bool pendingReroll;

    private GateGenerator Generator => gateRoom != null ? gateRoom.Generator : null;

    private bool Visible =>
        showPanel &&
        Generator != null &&
        gateRoom.IsSelecting &&
        (!followDebugToggle || AIDebugSettings.Enabled);

    private void Awake()
    {
        if (gateRoom == null)
            gateRoom = FindAnyObjectByType<GateSelectionRoom>();

        for (int i = 0; i < buttonLabels.Length; i++)
            buttonLabels[i] = "Paksa " + GateRarityStyle.Label((GateRarity)i);
    }

    private void Update()
    {
        if (hasPendingForce)
        {
            Generator.ForceRarityOnNextRoll(pendingForce);
            gateRoom.RerollOffer();
        }
        else if (pendingReroll)
        {
            gateRoom.RerollOffer();
        }

        hasPendingForce = false;
        pendingReroll = false;

        if (Visible && Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.5f;
            BuildText();
        }
    }

    private void BuildText()
    {
        builder.Clear();
        builder.Append("<b>RARITY GATE</b>  <color=#aaaaaa>peluang per slot</color>");

        for (int i = 0; i < GateRarityStyle.Count; i++)
        {
            GateRarity rarity = (GateRarity)i;

            builder.Append('\n')
                .Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(StaticColor(rarity))).Append('>')
                .Append(GateRarityStyle.Label(rarity)).Append("</color>  ")
                .Append((Generator.ChanceOf(rarity) * 100f).ToString("0.#")).Append("%")
                .Append("  <color=#aaaaaa>(").Append(Generator.CountInPool(rarity)).Append(" gate)</color>");
        }

        chanceText = builder.ToString();
    }

    // IMGUI tidak bisa menganimasikan pelangi, SIGMA ditampilkan magenta.
    private static Color StaticColor(GateRarity rarity)
    {
        return GateRarityStyle.IsAnimated(rarity) ? new Color(1f, 0.35f, 0.85f) : GateRarityStyle.Color(rarity);
    }

    private void OnGUI()
    {
        if (!Visible)
            return;

        if (labelStyle == null)
            labelStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

        float x = Screen.width / uiScale - width - margin.x;
        GUILayout.BeginArea(new Rect(x, margin.y, width, 400f), GUI.skin.box);

        GUILayout.Label(chanceText, labelStyle);
        GUILayout.Space(4f);
        GUILayout.Label("<b>Paksa gate tengah</b>", labelStyle);

        for (int i = 0; i < GateRarityStyle.Count; i++)
        {
            GateRarity rarity = (GateRarity)i;

            GUI.enabled = Generator.CountInPool(rarity) > 0;

            if (GUILayout.Button(buttonLabels[i]))
            {
                hasPendingForce = true;
                pendingForce = rarity;
            }
        }

        GUI.enabled = true;

        if (GUILayout.Button("Roll ulang biasa"))
            pendingReroll = true;

        GUILayout.EndArea();
    }
}
