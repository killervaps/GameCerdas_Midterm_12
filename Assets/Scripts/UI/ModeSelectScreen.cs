using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Modal pilihan mode, dibuka tombol MULAI di menu utama.
//   Mode Arena    4 arena buatan tangan, dipilih acak ber-seed tiap ronde.
//   Mode Dungeon  dungeon BSP baru tiap ronde dari seed run (Praktikum 10).
// Gaya stiker: dua kartu mode berdampingan, miring ke arah berlawanan. Tiap kartu berisi pita judul, deskripsi,
// chip ciri singkat, dan tombol main. Mode Dungeon diredupkan dan tidak bisa dipilih jika scene belum punya
// DungeonGenerator. Esc menutup (diurus GameUI).
public class ModeSelectScreen : UIScreen
{
    private const float PanelWidth = 940f;
    private const float CardSpacing = 24f;
    private const float CardPadding = 16f;

    private const string DungeonDescription =
        "Dungeon BSP baru tiap ronde, dibangun dari seed run. Room, corridor, Start, dan Goal semuanya procedural.";

    private Button dungeonButton;
    private TextMeshProUGUI dungeonText;
    private GameObject dungeonChips;
    private CanvasGroup dungeonGroup;

    protected override void Build()
    {
        Image dim = UIKit.CreateImage("Dim", rect, UIKit.Backdrop, false);
        dim.raycastTarget = true;
        UIKit.Stretch(dim.rectTransform);

        Image panel = UIKit.CreatePanel("Panel", rect, UIKit.Panel);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelWidth, 0f));
        UIKit.AddVertical(panel.gameObject, 22f, new RectOffset(48, 48, 60, 36));
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

        RectTransform cards = UIKit.CreateRect("Cards", panel.transform);
        HorizontalLayoutGroup cardsLayout = UIKit.AddHorizontal(cards.gameObject, CardSpacing, null, TextAnchor.UpperLeft);
        cardsLayout.childForceExpandHeight = true;

        // Lebar isi satu kartu, dipakai untuk membungkus chip ke baris berikutnya.
        float cardInner = (PanelWidth - 96f - CardSpacing) * 0.5f - UIKit.InkWidth * 2f - CardPadding * 2f;

        Button arenaButton = CreateCard(cards, "Mode Arena",
            "Empat arena buatan tangan. Arena dipilih acak tiap ronde, musuh dan item ditempatkan PCG.",
            new[] { "4 arena", "acak tiap ronde", "musuh dan item PCG" },
            true, -1.2f, "MAIN ARENA", () => ui.StartGame(GameMode.Arena), cardInner,
            out _, out _, out _);

        dungeonButton = CreateCard(cards, "Mode Dungeon", DungeonDescription,
            new[] { "BSP tiap ronde", "vault dan kunci", "portal Goal" },
            false, 1.2f, "MAIN DUNGEON", () => ui.StartGame(GameMode.Dungeon), cardInner,
            out dungeonText, out dungeonChips, out dungeonGroup);

        RectTransform buttons = UIKit.CreateRect("Buttons", panel.transform);
        UIKit.AddHorizontal(buttons.gameObject, 14f, null, TextAnchor.MiddleRight);
        UIKit.Layout(buttons, 60f);

        Button cancel = UIKit.CreateButton("CancelButton", buttons, "BATAL", UIKit.Safe(ui.CloseModeSelect), 56f);
        UIKit.Layout(cancel, 56f, 180f);

        firstSelected = arenaButton;
    }

    // Satu kartu mode: pita judul (krem untuk Arena, slate untuk Dungeon), deskripsi, chip ciri, tombol main.
    private static Button CreateCard(Transform parent, string title, string description, string[] traits, bool creamBand,
        float tilt, string buttonLabel, System.Action onClick, float innerWidth,
        out TextMeshProUGUI text, out GameObject chips, out CanvasGroup group)
    {
        Image card = UIKit.CreatePanel(title, parent, UIKit.Well);
        UIKit.Layout(card, -1f, -1f, 1f);
        int ink = Mathf.RoundToInt(UIKit.InkWidth);
        UIKit.AddVertical(card.gameObject, 0f, new RectOffset(ink, ink, ink, 16));
        card.rectTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);
        group = card.gameObject.AddComponent<CanvasGroup>();

        Color bandColor = creamBand ? UIKit.Cream : UIKit.PanelLight;
        Image band = UIKit.SetCorners(UIKit.CreateImage("Band", card.transform, bandColor), UIKit.InkTopBand);
        UIKit.AddHorizontal(band.gameObject, 0f, new RectOffset(16, 16, 10, 12), TextAnchor.MiddleLeft);
        band.gameObject.AddComponent<LayoutElement>().minHeight = 58f;

        TextMeshProUGUI bandTitle = UIKit.UseDisplayFont(UIKit.CreateText("Title", band.transform, title, 30f,
            creamBand ? UIKit.DarkOf(UIKit.Cream) : UIKit.Text, TextAlignmentOptions.Left, true));
        bandTitle.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform body = UIKit.CreateRect("Body", card.transform);
        UIKit.AddVertical(body.gameObject, 12f, new RectOffset((int)CardPadding, (int)CardPadding, 12, 0));

        text = UIKit.CreateText("Description", body, description, 19f, UIKit.TextMuted);

        RectTransform chipRoot = UIKit.CreateRect("Traits", body);
        chips = chipRoot.gameObject;
        UIKit.AddVertical(chips, 8f);
        BuildTraitChips(chipRoot, traits, innerWidth);

        Button button = UIKit.CreateButton("Button", body, buttonLabel, UIKit.Safe(onClick), 60f, true);
        UIKit.Layout(button, 60f);

        return button;
    }

    // Chip ciri disusun per baris: baris baru dimulai jika chip berikutnya tidak muat di lebar kartu.
    private static void BuildTraitChips(RectTransform root, string[] traits, float maxWidth)
    {
        const float chipSpacing = 8f;
        const float stampPadding = 24f;

        RectTransform row = null;
        float used = 0f;

        foreach (string trait in traits)
        {
            if (row == null)
            {
                row = NewChipRow(root, chipSpacing);
                used = 0f;
            }

            Image chip = UIKit.CreateStamp("Trait", row, 16f, 0f, out TextMeshProUGUI label);
            UIKit.SetStamp(chip, label, trait, UIKit.PanelLight);
            label.color = UIKit.Text;

            float width = label.GetPreferredValues(trait).x + stampPadding;

            if (used > 0f && used + chipSpacing + width > maxWidth)
            {
                row = NewChipRow(root, chipSpacing);
                chip.transform.SetParent(row, false);
                used = 0f;
            }

            used += (used > 0f ? chipSpacing : 0f) + width;
        }
    }

    private static RectTransform NewChipRow(RectTransform root, float spacing)
    {
        RectTransform row = UIKit.CreateRect("Row", root);
        UIKit.AddHorizontal(row.gameObject, spacing, null, TextAnchor.MiddleLeft);
        return row;
    }

    protected override void OnShow()
    {
        bool available = ui.Run != null && ui.Run.DungeonAvailable;

        dungeonButton.interactable = available;
        dungeonGroup.alpha = available ? 1f : 0.55f;
        dungeonChips.SetActive(available);
        dungeonText.text = available
            ? DungeonDescription
            : "Belum tersedia. Tambahkan DungeonGenerator lewat MathGate > PCG > 4.";
    }
}
