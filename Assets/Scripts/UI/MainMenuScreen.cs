using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Menu utama saat game pertama dibuka. Scene arena tetap terlihat samar di belakang.
// Gaya stiker tinta tebal: judul berupa stiker kata yang miring dan saling tumpuk, ditaburi token operator
// gate (motif identitas), subjudul di pita, tombol stiker, dan kartu alur ronde dengan token bernomor.
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

        // Kolom kiri: logo, pita subjudul, tombol, dan kartu alur dalam satu VerticalLayoutGroup,
        // jadi tidak mungkin saling tumpang tindih di resolusi/aspect ratio apa pun.
        RectTransform column = UIKit.CreateRect("Column", rect);
        column.anchorMin = new Vector2(0f, 0f);
        column.anchorMax = new Vector2(0f, 1f);
        column.pivot = new Vector2(0f, 0.5f);
        column.offsetMin = new Vector2(160f, 96f);
        column.offsetMax = new Vector2(160f + 640f, -56f);
        VerticalLayoutGroup columnLayout = UIKit.AddVertical(column.gameObject, 14f, null, TextAnchor.MiddleLeft);
        // Lebar anak mengikuti preferred width masing-masing.
        columnLayout.childForceExpandWidth = false;

        RectTransform logo = UIKit.CreateRect("Logo", column);
        BuildLogo(logo, ui.GameTitle);

        // Subjudul di pita slate yang sedikit miring.
        Image tape = UIKit.SetCorners(UIKit.CreateImage("SubtitleTape", column, UIKit.Panel), UIKit.InkSmall);
        UIKit.AddHorizontal(tape.gameObject, 0f, new RectOffset(16, 16, 7, 9), TextAnchor.MiddleLeft);
        UIKit.Layout(tape, -1f, 560f, 0f);
        tape.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1f);

        TextMeshProUGUI subtitle = UIKit.CreateText("Subtitle", tape.transform, ui.GameSubtitle, 23f, new Color(0.87f, 0.89f, 0.93f));
        UIKit.Layout(subtitle, -1f, -1f, 1f);

        RectTransform spacer = UIKit.CreateRect("Spacer", column);
        UIKit.Layout(spacer, 4f);

        // Tombol utama paling besar dan sedikit miring, seperti stiker yang ditempel paling akhir.
        Button play = UIKit.CreateButton("PlayButton", column, "MULAI", UIKit.Safe(ui.StartGame), 76f, true);
        UIKit.Layout(play, 76f, 400f, 0f);
        play.transform.localRotation = Quaternion.Euler(0f, 0f, -2f);

        Button tutorialButton = UIKit.CreateButton("TutorialButton", column, "CARA BERMAIN", UIKit.Safe(ui.OpenTutorial), 60f);
        UIKit.Layout(tutorialButton, 60f, 400f, 0f);

        Button quit = UIKit.CreateButton("QuitButton", column, "KELUAR", UIKit.Safe(ui.QuitGame), 60f);
        UIKit.Layout(quit, 60f, 400f, 0f);

        firstSelected = play;

        RectTransform gap = UIKit.CreateRect("Gap", column);
        UIKit.Layout(gap, 8f);

        BuildRulesCard(column);

        // Kredit.
        TextMeshProUGUI credit = UIKit.CreateText("Credit", rect, ui.CreditLine, 20f, UIKit.TextMuted, TextAlignmentOptions.Left);
        UIKit.Place(credit.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(160f, 36f), new Vector2(800f, 30f));
    }

    // Logo dari judul game: tiap kata jadi stiker sendiri (krem dan emas bergantian), bertumpuk menurun ke
    // kanan, lalu ditaburi token operator. Dibangun dari teks GameTitle, bukan gambar.
    private static void BuildLogo(RectTransform logo, string title)
    {
        const float fontSize = 100f;
        const float stepDown = 84f;
        const float overlap = 26f;

        string[] words = (title ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
            words = new[] { "MATH", "GATE" };

        float x = 0f;
        float y = -8f;
        float firstWidth = 0f;
        float lastRight = 0f;
        float lastTop = 0f;

        for (int i = 0; i < words.Length; i++)
        {
            bool even = i % 2 == 0;
            Image word = UIKit.CreateFreeStamp("Word" + i, logo, words[i], fontSize, even ? UIKit.Cream : UIKit.Accent, even ? -4f : 3f, out TextMeshProUGUI label);
            UIKit.SetShadowDistance(word, new Vector2(7f, -8f));

            RectTransform wordRect = word.rectTransform;
            wordRect.anchorMin = new Vector2(0f, 1f);
            wordRect.anchorMax = new Vector2(0f, 1f);
            wordRect.pivot = new Vector2(0f, 1f);
            wordRect.anchoredPosition = new Vector2(x, y);

            // Lebar stiker = lebar teks + padding stempel (12 kiri + 12 kanan).
            float width = label.GetPreferredValues(words[i]).x + 24f;

            if (i == 0)
                firstWidth = width;

            lastRight = x + width;
            lastTop = -y;
            x += width - overlap;
            y -= stepDown;
        }

        float height = -y + 60f;
        UIKit.Layout(logo, height, Mathf.Max(lastRight + 40f, 400f), 0f);

        // Token operator: di sudut kata pertama, di ujung kata terakhir, dan di bawah kata pertama.
        OperatorToken(logo, "×", UIKit.Cream, 56f, new Vector2(firstWidth - 30f, 14f), 12f);
        OperatorToken(logo, "÷", UIKit.Panel, 52f, new Vector2(lastRight + 6f, lastTop + 24f), -8f);
        OperatorToken(logo, "+", UIKit.Panel, 44f, new Vector2(firstWidth * 0.3f, fontSize * 1.45f), 0f);
    }

    private static void OperatorToken(RectTransform logo, string glyph, Color fill, float size, Vector2 position, float tilt)
    {
        Image token = UIKit.CreateToken("Op" + glyph, logo, size, fill, glyph, out TextMeshProUGUI label);

        // Token slate butuh teks terang, token krem memakai teks gelap bawaan.
        if (fill == UIKit.Panel)
            label.color = UIKit.Cream;

        RectTransform tokenRect = token.rectTransform;
        tokenRect.anchorMin = new Vector2(0f, 1f);
        tokenRect.anchorMax = new Vector2(0f, 1f);
        tokenRect.pivot = new Vector2(0.5f, 0.5f);
        tokenRect.anchoredPosition = new Vector2(position.x, -position.y);
        tokenRect.sizeDelta = new Vector2(size, size);
        tokenRect.localRotation = Quaternion.Euler(0f, 0f, tilt);
    }

    // Ringkasan aturan dengan token bernomor, di bawah tombol (sisi kanan dipakai karakter).
    private static void BuildRulesCard(RectTransform column)
    {
        Image card = UIKit.CreatePanel("RulesCard", column, UIKit.Panel);
        UIKit.Layout(card, -1f, 580f, 0f);
        UIKit.AddVertical(card.gameObject, 10f, new RectOffset(24, 24, 18, 22));
        card.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 1.5f);

        UIKit.UseDisplayFont(UIKit.CreateText("Heading", card.transform, "Alur satu ronde", 26f, UIKit.Text, TextAlignmentOptions.Left, true));

        string[] steps =
        {
            "Di <b>Safezone</b>, lihat musuh ronde berikutnya dan tingkat ancamannya.",
            "Dekati gate untuk membaca <color=" + UIKit.BuffHex + ">buff</color> dan " +
            "<color=" + UIKit.TradeOffHex + ">trade-off</color>-nya, lalu <b>tahan [E]</b>.",
            "Kalahkan semua musuh di arena. Build menumpuk tiap ronde."
        };

        for (int i = 0; i < steps.Length; i++)
        {
            RectTransform row = UIKit.CreateRect("Step" + (i + 1), card.transform);
            UIKit.AddHorizontal(row.gameObject, 12f, null, TextAnchor.UpperLeft);

            UIKit.CreateToken("Number", row, 34f, UIKit.Cream, (i + 1).ToString(), out _);

            TextMeshProUGUI text = UIKit.CreateText("Text", row, steps[i], 20f, UIKit.Text);
            UIKit.Layout(text, -1f, -1f, 1f);
        }
    }
}
