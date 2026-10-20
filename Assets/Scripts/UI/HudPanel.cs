using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD selama run:
//  - kiri atas: satu kartu berisi ronde, status, sisa musuh, ancaman live, build, dan (di Safezone)
//    preview musuh ronde berikutnya yang diisi EnemyPreviewPanel lewat PreviewHost;
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

    private TextMeshProUGUI roundText;
    private TextMeshProUGUI stateText;
    private TextMeshProUGUI hpText;
    private RectTransform hpFill;
    private Image hpFillImage;
    private RectTransform hpGhost;
    private GameObject enemyRow;
    private TextMeshProUGUI enemyText;
    private GameObject threatRow;
    private TextMeshProUGUI threatText;
    private Image threatDot;
    private TextMeshProUGUI buildText;
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

        lowHpLabel = UIKit.CreateText("LowHpLabel", rect, "HP KRITIS!", 30f, new Color(1f, 0.35f, 0.35f), TextAlignmentOptions.Center, true);
        lowHpLabel.characterSpacing = 6f;
        lowHpLabel.outlineWidth = 0.2f;
        lowHpLabel.outlineColor = new Color32(30, 0, 0, 220);
        UIKit.Place(lowHpLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 82f), new Vector2(500f, 44f));
        lowHpLabel.alpha = 0f;

        // ---------- Kartu kiri atas ----------
        Image card = UIKit.CreatePanel("StatusCard", rect, UIKit.Panel);
        UIKit.Place(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(430f, 0f));
        UIKit.AddVertical(card.gameObject, 8f, new RectOffset(22, 22, 18, 20));
        UIKit.FitHeight(card.gameObject);

        roundText = UIKit.CreateText("Round", card.transform, "RONDE -", 34f, UIKit.Text, TextAlignmentOptions.Left, true);
        roundText.characterSpacing = 3f;

        stateText = UIKit.CreateText("State", card.transform, string.Empty, 20f, UIKit.Accent);

        enemyRow = UIKit.CreateRect("EnemyRow", card.transform).gameObject;
        UIKit.AddHorizontal(enemyRow, 8f);
        UIKit.Layout(enemyRow.transform, 28f);
        enemyText = UIKit.CreateText("Text", enemyRow.transform, string.Empty, 21f, UIKit.Text);
        UIKit.Layout(enemyText, -1f, -1f, 1f);

        threatRow = UIKit.CreateRect("ThreatRow", card.transform).gameObject;
        UIKit.AddHorizontal(threatRow, 10f);
        UIKit.Layout(threatRow.transform, 28f);
        threatDot = UIKit.CreateImage("Dot", threatRow.transform, Color.white);
        UIKit.Layout(threatDot, 14f, 14f, 0f);
        threatText = UIKit.CreateText("Text", threatRow.transform, string.Empty, 21f, UIKit.Text);
        UIKit.Layout(threatText, -1f, -1f, 1f);

        UIKit.CreateDivider(card.transform);

        buildText = UIKit.CreateText("Build", card.transform, string.Empty, 19f, UIKit.TextMuted);

        // Bagian preview musuh (diisi EnemyPreviewPanel, hanya aktif di Safezone).
        previewHost = UIKit.CreateRect("EnemyPreviewSection", card.transform);
        UIKit.AddVertical(previewHost.gameObject, 10f);
        previewHost.gameObject.SetActive(false);

        // ---------- Tengah bawah: HP bar ----------
        Image hpPanel = UIKit.CreatePanel("HPPanel", rect, UIKit.Panel);
        statusCard = hpPanel.rectTransform;
        statusCardImage = hpPanel;
        UIKit.Place(statusCard, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), HpPanelPosition, new Vector2(640f, 40f));

        // HP bar dengan "ghost" merah yang turun pelan setelah kena damage.
        RectTransform hpBar = UIKit.CreateBar("HPBar", hpPanel.transform, new Color(0f, 0f, 0f, 0.45f), new Color(0.85f, 0.25f, 0.25f, 0.9f), out Image ghost);
        UIKit.Stretch(hpBar, 4f, 4f, 4f, 4f);
        hpFrame = hpBar.GetComponent<Image>();
        hpGhost = ghost.rectTransform;

        hpFillImage = UIKit.CreateImage("Fill", hpBar, UIKit.Buff);
        hpFill = hpFillImage.rectTransform;
        UIKit.SetBarFill(hpFill, 1f);

        hpText = UIKit.CreateText("HPText", hpBar, "HP", 22f, UIKit.Text, TextAlignmentOptions.Center, true);
        hpText.outlineWidth = 0.15f;
        hpText.outlineColor = new Color32(0, 0, 0, 160);
        UIKit.Stretch(hpText.rectTransform);

        // ---------- Kanan atas: waktu ----------
        Image timerCard = UIKit.CreatePanel("TimerCard", rect, UIKit.Panel);
        UIKit.Place(timerCard.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -28f), new Vector2(200f, 92f));

        timerText = UIKit.CreateText("Time", timerCard.transform, "00:00", 36f, UIKit.Text, TextAlignmentOptions.Center, true);
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

        bannerTitle = UIKit.CreateText("Title", banner, string.Empty, 84f, UIKit.Text, TextAlignmentOptions.Center, true);
        bannerTitle.characterSpacing = 10f;
        bannerTitle.outlineWidth = 0.15f;
        bannerTitle.outlineColor = new Color32(0, 0, 0, 200);
        UIKit.Stretch(bannerTitle.rectTransform, 0f, 0f, 0f, 50f);

        bannerSubtitle = UIKit.CreateText("Subtitle", banner, string.Empty, 28f, UIKit.Accent, TextAlignmentOptions.Center, true);
        UIKit.Stretch(bannerSubtitle.rectTransform, 0f, 0f, 112f, 0f);
    }

    // Posisi panel HP (tengah bawah). Dipakai juga sebagai titik diam saat panel bergetar.
    private static readonly Vector2 HpPanelPosition = new Vector2(0f, 30f);

    // Wadah di dalam kartu kiri atas untuk konten EnemyPreviewPanel.
    public RectTransform PreviewHost => previewHost;

    public RectTransform CardRect => (RectTransform)roundText.transform.parent;

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
        skillCountdown = UIKit.CreateText("Countdown", circle, string.Empty, 40f, Color.white, TextAlignmentOptions.Center, true);
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

    public void RefreshBuild()
    {
        PlayerStats stats = ui.Stats;

        if (stats == null || stats.SelectedGates.Count == 0)
        {
            buildText.text = "Build: <color=" + UIKit.MutedHex + ">belum ada gate</color>";
            return;
        }

        builder.Clear();
        builder.Append("Build: ");

        for (int i = 0; i < stats.SelectedGates.Count; i++)
        {
            MathGateData gate = stats.SelectedGates[i];

            if (gate == null)
                continue;

            if (i > 0)
                builder.Append("<color=" + UIKit.MutedHex + "> + </color>");

            builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(UIKit.RarityColor(gate.Rarity))).Append('>')
                .Append(gate.DisplayName).Append("</color>");
        }

        buildText.text = builder.ToString();
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
            roundText.text = "RONDE " + Mathf.Max(1, run.CurrentRound) + " <size=70%><color=" + UIKit.MutedHex + ">/ " + run.TotalRounds + "</color></size>";
            timerText.text = UIKit.FormatTime(run.RunDuration);
        }

        stateText.text = StateLabel(state, run);

        PlayerHealth health = ui.Health;

        if (health != null)
            hpText.text = "HP " + Mathf.CeilToInt(health.CurrentHealth) + " / " + Mathf.CeilToInt(health.MaxHealth);

        bool inBattle = state == RunState.Battle;
        BattleManager battle = ui.Battle;

        enemyRow.SetActive(inBattle && battle != null);

        if (inBattle && battle != null)
            enemyText.text = "Musuh tersisa  <b>" + battle.AliveCount + "</b> <color=" + UIKit.MutedHex + ">/ " + battle.TotalCount + "</color>";

        threatRow.SetActive(inBattle && ui.Threat != null);

        if (inBattle && ui.Threat != null && Time.unscaledTime >= nextThreat)
        {
            nextThreat = Time.unscaledTime + threatInterval;
            RefreshLiveThreat();
        }
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
        threatDot.color = UIKit.ThreatColor(threat.level);
        threatText.text = "Ancaman  <b><color=" + threat.ColorHex + ">" + threat.Label + "</color></b> <size=80%><color=" +
                          UIKit.MutedHex + ">(" + UIKit.FormatRatio(threat.ratio) + ")</color></size>";
    }

    private static string StateLabel(RunState state, RunManager run)
    {
        switch (state)
        {
            case RunState.Preparing: return "Safezone - pilih gate untuk ronde ini";
            case RunState.Battle:
                return run != null && run.CurrentArena != null ? "Bertarung di " + run.CurrentArena.DisplayName : "Bertarung";
            case RunState.RoundCleared: return "Ronde bersih! Kembali ke Safezone...";
            case RunState.Victory: return "Menang!";
            case RunState.Defeat: return "Kalah";
            default: return string.Empty;
        }
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
        hpFrame.color = Color.Lerp(new Color(0f, 0f, 0f, 0.45f), new Color(1f, 0.25f, 0.25f, 0.9f), hitFlash);

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

        // Saat baru siap: membesar sesaat.
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
