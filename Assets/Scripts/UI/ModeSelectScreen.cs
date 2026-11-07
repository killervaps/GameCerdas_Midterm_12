using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Modal pilihan mode, dibuka tombol MULAI di menu utama.
//   Mode Arena    4 arena buatan tangan, dipilih acak ber-seed tiap ronde.
//   Mode Dungeon  dungeon BSP baru tiap ronde dari seed run (Praktikum 10).
// Mode Dungeon tidak bisa dipilih jika scene belum punya DungeonGenerator. Esc menutup (diurus GameUI).
public class ModeSelectScreen : UIScreen
{
    private const float PanelWidth = 820f;
    private const float ButtonWidth = 300f;
    private const float RowHeight = 76f;

    private Button dungeonButton;
    private TextMeshProUGUI dungeonText;

    protected override void Build()
    {
        Image dim = UIKit.CreateImage("Dim", rect, UIKit.Backdrop, false);
        dim.raycastTarget = true;
        UIKit.Stretch(dim.rectTransform);

        Image panel = UIKit.CreatePanel("Panel", rect, UIKit.Panel);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelWidth, 0f));
        UIKit.AddVertical(panel.gameObject, 18f, new RectOffset(48, 48, 60, 36));
        UIKit.FitHeight(panel.gameObject);

        // Tab stiker krem di tepi atas panel, sama seperti Pengaturan.
        Image header = UIKit.CreateFreeStamp("Header", panel.transform, "Pilih Mode", 30f, UIKit.Cream, -3f, out _);
        UIKit.SetShadowDistance(header, new Vector2(5f, -6f));
        UIKit.Layout(header).ignoreLayout = true;
        RectTransform headerRect = header.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(0f, 1f);
        headerRect.pivot = new Vector2(0f, 0.5f);
        headerRect.anchoredPosition = new Vector2(44f, 2f);

        Button arenaButton = CreateRow(panel.transform, "MODE ARENA",
            "Empat arena buatan tangan. Arena dipilih acak tiap ronde, musuh dan item ditempatkan PCG.",
            () => ui.StartGame(GameMode.Arena), true, out _);

        dungeonButton = CreateRow(panel.transform, "MODE DUNGEON",
            "Dungeon BSP baru tiap ronde, dibangun dari seed run. Room, corridor, Start, dan Goal semuanya procedural.",
            () => ui.StartGame(GameMode.Dungeon), false, out dungeonText);

        UIKit.CreateDivider(panel.transform);

        RectTransform buttons = UIKit.CreateRect("Buttons", panel.transform);
        UIKit.AddHorizontal(buttons.gameObject, 14f, null, TextAnchor.MiddleRight);
        UIKit.Layout(buttons, 64f);

        Button cancel = UIKit.CreateButton("CancelButton", buttons, "BATAL", UIKit.Safe(ui.CloseModeSelect), 64f);
        UIKit.Layout(cancel, 64f, 200f);

        firstSelected = arenaButton;
    }

    // Satu mode: tombol di kiri, penjelasan singkat di kanan.
    private static Button CreateRow(Transform parent, string title, string description, System.Action onClick, bool primary, out TextMeshProUGUI text)
    {
        RectTransform row = UIKit.CreateRect(title, parent);
        UIKit.AddHorizontal(row.gameObject, 24f, null, TextAnchor.MiddleLeft);
        UIKit.Layout(row, RowHeight);

        Button button = UIKit.CreateButton("Button", row, title, UIKit.Safe(onClick), RowHeight, primary);
        UIKit.Layout(button, RowHeight, ButtonWidth, 0f);

        text = UIKit.CreateText("Description", row, description, 20f, UIKit.TextMuted);
        UIKit.Layout(text, -1f, -1f, 1f);

        return button;
    }

    protected override void OnShow()
    {
        bool available = ui.Run != null && ui.Run.DungeonAvailable;

        dungeonButton.interactable = available;

        if (!available)
            dungeonText.text = "Belum tersedia. Tambahkan DungeonGenerator lewat MathGate > PCG > 4.";
    }
}
