using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Tutorial berupa halaman (Cara Bermain). Bisa dibuka dari menu utama dan menu jeda.
// Navigasi: tombol, panah kiri/kanan, atau A/D. Esc menutup (diurus GameUI).
public class TutorialScreen : UIScreen
{
    private struct Page
    {
        public string title;
        public string body;

        public Page(string title, string body)
        {
            this.title = title;
            this.body = body;
        }
    }

    private Page[] pages;
    private int index;

    private TextMeshProUGUI pageTitle;
    private TextMeshProUGUI pageBody;
    private TextMeshProUGUI counter;
    private Button prevButton;
    private Button nextButton;
    private TextMeshProUGUI nextLabel;
    private RectTransform dots;

    protected override void Build()
    {
        Image dim = UIKit.CreateImage("Dim", rect, UIKit.Backdrop, false);
        dim.raycastTarget = true;
        UIKit.Stretch(dim.rectTransform);

        Image panel = UIKit.CreatePanel("Panel", rect, UIKit.Panel);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1120f, 680f));

        // Tab stiker krem yang menempel di tepi atas panel, miring sedikit.
        Image header = UIKit.CreateFreeStamp("Header", panel.transform, "Cara bermain", 30f, UIKit.Cream, -3f, out _);
        UIKit.SetShadowDistance(header, new Vector2(5f, -6f));
        RectTransform headerRect = header.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(0f, 1f);
        headerRect.pivot = new Vector2(0f, 0.5f);
        headerRect.anchoredPosition = new Vector2(44f, 2f);

        counter = UIKit.UseDisplayFont(UIKit.CreateText("Counter", panel.transform, string.Empty, 26f, UIKit.TextMuted, TextAlignmentOptions.Right, true));
        UIKit.Place(counter.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-56f, -40f), new Vector2(200f, 30f));

        pageTitle = UIKit.UseDisplayFont(UIKit.CreateText("PageTitle", panel.transform, string.Empty, 46f, UIKit.Text, TextAlignmentOptions.Left, true));
        UIKit.Place(pageTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(56f, -80f), new Vector2(1008f, 64f));

        pageBody = UIKit.CreateText("PageBody", panel.transform, string.Empty, 27f, UIKit.Text, TextAlignmentOptions.TopLeft);
        pageBody.lineSpacing = 12f;
        pageBody.enableAutoSizing = true;
        pageBody.fontSizeMin = 18f;
        pageBody.fontSizeMax = 27f;
        pageBody.paragraphSpacing = 14f;
        RectTransform bodyRect = pageBody.rectTransform;
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.offsetMin = new Vector2(56f, 130f);
        bodyRect.offsetMax = new Vector2(-56f, -170f);

        // Bawah: titik halaman + tombol navigasi.
        dots = UIKit.CreateRect("Dots", panel.transform);
        UIKit.Place(dots, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(56f, 70f), new Vector2(300f, 18f));
        UIKit.AddHorizontal(dots.gameObject, 10f);

        RectTransform nav = UIKit.CreateRect("Nav", panel.transform);
        UIKit.Place(nav, new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(-56f, 70f), new Vector2(620f, 64f));
        HorizontalLayoutGroup navLayout = UIKit.AddHorizontal(nav.gameObject, 14f, null, TextAnchor.MiddleRight);
        navLayout.childForceExpandHeight = true;

        prevButton = UIKit.CreateButton("PrevButton", nav, "SEBELUMNYA", UIKit.Safe(Previous));
        UIKit.Layout(prevButton, 64f, 200f);

        Button close = UIKit.CreateButton("CloseButton", nav, "TUTUP", UIKit.Safe(ui.CloseTutorial));
        UIKit.Layout(close, 64f, 160f);

        nextButton = UIKit.CreateButton("NextButton", nav, "BERIKUTNYA", UIKit.Safe(Next), 64f, true);
        UIKit.Layout(nextButton, 64f, 220f);
        nextLabel = nextButton.GetComponentInChildren<TextMeshProUGUI>();

        // Panah dan A/D dipakai untuk membalik halaman (lihat Update), jadi tombol nav tidak boleh ikut
        // memindah fokus ke kiri/kanan. Tanpa ini, panah kiri memindah fokus ke TUTUP dan Enter
        // berikutnya menutup tutorial. Fokus tetap di BERIKUTNYA, mouse tetap bisa mengklik ketiganya.
        LockNavigation(prevButton);
        LockNavigation(close);
        LockNavigation(nextButton);

        firstSelected = nextButton;

        TextMeshProUGUI keys = UIKit.CreateText("KeyHint", panel.transform,
            "<b>A / D</b> atau <b>panah kiri / kanan</b> untuk ganti halaman   |   <b>Esc</b> untuk menutup",
            18f, UIKit.TextMuted);
        keys.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Place(keys.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(56f, 22f), new Vector2(1008f, 26f));
    }

    private static void LockNavigation(Selectable selectable)
    {
        // Explicit tanpa tujuan: arah tidak memindah fokus, tetapi klik mouse tetap memilih tombol.
        selectable.navigation = new Navigation { mode = Navigation.Mode.Explicit };
    }

    public void Open(int page)
    {
        pages = BuildPages();
        index = Mathf.Clamp(page, 0, pages.Length - 1);

        RebuildDots();
        Show();
        ShowPage();
    }

    private void Next()
    {
        if (index >= pages.Length - 1)
        {
            ui.CloseTutorial();
            return;
        }

        index++;
        ShowPage();
    }

    private void Previous()
    {
        if (index <= 0)
            return;

        index--;
        ShowPage();
    }

    private void ShowPage()
    {
        if (pages == null || pages.Length == 0)
            return;

        Page page = pages[index];
        pageTitle.text = page.title;
        pageBody.text = page.body;
        counter.text = (index + 1) + " / " + pages.Length;

        prevButton.interactable = index > 0;
        nextLabel.text = index >= pages.Length - 1 ? "SELESAI" : "BERIKUTNYA";

        for (int i = 0; i < dots.childCount; i++)
        {
            Image dot = dots.GetChild(i).GetComponent<Image>();

            if (dot != null)
                dot.color = i == index ? UIKit.Cream : UIKit.Well;
        }
    }

    private void RebuildDots()
    {
        UIKit.ClearChildren(dots);

        for (int i = 0; i < pages.Length; i++)
        {
            Image dot = UIKit.SetCorners(UIKit.CreateImage("Dot", dots, Color.white), UIKit.InkSmall);
            UIKit.Layout(dot, 14f, 40f);
        }
    }

    protected override void Update()
    {
        base.Update();

        if (!IsVisible)
            return;

        if (NextPressed())
            Next();
        else if (PreviousPressed())
            Previous();
    }

    // Gamepad ikut didukung karena tombol nav tidak lagi bisa dicapai lewat d-pad kiri/kanan.
    private static bool NextPressed()
    {
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;

        return (keyboard != null && (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)) ||
               (gamepad != null && (gamepad.dpad.right.wasPressedThisFrame || gamepad.rightShoulder.wasPressedThisFrame));
    }

    private static bool PreviousPressed()
    {
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;

        return (keyboard != null && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)) ||
               (gamepad != null && (gamepad.dpad.left.wasPressedThisFrame || gamepad.leftShoulder.wasPressedThisFrame));
    }

    private Page[] BuildPages()
    {
        float safe = ui.Threat != null ? ui.Threat.SafeBelow : 0.8f;
        float danger = ui.Threat != null ? ui.Threat.DangerAbove : 1.25f;
        int rounds = ui.Run != null && ui.Run.TotalRounds > 0 ? ui.Run.TotalRounds : 3;

        string safeHex = ThreatEstimator.LevelColorHex(ThreatLevel.Safe);
        string mediumHex = ThreatEstimator.LevelColorHex(ThreatLevel.Medium);
        string dangerHex = ThreatEstimator.LevelColorHex(ThreatLevel.Danger);

        return new[]
        {
            new Page("Tujuan",
                "Bertahan melewati <b>" + rounds + " ronde</b>. Setiap ronde punya alur yang sama:\n" +
                "<b>1.</b> Di <b>Safezone</b>, pilih satu dari tiga gate.\n" +
                "<b>2.</b> Kamu diteleport ke arena dan musuh muncul.\n" +
                "<b>3.</b> Kalahkan semua musuh, HP pulih sebagian, lalu kembali ke Safezone.\n\n" +
                "Efek gate <b>menumpuk</b> sepanjang run. Jika HP habis, run berakhir."),

            new Page("Kontrol",
                "<b>WASD</b>  Bergerak\n" +
                "<b>Tahan Klik Kiri</b>  Menembak Bola Aura ke target (MC melambat saat menembak)\n" +
                "<b>Tab</b>  Ganti target\n" +
                "<b>Space</b>  Aura Burst: damage area di sekitar MC, ada cooldown\n" +
                "<b>Tahan E</b>  Memilih gate saat berdiri di depannya\n" +
                "<b>Esc</b>  Jeda"),

            new Page("Gate & Trade-off",
                "Setiap gate adalah operasi matematika pada stat: <b>+  -  " + "×" + "  " + "÷" + "</b>.\n" +
                "Tidak ada gate yang gratis: selalu ada <color=" + UIKit.BuffHex + "><b>buff</b></color> " +
                "dan <color=" + UIKit.TradeOffHex + "><b>trade-off</b></color>.\n\n" +
                "Berdiri di depan gate untuk membuka <b>kartu detail</b>: deskripsi, efek, perubahan stat, " +
                "dan ancaman ronde berikutnya jika gate itu dipilih. Tahan <b>E</b> untuk memilih.\n\n" +
                "Ingat urutan operasi: " + "×" + "2 lalu +10 hasilnya berbeda dengan +10 lalu " + "×" + "2."),

            new Page("Membaca Ancaman",
                "<b>Power</b> = DPS " + "×" + " EHP (HP efektif setelah Defense).\n" +
                "<b>Ancaman</b> = total Power musuh / Power MC.\n\n" +
                "<color=" + safeHex + "><b>Aman</b></color>   di bawah " + safe.ToString("0.##") + "\n" +
                "<color=" + mediumHex + "><b>Sedang</b></color>   " + safe.ToString("0.##") + " - " + danger.ToString("0.##") + "\n" +
                "<color=" + dangerHex + "><b>Bahaya</b></color>   di atas " + danger.ToString("0.##") + "\n\n" +
                "Panel <b>Musuh Berikutnya</b> di kiri menunjukkan ancaman build sekarang. Saat berdiri di depan gate, " +
                "penanda kedua menunjukkan ancaman <b>setelah</b> gate itu."),

            new Page("Kenali Musuh",
                RoleLine(EnemyRole.Charger, "Mendekat lalu menerjang setelah ancang-ancang tiga ketukan. Menghindar ke samping saat ia bersiap.") + "\n\n" +
                RoleLine(EnemyRole.Shooter, "Menembak dari tepat di luar jangkauanmu dan kabur saat HP-nya rendah. Range tinggi membantu.") + "\n\n" +
                RoleLine(EnemyRole.Guardian, "Menjaga area dan melindungi Shooter. Kelompok yang rapat empuk untuk Aura Burst.")),
        };
    }

    private string RoleLine(EnemyRole role, string description)
    {
        string roleName = role.ToString();

        if (ui.Threat != null && ui.Threat.TryGetRoleData(role, out EnemyRoleData data))
            roleName = data.DisplayName;

        return "<color=" + RoleColors.Hex(role) + "><b>" + roleName + "</b></color>\n<size=90%>" + description + "</size>";
    }
}
