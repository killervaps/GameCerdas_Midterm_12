using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Menu utama saat game pertama dibuka. Scene arena tetap terlihat samar di belakang.
public class MainMenuScreen : UIScreen
{
    protected override void Build()
    {
        // Latar: penahan klik transparan + gradasi gelap dari kiri. Sisi kanan dibiarkan terang
        // karena di situ kamera showcase menampilkan karakter MC.
        Image blocker = UIKit.CreateImage("ClickBlocker", rect, new Color(0f, 0f, 0f, 0f), false);
        blocker.raycastTarget = true;
        UIKit.Stretch(blocker.rectTransform);

        Image fade = UIKit.CreateImage("SideFade", rect, new Color(0.02f, 0.03f, 0.05f, 0.9f), false);
        fade.sprite = UIKit.HorizontalFade;
        UIKit.Stretch(fade.rectTransform);

        Image bottom = UIKit.CreateImage("BottomFade", rect, new Color(0.02f, 0.03f, 0.05f, 0.55f), false);
        bottom.sprite = UIKit.VerticalFade;
        RectTransform bottomRect = bottom.rectTransform;
        bottomRect.anchorMin = new Vector2(0f, 0f);
        bottomRect.anchorMax = new Vector2(1f, 0.3f);
        bottomRect.offsetMin = Vector2.zero;
        bottomRect.offsetMax = Vector2.zero;

        // Kolom kiri: judul, tombol, dan kartu aturan dalam satu VerticalLayoutGroup,
        // jadi tidak mungkin saling tumpang tindih di resolusi/aspect ratio apa pun.
        RectTransform column = UIKit.CreateRect("Column", rect);
        column.anchorMin = new Vector2(0f, 0f);
        column.anchorMax = new Vector2(0f, 1f);
        column.pivot = new Vector2(0f, 0.5f);
        column.offsetMin = new Vector2(160f, 96f);
        column.offsetMax = new Vector2(160f + 620f, -56f);
        VerticalLayoutGroup columnLayout = UIKit.AddVertical(column.gameObject, 16f, null, TextAnchor.MiddleLeft);
        // Lebar anak mengikuti preferred width masing-masing (tombol 380, teks selebar kolom).
        columnLayout.childForceExpandWidth = false;

        Image accent = UIKit.CreateImage("AccentBar", column, UIKit.Accent);
        UIKit.Layout(accent, 8f, 120f, 0f);

        TextMeshProUGUI title = UIKit.CreateText("Title", column, ui.GameTitle, 96f, UIKit.Text, TextAlignmentOptions.Left, true);
        title.characterSpacing = 4f;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(title, 112f, -1f, 1f);

        TextMeshProUGUI subtitle = UIKit.CreateText("Subtitle", column, ui.GameSubtitle, 26f, UIKit.TextMuted);
        UIKit.Layout(subtitle, 72f, -1f, 1f);

        RectTransform spacer = UIKit.CreateRect("Spacer", column);
        UIKit.Layout(spacer, 8f);

        Button play = UIKit.CreateButton("PlayButton", column, "MULAI", UIKit.Safe(ui.StartGame), 72f, true);
        UIKit.Layout(play, 72f, 380f, 0f);

        Button tutorialButton = UIKit.CreateButton("TutorialButton", column, "CARA BERMAIN", UIKit.Safe(ui.OpenTutorial), 60f);
        UIKit.Layout(tutorialButton, 60f, 380f, 0f);

        Button quit = UIKit.CreateButton("QuitButton", column, "KELUAR", UIKit.Safe(ui.QuitGame), 60f);
        UIKit.Layout(quit, 60f, 380f, 0f);

        firstSelected = play;

        RectTransform gap = UIKit.CreateRect("Gap", column);
        UIKit.Layout(gap, 12f);

        // Ringkasan aturan, di bawah tombol (sisi kanan dipakai karakter).
        Image card = UIKit.CreatePanel("RulesCard", column, new Color(0.09f, 0.11f, 0.15f, 0.8f));
        UIKit.Layout(card, -1f, 580f, 0f);
        UIKit.AddVertical(card.gameObject, 8f, new RectOffset(26, 26, 20, 22));

        TextMeshProUGUI cardTitle = UIKit.CreateText("Heading", card.transform, "ALUR SATU RONDE", 20f, UIKit.Accent, TextAlignmentOptions.Left, true);
        cardTitle.characterSpacing = 3f;

        UIKit.CreateText("Steps", card.transform,
            "1. Di <b>Safezone</b>, lihat musuh ronde berikutnya dan tingkat ancamannya.\n" +
            "2. Dekati gate untuk membaca <color=" + UIKit.BuffHex + ">buff</color> dan " +
            "<color=" + UIKit.TradeOffHex + ">trade-off</color>-nya, lalu <b>tahan [E]</b>.\n" +
            "3. Kalahkan semua musuh di arena. Build menumpuk tiap ronde.",
            20f, UIKit.Text);

        // Kredit.
        TextMeshProUGUI credit = UIKit.CreateText("Credit", rect, ui.CreditLine, 20f, UIKit.TextMuted, TextAlignmentOptions.Left);
        UIKit.Place(credit.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(160f, 36f), new Vector2(800f, 30f));
    }
}
