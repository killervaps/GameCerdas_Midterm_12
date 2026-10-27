using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD selama run:
//  - kiri atas: kumpulan stiker, yaitu token ronde + label status, kartu info (sisa musuh dan ancaman
//    live saat bertarung, preview musuh berikutnya di Safezone lewat PreviewHost), dan chip build;
//  - tengah bawah: HP bar besar;
//  - kanan bawah: cooldown Aura Burst berbentuk lingkaran dengan ikon ledakan;
//  - kanan atas: waktu run; tengah: banner "RONDE N" saat pertempuran dimulai.
public class HudPanel : UIScreen
{
    [SerializeField] private float refreshInterval = 0.1f;
    [SerializeField] private float threatInterval = 0.25f;
    [SerializeField] private float bannerDuration = 2.2f;

    [Header("HP Kritis")]
    // Di bawah rasio HP ini, tepi layar berdenyut merah seperti detak jantung.
    [SerializeField, Range(0f, 1f)] private float lowHpThreshold = 0.3f;
    [SerializeField] private Color lowHpColor = new Color(0.8f, 0f, 0f, 1f);
    [SerializeField] private float lowHpMinAlpha = 0.3f;
    [SerializeField] private float lowHpMaxAlpha = 0.7f;

    private RectTransform topLeft;
    private RectTransform roundTokens;
    private int shownRound = -1;
    private int shownTotalRounds = -1;
    private Image stateTag;
    private TextMeshProUGUI stateTagLabel;
    private GameObject infoCard;
    private GameObject battleSection;
    private TextMeshProUGUI enemyCountText;
    private RectTransform enemyTokens;
    private readonly List<Image> enemyTokenImages = new List<Image>();
    private readonly List<TextMeshProUGUI> enemyTokenLabels = new List<TextMeshProUGUI>();
    private readonly List<Color> enemyTokenColors = new List<Color>();
    // Musuh yang sedang diwakili token, untuk mendeteksi wave baru.
    private readonly List<EnemyHealth> tokenEnemies = new List<EnemyHealth>();
    private GameObject threatLine;
    private Image threatStamp;
    private TextMeshProUGUI threatStampLabel;
    private TextMeshProUGUI threatRatioText;
    private RectTransform buildChips;
    private TextMeshProUGUI hpText;
    private RectTransform hpFill;
    private Image hpFillImage;
    private RectTransform hpGhost;
    private TextMeshProUGUI timerText;

    private CanvasGroup skillGroup;
    private RectTransform skillRoot;
    private Image skillRing;
    private Image skillIcon;
    private Image skillIconInner;
    private Image skillGlow;
    private TextMeshProUGUI skillCountdown;
    private bool skillWasReady = true;
    private float skillPunch;
    private RectTransform previewHost;

    private CanvasGroup bannerGroup;
    private TextMeshProUGUI bannerTitle;
    private TextMeshProUGUI bannerSubtitle;
    private float bannerTime;

    private AuraBurst auraBurst;
    private float nextRefresh;
    private float nextThreat;
    private float ghostHp = 1f;
    private float lastHpRatio = 1f;
    private float hitFlash;
    private RectTransform statusCard;
    private Image statusCardImage;
    private Image hpFrame;
    private Image lowHpVignette;
    private TextMeshProUGUI lowHpLabel;
    private float lowHpAlpha;
    private float heartbeatPhase;

    private readonly List<EnemyController> liveEnemies = new List<EnemyController>();
    private readonly StringBuilder builder = new StringBuilder();

    protected override void Build()
    {
        auraBurst = FindAnyObjectByType<AuraBurst>();

        // Vignette HP kritis paling belakang supaya kartu HUD tetap terbaca di atasnya.
        lowHpVignette = UIKit.CreateImage("LowHpVignette", rect, new Color(0f, 0f, 0f, 0f), false);
        lowHpVignette.sprite = UIKit.Vignette;
        UIKit.Stretch(lowHpVignette.rectTransform);

        lowHpLabel = UIKit.UseDisplayFont(UIKit.CreateText("LowHpLabel", rect, "HP KRITIS!", 32f, new Color(1f, 0.35f, 0.35f), TextAlignmentOptions.Center, true));
        lowHpLabel.outlineWidth = 0.2f;
        lowHpLabel.outlineColor = new Color32(30, 0, 0, 220);
        UIKit.Place(lowHpLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 82f), new Vector2(500f, 44f));
        lowHpLabel.alpha = 0f;

        BuildTopLeft();

        // ---------- Tengah bawah: HP bar ----------
        Image hpPanel = UIKit.CreatePanel("HPPanel", rect, UIKit.Panel);
        statusCard = hpPanel.rectTransform;
        statusCardImage = hpPanel;
        UIKit.Place(statusCard, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), HpPanelPosition, new Vector2(640f, 40f));

        // HP bar dengan "ghost" merah yang turun pelan setelah kena damage.
        RectTransform hpBar = UIKit.CreateBar("HPBar", hpPanel.transform, UIKit.Well, new Color(0.85f, 0.25f, 0.25f, 0.9f), out Image ghost);
        // Diinset sedikit lebih dari tebal tinta panel supaya garis tinta tetap terlihat utuh.
        UIKit.Stretch(hpBar, 7f, 7f, 7f, 7f);
        hpFrame = hpBar.GetComponent<Image>();
        hpGhost = ghost.rectTransform;

        hpFillImage = UIKit.SetCorners(UIKit.CreateImage("Fill", hpBar, UIKit.Buff), UIKit.RoundedSmall);
        hpFill = hpFillImage.rectTransform;
        UIKit.SetBarFill(hpFill, 1f);

        hpText = UIKit.CreateText("HPText", hpBar, "HP", 22f, UIKit.Text, TextAlignmentOptions.Center, true);
        hpText.outlineWidth = 0.15f;
        hpText.outlineColor = new Color32(0, 0, 0, 160);
        UIKit.Stretch(hpText.rectTransform);

        // ---------- Kanan atas: waktu ----------
        Image timerCard = UIKit.CreatePanel("TimerCard", rect, UIKit.Panel);
        UIKit.Place(timerCard.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -28f), new Vector2(200f, 92f));

        timerText = UIKit.UseDisplayFont(UIKit.CreateText("Time", timerCard.transform, "00:00", 38f, UIKit.Text, TextAlignmentOptions.Center, true));
        UIKit.Stretch(timerText.rectTransform, 0f, 0f, 8f, 34f);

        TextMeshProUGUI pauseHint = UIKit.CreateText("PauseHint", timerCard.transform, "<b>[Esc]</b> Jeda", 17f, UIKit.TextMuted, TextAlignmentOptions.Center);
        UIKit.Stretch(pauseHint.rectTransform, 0f, 0f, 56f, 8f);

        // ---------- Kanan bawah: Aura Burst (cooldown lingkaran) ----------
        BuildSkill();

        if (auraBurst == null)
            skillRoot.gameObject.SetActive(false);

        // ---------- Banner tengah ----------
        RectTransform banner = UIKit.CreateRect("Banner", rect);
        UIKit.Place(banner, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1000f, 160f));
        bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;
        bannerGroup.blocksRaycasts = false;
        bannerGroup.interactable = false;

        // Banner "RONDE N" adalah momen kunci, jadi tetap kapital dengan spacing.
        bannerTitle = UIKit.UseDisplayFont(UIKit.CreateText("Title", banner, string.Empty, 84f, UIKit.Text, TextAlignmentOptions.Center, true));
        bannerTitle.characterSpacing = 10f;
        bannerTitle.outlineWidth = 0.25f;
        bannerTitle.outlineColor = UIKit.Ink;
        UIKit.Stretch(bannerTitle.rectTransform, 0f, 0f, 0f, 50f);

        bannerSubtitle = UIKit.CreateText("Subtitle", banner, string.Empty, 28f, UIKit.Text, TextAlignmentOptions.Center, true);
        bannerSubtitle.outlineWidth = 0.15f;
        bannerSubtitle.outlineColor = new Color32(0, 0, 0, 200);
        UIKit.Stretch(bannerSubtitle.rectTransform, 0f, 0f, 112f, 0f);
    }

    // Posisi panel HP (tengah bawah). Dipakai juga sebagai titik diam saat panel bergetar.
    private static readonly Vector2 HpPanelPosition = new Vector2(0f, 30f);

    // Wadah di dalam kartu kiri atas untuk konten EnemyPreviewPanel.
    public RectTransform PreviewHost => previewHost;

    // Wadah stiker kiri atas. Dipakai HintToast untuk menjaga jarak dan EnemyPreviewPanel untuk rebuild layout.
    public RectTransform CardRect => topLeft;

    // ---------- Kiri atas ----------
    // Dipecah jadi beberapa stiker (ronde, status, kartu info, chip build) alih-alih satu kartu statistik,
    // supaya HUD terbaca seperti tempelan di layar, bukan tabel.
    private void BuildTopLeft()
    {
        topLeft = UIKit.CreateRect("TopLeft", rect);
        UIKit.Place(topLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(430f, 0f));
        VerticalLayoutGroup column = UIKit.AddVertical(topLeft.gameObject, 16f, null, TextAnchor.UpperLeft);
        column.childForceExpandWidth = false;
        UIKit.FitHeight(topLeft.gameObject);

        // Baris 1: stiker "Ronde" + token per ronde, lalu label status miring.
        RectTransform roundRow = UIKit.CreateRect("RoundRow", topLeft);
        UIKit.AddHorizontal(roundRow.gameObject, 14f, null, TextAnchor.MiddleLeft);

        Image roundSticker = UIKit.SetCorners(UIKit.CreateImage("RoundSticker", roundRow, UIKit.Panel), UIKit.InkMedium);
        UIKit.AddHardShadow(roundSticker, UIKit.ButtonShadow);
        UIKit.AddHorizontal(roundSticker.gameObject, 8f, new RectOffset(14, 14, 7, 9), TextAnchor.MiddleLeft);

        TextMeshProUGUI roundLabel = UIKit.UseDisplayFont(UIKit.CreateText("Label", roundSticker.transform, "Ronde", 26f, UIKit.Text, TextAlignmentOptions.Left, true));
        roundLabel.textWrappingMode = TextWrappingModes.NoWrap;

        roundTokens = UIKit.CreateRect("Tokens", roundSticker.transform);
        UIKit.AddHorizontal(roundTokens.gameObject, 6f, null, TextAnchor.MiddleLeft);

        stateTag = UIKit.CreateStamp("StateTag", roundRow, 22f, 4f, out stateTagLabel);

        // Baris 2: kartu info. Isinya bagian pertempuran atau preview musuh (Safezone).
        Image card = UIKit.CreatePanel("InfoCard", topLeft, UIKit.Panel);
        infoCard = card.gameObject;
        UIKit.Layout(card, -1f, 330f, 0f);
        UIKit.AddVertical(card.gameObject, 10f, new RectOffset(18, 18, 14, 18));

        RectTransform battleRoot = UIKit.CreateRect("BattleSection", card.transform);
        battleSection = battleRoot.gameObject;
        UIKit.AddVertical(battleSection, 10f);

        RectTransform countRow = UIKit.CreateRect("CountRow", battleRoot);
        UIKit.AddHorizontal(countRow.gameObject, 8f, null, TextAnchor.MiddleLeft);
        TextMeshProUGUI caption = UIKit.CreateText("Caption", countRow, "Musuh tersisa", 20f, UIKit.TextMuted);
        UIKit.Layout(caption, -1f, -1f, 1f);
        enemyCountText = UIKit.UseDisplayFont(UIKit.CreateText("Count", countRow, "-", 30f, UIKit.Text, TextAlignmentOptions.Right, true));
        enemyCountText.textWrappingMode = TextWrappingModes.NoWrap;

        // Satu token per musuh yang di-spawn. Token musuh yang sudah mati diredupkan.
        enemyTokens = UIKit.CreateRect("EnemyTokens", battleRoot);
        GridLayoutGroup grid = enemyTokens.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(30f, 30f);
        grid.spacing = new Vector2(6f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 8;
        grid.childAlignment = TextAnchor.UpperLeft;

        RectTransform threatRoot = UIKit.CreateRect("ThreatLine", battleRoot);
        threatLine = threatRoot.gameObject;
        UIKit.AddHorizontal(threatLine, 12f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft);
        threatStamp = UIKit.CreateStamp("ThreatStamp", threatRoot, 22f, -4f, out threatStampLabel);
        threatRatioText = UIKit.CreateText("Ratio", threatRoot, string.Empty, 20f, UIKit.TextMuted);
        UIKit.Layout(threatRatioText, -1f, -1f, 1f);

        // Bagian preview musuh (diisi EnemyPreviewPanel, hanya aktif di Safezone).
        previewHost = UIKit.CreateRect("EnemyPreviewSection", card.transform);
        UIKit.AddVertical(previewHost.gameObject, 10f);
        previewHost.gameObject.SetActive(false);

        // Baris 3: label "Build" + chip per gate yang sudah dipilih.
        RectTransform buildRow = UIKit.CreateRect("BuildRow", topLeft);
        UIKit.AddHorizontal(buildRow.gameObject, 10f, null, TextAnchor.UpperLeft);

        Image buildLabel = UIKit.CreateStamp("BuildLabel", buildRow, 19f, 0f, out TextMeshProUGUI buildLabelText);
        buildLabel.color = UIKit.Panel;
        buildLabelText.text = "Build";
        buildLabelText.color = UIKit.Text;

        // Kolom chip mengisi sisa lebar baris. Chip yang lebih lebar dari kolom dipersempit oleh layout dan
        // teksnya turun ke baris berikutnya, jadi nama gate panjang dan efek banyak tetap muat.
        buildChips = UIKit.CreateRect("Chips", buildRow);
        UIKit.Layout(buildChips, -1f, -1f, 1f);
        VerticalLayoutGroup chipColumn = UIKit.AddVertical(buildChips.gameObject, 10f);
        chipColumn.childForceExpandWidth = false;
    }

    private void BuildSkill()
    {
        const float circleSize = 124f;

        skillRoot = UIKit.CreateRect("AuraBurst", rect);
        UIKit.Place(skillRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 26f), new Vector2(170f, 196f));
        skillGroup = skillRoot.gameObject.AddComponent<CanvasGroup>();
        skillGroup.blocksRaycasts = false;
        skillGroup.interactable = false;

        RectTransform circle = UIKit.CreateRect("Circle", skillRoot);
        UIKit.Place(circle, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(circleSize, circleSize));

        // Cahaya lembut saat siap.
        skillGlow = UIKit.CreateImage("Glow", circle, new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0f), false);
        skillGlow.sprite = UIKit.Glow;
        UIKit.Place(skillGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(circleSize * 1.5f, circleSize * 1.5f));

        // Lingkaran polos semi-transparan tanpa garis tinta atau bayangan, sengaja beda dari panel lain:
        // pemilik project memilih tampilan awal ini supaya lingkaran skill tidak tampak seperti lubang hitam.
        Image background = UIKit.CreateImage("Background", circle, new Color(0.06f, 0.08f, 0.12f, 0.88f), false);
        background.sprite = UIKit.Circle;
        UIKit.Stretch(background.rectTransform);

        Image track = UIKit.CreateImage("Track", circle, new Color(1f, 1f, 1f, 0.12f), false);
        track.sprite = UIKit.Ring;
        UIKit.Stretch(track.rectTransform);

        // Isi radial searah jarum jam dari atas: 0 = baru dipakai, 1 = siap.
        skillRing = UIKit.CreateImage("Ring", circle, UIKit.Accent, false);
        skillRing.sprite = UIKit.Ring;
        skillRing.type = Image.Type.Filled;
        skillRing.fillMethod = Image.FillMethod.Radial360;
        skillRing.fillOrigin = (int)Image.Origin360.Top;
        skillRing.fillClockwise = true;
        skillRing.fillAmount = 1f;
        UIKit.Stretch(skillRing.rectTransform);

        // Ikon ledakan dua lapis.
        skillIcon = UIKit.CreateImage("BurstIcon", circle, new Color(1f, 0.55f, 0.15f), false);
        skillIcon.sprite = UIKit.Burst;
        UIKit.Place(skillIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(78f, 78f));

        skillIconInner = UIKit.CreateImage("BurstCore", skillIcon.transform, new Color(1f, 0.9f, 0.45f), false);
        skillIconInner.sprite = UIKit.Burst;
        UIKit.Place(skillIconInner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f));
        skillIconInner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 22.5f);

        // Hitung mundur di lapisan paling atas, di atas ikon.
        skillCountdown = UIKit.UseDisplayFont(UIKit.CreateText("Countdown", circle, string.Empty, 40f, Color.white, TextAlignmentOptions.Center, true));
        skillCountdown.textWrappingMode = TextWrappingModes.NoWrap;
        skillCountdown.outlineWidth = 0.25f;
        skillCountdown.outlineColor = new Color32(0, 0, 0, 230);
        UIKit.Stretch(skillCountdown.rectTransform);

        // Label di bawah ikon.
        TextMeshProUGUI skillName = UIKit.CreateText("Name", skillRoot, "Aura Burst", 21f, UIKit.Text, TextAlignmentOptions.Center, true);
        UIKit.Place(skillName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -circleSize - 6f), new Vector2(170f, 28f));

        TextMeshProUGUI key = UIKit.CreateText("Key", skillRoot, "[Space]", 18f, UIKit.TextMuted, TextAlignmentOptions.Center, true);
        UIKit.Place(key.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -circleSize - 34f), new Vector2(170f, 26f));
    }

    protected override void OnShow()
    {
        RefreshBuild();
        Refresh();
    }

    // Banner besar di tengah layar, mis. "RONDE 2" + komposisi wave.
    public void Flash(string title, string subtitle)
    {
        bannerTitle.text = title;
        bannerSubtitle.text = subtitle;
        bannerTime = bannerDuration;
    }

    // Satu chip per gate, warna rarity, berisi nama dan operasinya (mis. "Pembagi   +40 HP   ÷2 DMG").
    public void RefreshBuild()
    {
        UIKit.ClearChildren(buildChips);

        PlayerStats stats = ui.Stats;

        if (stats == null || stats.SelectedGates.Count == 0)
        {
            Image empty = UIKit.CreateStamp("EmptyChip", buildChips, 19f, 0f, out TextMeshProUGUI emptyLabel);
            empty.color = UIKit.Well;
            emptyLabel.text = "belum ada gate";
            emptyLabel.color = UIKit.TextMuted;
            return;
        }

        for (int i = 0; i < stats.SelectedGates.Count; i++)
        {
            MathGateData gate = stats.SelectedGates[i];

            if (gate == null)
                continue;

            // Baris 1 nama gate, baris 2 operasinya. Simbol, nilai, dan stat satu operasi digabung dengan
            // spasi tak terputus (U+00A0) supaya "÷1.25 DMG" tidak terbelah saat teks dibungkus.
            builder.Clear();
            builder.Append(ShortGateName(gate.DisplayName)).Append("\n<size=82%>");

            for (int e = 0; e < gate.Effects.Count; e++)
            {
                ModifierEffect effect = gate.Effects[e];

                if (e > 0)
                    builder.Append("   ");

                builder.Append(effect.Symbol).Append(effect.value.ToString("0.##")).Append(' ').Append(UIKit.StatShort(effect.stat));
            }

            builder.Append("</size>");

            // Kemiringan bergantian supaya tumpukan chip terasa ditempel tangan.
            Image chip = UIKit.CreateStamp("GateChip", buildChips, 19f, i % 2 == 0 ? -2f : 2f, out TextMeshProUGUI chipLabel);
            chipLabel.textWrappingMode = TextWrappingModes.Normal;
            chipLabel.alignment = TextAlignmentOptions.Left;
            chipLabel.lineSpacing = -10f;
            UIKit.SetStamp(chip, chipLabel, builder.ToString(), UIKit.RarityColor(gate.Rarity));

            if (GateRarityStyle.IsAnimated(gate.Rarity))
                chip.gameObject.AddComponent<SigmaRainbow>().Bind(chip, chipLabel);
        }

        UIKit.ForceRebuild(topLeft);
    }

    // "Gerbang Pembagi" -> "Pembagi". Kata "Gerbang" sudah jelas dari konteks chip build.
    private static string ShortGateName(string displayName)
    {
        const string prefix = "Gerbang ";
        return displayName != null && displayName.StartsWith(prefix) ? displayName.Substring(prefix.Length) : displayName;
    }

    protected override void Update()
    {
        base.Update();

        if (!IsVisible)
            return;

        UpdateBanner();
        UpdateHpBar();
        UpdateSkill();

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            Refresh();
        }
    }

    private void Refresh()
    {
        RunManager run = ui.Run;
        RunState state = ui.State;

        if (run != null)
        {
            RefreshRoundTokens(run);
            timerText.text = UIKit.FormatTime(run.RunDuration);
        }

        RefreshStateTag(state, run);

        PlayerHealth health = ui.Health;

        if (health != null)
            hpText.text = "HP " + Mathf.CeilToInt(health.CurrentHealth) + " / " + Mathf.CeilToInt(health.MaxHealth);

        bool inBattle = state == RunState.Battle;
        BattleManager battle = ui.Battle;

        battleSection.SetActive(inBattle && battle != null);

        if (inBattle && battle != null)
        {
            enemyCountText.text = battle.AliveCount + "<size=65%><color=" + UIKit.MutedHex + "> / " + battle.TotalCount + "</color></size>";
            RefreshEnemyTokens();
        }

        threatLine.SetActive(inBattle && ui.Threat != null);

        if (inBattle && ui.Threat != null && Time.unscaledTime >= nextThreat)
        {
            nextThreat = Time.unscaledTime + threatInterval;
            RefreshLiveThreat();
        }

        // Kartu info disembunyikan jika tidak ada isinya (mis. saat ronde bersih).
        bool hasInfo = battleSection.activeSelf || previewHost.gameObject.activeSelf;

        if (infoCard.activeSelf != hasInfo)
            infoCard.SetActive(hasInfo);
    }

    // Token ronde: sudah lewat = slate, sekarang = krem besar dan miring, belum = sumur redup.
    private void RefreshRoundTokens(RunManager run)
    {
        int total = Mathf.Max(1, run.TotalRounds);
        int current = Mathf.Clamp(run.CurrentRound, 1, total);

        if (current == shownRound && total == shownTotalRounds)
            return;

        shownRound = current;
        shownTotalRounds = total;
        UIKit.ClearChildren(roundTokens);

        for (int i = 1; i <= total; i++)
        {
            bool now = i == current;
            bool past = i < current;
            Color fill = now ? UIKit.Cream : past ? UIKit.PanelLight : UIKit.Well;

            Image token = UIKit.CreateToken("Round" + i, roundTokens, now ? 40f : 30f, fill, i.ToString(), out TextMeshProUGUI label);

            if (now)
                token.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -6f);
            else
                label.color = past ? UIKit.Text : UIKit.TextMuted;
        }
    }

    private void RefreshStateTag(RunState state, RunManager run)
    {
        string text;
        Color fill;

        switch (state)
        {
            case RunState.Preparing:
                text = "Safezone";
                fill = UIKit.Cream;
                break;
            case RunState.Battle:
                text = run != null && run.CurrentArena != null ? run.CurrentArena.DisplayName : "Arena";
                fill = UIKit.PanelLight;
                break;
            case RunState.RoundCleared:
                text = "Ronde bersih";
                fill = UIKit.Buff;
                break;
            case RunState.Victory:
                text = "Menang";
                fill = UIKit.Accent;
                break;
            case RunState.Defeat:
                text = "Kalah";
                fill = UIKit.TradeOff;
                break;
            default:
                text = null;
                fill = UIKit.Cream;
                break;
        }

        bool show = !string.IsNullOrEmpty(text);

        if (stateTag.gameObject.activeSelf != show)
            stateTag.gameObject.SetActive(show);

        if (!show || stateTagLabel.text == text)
            return;

        UIKit.SetStamp(stateTag, stateTagLabel, text, fill);

        // Isi slate terlalu gelap untuk teks gelap, jadi labelnya terang.
        if (fill == UIKit.PanelLight)
            stateTagLabel.color = UIKit.Text;
    }

    // Satu token per musuh yang di-spawn ronde ini, warna role + huruf role. Dibangun ulang setiap kali
    // daftar musuh berganti (bukan hanya jumlahnya, karena dua ronde bisa sama-sama berisi 2 musuh dengan
    // role berbeda), lalu hanya warnanya yang diperbarui saat musuh mati.
    private void RefreshEnemyTokens()
    {
        if (ui.Spawner == null)
            return;

        IReadOnlyList<EnemyHealth> spawned = ui.Spawner.Spawned;

        if (!SameEnemies(spawned))
        {
            tokenEnemies.Clear();
            UIKit.ClearChildren(enemyTokens);
            enemyTokenImages.Clear();
            enemyTokenLabels.Clear();
            enemyTokenColors.Clear();

            for (int i = 0; i < spawned.Count; i++)
            {
                tokenEnemies.Add(spawned[i]);
                EnemyRole role = RoleOf(spawned[i]);
                Color roleColor = RoleColors.Get(role);

                Image token = UIKit.CreateToken("Enemy" + i, enemyTokens, 30f, roleColor, EnemyPreviewBoard.RoleBadgeLetter(role), out TextMeshProUGUI label);
                enemyTokenImages.Add(token);
                enemyTokenLabels.Add(label);
                enemyTokenColors.Add(roleColor);
            }
        }

        for (int i = 0; i < enemyTokenImages.Count && i < spawned.Count; i++)
        {
            bool dead = spawned[i] == null || spawned[i].IsDead;
            enemyTokenImages[i].color = dead ? UIKit.Well : enemyTokenColors[i];
            enemyTokenLabels[i].color = dead ? UIKit.TextMuted : UIKit.DarkOf(enemyTokenColors[i]);
        }
    }

    private bool SameEnemies(IReadOnlyList<EnemyHealth> spawned)
    {
        if (spawned.Count != tokenEnemies.Count)
            return false;

        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != tokenEnemies[i])
                return false;
        }

        return true;
    }

    // Role dari catatan spawner (sesuai entri wave). Role Data di controller hanya cadangan.
    private EnemyRole RoleOf(EnemyHealth enemy)
    {
        if (enemy == null)
            return EnemyRole.Charger;

        if (ui.Spawner != null && ui.Spawner.TryGetRole(enemy, out EnemyRole role))
            return role;

        EnemyController controller = enemy.GetComponentInParent<EnemyController>();
        return controller != null ? controller.Role : EnemyRole.Charger;
    }

    private void RefreshLiveThreat()
    {
        if (ui.Spawner == null || ui.Stats == null)
            return;

        liveEnemies.Clear();

        foreach (EnemyHealth enemy in ui.Spawner.Spawned)
        {
            if (enemy == null || enemy.IsDead)
                continue;

            EnemyController controller = enemy.GetComponent<EnemyController>();

            if (controller == null)
                controller = enemy.GetComponentInParent<EnemyController>();

            if (controller != null)
                liveEnemies.Add(controller);
        }

        ThreatResult threat = ui.Threat.EvaluateLive(liveEnemies, ui.Stats.GetSnapshot());
        UIKit.SetStamp(threatStamp, threatStampLabel, threat.Label, UIKit.ThreatColor(threat.level));
        threatRatioText.text = "rasio " + UIKit.FormatRatio(threat.ratio);
    }

    private void UpdateHpBar()
    {
        PlayerHealth health = ui.Health;

        if (health == null || health.MaxHealth <= 0f)
            return;

        float ratio = Mathf.Clamp01(health.CurrentHealth / health.MaxHealth);
        UIKit.SetBarFill(hpFill, ratio);

        // Kartu status bergetar dan HP bar berkedip merah saat MC terkena damage.
        if (ratio < lastHpRatio - 0.0001f)
            hitFlash = 1f;

        lastHpRatio = ratio;
        hitFlash = Mathf.MoveTowards(hitFlash, 0f, 2.5f * Time.unscaledDeltaTime);

        Vector2 shake = hitFlash > 0f
            ? new Vector2(Mathf.Sin(Time.unscaledTime * 90f), Mathf.Cos(Time.unscaledTime * 70f)) * (8f * hitFlash)
            : Vector2.zero;
        statusCard.anchoredPosition = HpPanelPosition + shake;
        statusCardImage.color = Color.Lerp(UIKit.Panel, new Color(0.45f, 0.08f, 0.08f, 0.95f), hitFlash * 0.8f);
        hpFrame.color = Color.Lerp(UIKit.Well, new Color(1f, 0.25f, 0.25f, 0.9f), hitFlash);

        // Ghost mengejar HP sebenarnya dengan pelan saat turun, langsung saat naik.
        ghostHp = ratio > ghostHp ? ratio : Mathf.MoveTowards(ghostHp, ratio, 0.5f * Time.unscaledDeltaTime);
        UIKit.SetBarFill(hpGhost, ghostHp);

        hpFillImage.color = ratio > 0.5f
            ? UIKit.Buff
            : Color.Lerp(UIKit.TradeOff, new Color(1f, 0.8f, 0.2f), ratio * 2f);

        UpdateLowHp(ratio);
    }

    // Denyut merah di tepi layar saat HP di bawah ambang. Makin rendah HP, makin pekat dan makin cepat.
    private void UpdateLowHp(float ratio)
    {
        float dt = Time.unscaledDeltaTime;
        bool critical = ratio > 0f && ratio < lowHpThreshold && ui.State != RunState.Victory;
        float severity = critical ? Mathf.InverseLerp(lowHpThreshold, 0f, ratio) : 0f;

        // Detak jantung: dua denyut cepat lalu jeda. 60-120 BPM tergantung tingkat bahaya.
        float bpm = Mathf.Lerp(60f, 120f, severity);
        heartbeatPhase = Mathf.Repeat(heartbeatPhase + dt * bpm / 60f, 1f);
        float beat = Mathf.Max(Pulse(heartbeatPhase, 0f), 0.6f * Pulse(heartbeatPhase, 0.22f));

        float target = critical
            ? Mathf.Lerp(lowHpMinAlpha, lowHpMaxAlpha, severity) * (0.55f + 0.45f * beat)
            : 0f;

        lowHpAlpha = Mathf.MoveTowards(lowHpAlpha, target, 3f * dt);
        lowHpVignette.color = new Color(lowHpColor.r, lowHpColor.g, lowHpColor.b, lowHpAlpha);

        lowHpLabel.alpha = critical ? 0.55f + 0.45f * beat : Mathf.MoveTowards(lowHpLabel.alpha, 0f, 3f * dt);
        float scale = 1f + 0.08f * beat * (critical ? 1f : 0f);
        lowHpLabel.rectTransform.localScale = new Vector3(scale, scale, 1f);

        if (critical)
            hpText.color = Color.Lerp(UIKit.Text, new Color(1f, 0.45f, 0.45f), beat);
        else
            hpText.color = UIKit.Text;
    }

    // Puncak tajam di sekitar "start", meluruh cepat.
    private static float Pulse(float phase, float start)
    {
        float t = Mathf.Repeat(phase - start, 1f);
        return t < 0.18f ? Mathf.Sin(t / 0.18f * Mathf.PI) : 0f;
    }

    private void UpdateSkill()
    {
        if (auraBurst == null)
            return;

        float dt = Time.unscaledDeltaTime;
        bool inBattle = ui.State == RunState.Battle;
        bool ready = auraBurst.IsReady;
        skillGroup.alpha = Mathf.MoveTowards(skillGroup.alpha, inBattle ? 1f : 0.4f, 4f * dt);

        float readiness = Mathf.Clamp01(auraBurst.Readiness);
        skillRing.fillAmount = ready ? 1f : readiness;
        skillRing.color = ready ? UIKit.Accent : new Color(0.55f, 0.65f, 0.8f, 0.9f);

        // Ikon redup selama cooldown, terang dan berdenyut halus saat siap.
        Color iconColor = new Color(1f, 0.55f, 0.15f);
        Color coreColor = new Color(1f, 0.9f, 0.45f);
        skillIcon.color = ready ? iconColor : new Color(0.35f, 0.37f, 0.42f, 0.9f);
        skillIconInner.color = ready ? coreColor : new Color(0.5f, 0.52f, 0.58f, 0.9f);

        skillCountdown.text = ready ? string.Empty : auraBurst.CooldownRemaining.ToString("0.0");

        // Saat baru siap: membesar sesaat. Selama siap ikon berputar pelan dan glow berdenyut, supaya
        // status siap terasa hidup (dipilih pemilik project, lihat audit antislop #7).
        if (ready && !skillWasReady)
            skillPunch = 1f;

        skillWasReady = ready;
        skillPunch = Mathf.MoveTowards(skillPunch, 0f, 3f * dt);

        float idle = ready && inBattle ? 0.04f * Mathf.Sin(Time.unscaledTime * 4f) : 0f;
        float scale = 1f + 0.25f * skillPunch + idle;
        skillIcon.rectTransform.localScale = new Vector3(scale, scale, 1f);
        skillIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, ready ? Time.unscaledTime * 20f : 0f);

        float glow = ready ? 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f) + 0.4f * skillPunch : 0f;
        skillGlow.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, Mathf.Clamp01(glow) * (inBattle ? 1f : 0.4f));
    }

    private void UpdateBanner()
    {
        if (bannerTime <= 0f)
        {
            bannerGroup.alpha = 0f;
            return;
        }

        bannerTime -= Time.unscaledDeltaTime;

        float elapsed = bannerDuration - bannerTime;
        float fadeIn = Mathf.Clamp01(elapsed / 0.25f);
        float fadeOut = Mathf.Clamp01(bannerTime / 0.6f);
        bannerGroup.alpha = Mathf.Min(fadeIn, fadeOut);

        float scale = Mathf.Lerp(1.15f, 1f, fadeIn);
        bannerGroup.transform.localScale = new Vector3(scale, scale, 1f);
    }
}
