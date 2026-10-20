using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Indikator damage dua arah:
//  - Damage MC ke musuh: angka melayang di atas musuh (kuning, oranye untuk pukulan besar, ungu untuk Aura Burst).
//  - Damage musuh ke MC: angka merah di atas MC, kilatan merah di tepi layar, dan penanda arah
//    di sekitar MC yang menunjuk ke penyerang.
// Semua digambar di Canvas GameUI dan diproyeksikan dari posisi dunia, tidak perlu mengubah prefab.
public class DamageFeedback : UIScreen
{
    [Header("Angka damage")]
    [SerializeField] private float popupDuration = 0.9f;
    [SerializeField] private float riseHeight = 1.1f;
    [SerializeField] private float enemyFontSize = 34f;
    [SerializeField] private float playerFontSize = 40f;
    // Pukulan >= fraksi MaxHP musuh ini dianggap besar (angka lebih besar, oranye).
    [SerializeField, Range(0f, 1f)] private float bigHitThreshold = 0.3f;

    [Header("MC terkena damage")]
    [SerializeField] private Color vignetteColor = new Color(0.85f, 0.05f, 0.05f, 1f);
    [SerializeField] private float vignetteMinAlpha = 0.35f;
    [SerializeField] private float vignetteMaxAlpha = 0.75f;
    [SerializeField] private float vignetteFadeSpeed = 1.6f;
    [SerializeField] private float indicatorDuration = 1.1f;
    [SerializeField] private float indicatorRadius = 170f;

    private static readonly Color EnemyHitColor = new Color(1f, 0.95f, 0.7f);
    private static readonly Color BigHitColor = new Color(1f, 0.6f, 0.15f);
    private static readonly Color BurstHitColor = new Color(0.78f, 0.6f, 1f);
    private static readonly Color PlayerHitColor = new Color(1f, 0.3f, 0.3f);

    private class Popup
    {
        public TextMeshProUGUI text;
        public RectTransform rect;
        public Vector3 world;
        public Vector2 jitter;
        public float age;
        public float scale;
    }

    private class Indicator
    {
        public RectTransform rect;
        public CanvasGroup group;
        public Vector3 source;
        public float age;
    }

    private readonly List<Popup> popups = new List<Popup>();
    private readonly Stack<Popup> popupPool = new Stack<Popup>();
    private readonly List<Indicator> indicators = new List<Indicator>();
    private readonly Stack<Indicator> indicatorPool = new Stack<Indicator>();

    private readonly Dictionary<EnemyHealth, Action<float, DamageInfo>> subscriptions =
        new Dictionary<EnemyHealth, Action<float, DamageInfo>>();
    private readonly List<EnemyHealth> staleEnemies = new List<EnemyHealth>();

    private RectTransform popupLayer;
    private RectTransform indicatorLayer;
    private Image vignette;
    private float vignetteAlpha;
    private PlayerHealth player;
    private Camera cam;

    protected override void Build()
    {
        vignette = UIKit.CreateImage("DamageVignette", rect, new Color(0f, 0f, 0f, 0f), false);
        vignette.sprite = UIKit.Vignette;
        UIKit.Stretch(vignette.rectTransform);

        indicatorLayer = UIKit.Stretch(UIKit.CreateRect("Indicators", rect));
        popupLayer = UIKit.Stretch(UIKit.CreateRect("Popups", rect));

        player = ui.Health;

        if (player != null)
            player.Damaged += HandlePlayerDamaged;
    }

    private void OnDestroy()
    {
        if (player != null)
            player.Damaged -= HandlePlayerDamaged;

        foreach (KeyValuePair<EnemyHealth, Action<float, DamageInfo>> pair in subscriptions)
        {
            if (pair.Key != null)
                pair.Key.Damaged -= pair.Value;
        }

        subscriptions.Clear();
    }

    protected override void OnHide()
    {
        vignetteAlpha = 0f;
        vignette.color = new Color(0f, 0f, 0f, 0f);

        for (int i = popups.Count - 1; i >= 0; i--)
            ReleasePopup(i);

        for (int i = indicators.Count - 1; i >= 0; i--)
            ReleaseIndicator(i);
    }

    // ---------- Event damage ----------

    private void HandleEnemyDamaged(EnemyHealth enemy, float damage, DamageInfo info)
    {
        if (!IsVisible || enemy == null || damage <= 0f)
            return;

        bool big = enemy.MaxHealth > 0f && damage >= enemy.MaxHealth * bigHitThreshold;
        Color color = info.kind == DamageKind.Burst ? BurstHitColor : big ? BigHitColor : EnemyHitColor;
        float size = enemyFontSize * (big ? 1.35f : 1f);

        SpawnPopup(enemy.AimPoint + Vector3.up * 0.6f, FormatDamage(damage), color, size);
    }

    private void HandlePlayerDamaged(float damage, DamageInfo info)
    {
        if (!IsVisible || player == null || damage <= 0f)
            return;

        SpawnPopup(player.AimPoint + Vector3.up * 0.9f, "-" + FormatDamage(damage), PlayerHitColor, playerFontSize);

        float share = player.MaxHealth > 0f ? damage / player.MaxHealth : 0.2f;
        float strength = Mathf.Lerp(vignetteMinAlpha, vignetteMaxAlpha, Mathf.Clamp01(share * 4f));
        vignetteAlpha = Mathf.Max(vignetteAlpha, strength);

        Vector3 source = info.source != null ? info.source.transform.position : info.point;

        if (info.source != null || info.point != Vector3.zero)
            SpawnIndicator(source);
    }

    private static string FormatDamage(float damage)
    {
        return damage < 10f && !Mathf.Approximately(damage, Mathf.Round(damage))
            ? damage.ToString("0.#")
            : Mathf.RoundToInt(damage).ToString();
    }

    // ---------- Update ----------

    private void LateUpdate()
    {
        if (!IsVisible)
            return;

        if (cam == null || !cam.isActiveAndEnabled)
            cam = Camera.main;

        SyncEnemySubscriptions();

        float dt = Time.unscaledDeltaTime;

        // Saat jeda, animasi berhenti di tempat.
        if (ui != null && ui.IsPaused)
            dt = 0f;

        vignetteAlpha = Mathf.MoveTowards(vignetteAlpha, 0f, vignetteFadeSpeed * dt);
        vignette.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, vignetteAlpha);

        if (cam == null)
            return;

        UpdatePopups(dt);
        UpdateIndicators(dt);
    }

    // Berlangganan event Damaged setiap musuh yang aktif. Musuh di-spawn dan dinonaktifkan sepanjang run.
    private void SyncEnemySubscriptions()
    {
        IReadOnlyList<EnemyHealth> active = EnemyHealth.Active;

        for (int i = 0; i < active.Count; i++)
        {
            EnemyHealth enemy = active[i];

            if (enemy == null || subscriptions.ContainsKey(enemy))
                continue;

            Action<float, DamageInfo> handler = (damage, info) => HandleEnemyDamaged(enemy, damage, info);
            enemy.Damaged += handler;
            subscriptions.Add(enemy, handler);
        }

        staleEnemies.Clear();

        foreach (KeyValuePair<EnemyHealth, Action<float, DamageInfo>> pair in subscriptions)
        {
            if (pair.Key == null || !pair.Key.isActiveAndEnabled)
                staleEnemies.Add(pair.Key);
        }

        foreach (EnemyHealth enemy in staleEnemies)
        {
            if (enemy != null)
                enemy.Damaged -= subscriptions[enemy];

            subscriptions.Remove(enemy);
        }
    }

    private void UpdatePopups(float dt)
    {
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            Popup popup = popups[i];
            popup.age += dt;

            float t = popup.age / popupDuration;

            if (t >= 1f)
            {
                ReleasePopup(i);
                continue;
            }

            float rise = 1f - (1f - t) * (1f - t);
            Vector3 screen = cam.WorldToScreenPoint(popup.world + Vector3.up * (riseHeight * rise));

            if (screen.z <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(popupLayer, screen, null, out Vector2 local))
            {
                popup.text.alpha = 0f;
                continue;
            }

            popup.rect.anchoredPosition = local + popup.jitter;

            // Muncul membesar lalu mengecil ke ukuran normal, memudar di akhir.
            float pop = popup.age < 0.12f ? Mathf.Lerp(1.6f, 1f, popup.age / 0.12f) : 1f;
            float scale = popup.scale * pop;
            popup.rect.localScale = new Vector3(scale, scale, 1f);
            popup.text.alpha = t < 0.6f ? 1f : Mathf.InverseLerp(1f, 0.6f, t);
        }
    }

    private void UpdateIndicators(float dt)
    {
        if (player == null)
            return;

        Vector3 playerScreen = cam.WorldToScreenPoint(player.AimPoint);

        if (playerScreen.z <= 0f ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(indicatorLayer, playerScreen, null, out Vector2 center))
            return;

        for (int i = indicators.Count - 1; i >= 0; i--)
        {
            Indicator indicator = indicators[i];
            indicator.age += dt;

            float t = indicator.age / indicatorDuration;

            if (t >= 1f)
            {
                ReleaseIndicator(i);
                continue;
            }

            // Arah di layar dari MC ke penyerang. Kamera top-down, jadi arah layar = arah dunia di lantai.
            Vector3 sourceScreen = cam.WorldToScreenPoint(indicator.source);
            Vector2 direction = new Vector2(sourceScreen.x - playerScreen.x, sourceScreen.y - playerScreen.y);

            if (sourceScreen.z <= 0f)
                direction = -direction;

            direction = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector2.up;

            float pulse = 1f + 0.15f * Mathf.Sin(indicator.age * 25f) * (1f - t);
            indicator.rect.anchoredPosition = center + direction * indicatorRadius;
            indicator.rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
            indicator.rect.localScale = new Vector3(pulse, pulse, 1f);

            indicator.group.alpha = t < 0.5f ? 0.95f : Mathf.InverseLerp(1f, 0.5f, t) * 0.95f;
        }
    }

    // ---------- Pool ----------

    private void SpawnPopup(Vector3 world, string label, Color color, float size)
    {
        Popup popup = popupPool.Count > 0 ? popupPool.Pop() : CreatePopup();

        popup.world = world;
        popup.jitter = new Vector2(UnityEngine.Random.Range(-28f, 28f), UnityEngine.Random.Range(-6f, 10f));
        popup.age = 0f;
        popup.scale = size / enemyFontSize;
        popup.text.text = label;
        popup.text.color = color;
        popup.text.alpha = 0f;
        popup.rect.gameObject.SetActive(true);
        popup.rect.SetAsLastSibling();

        popups.Add(popup);
    }

    private Popup CreatePopup()
    {
        TextMeshProUGUI text = UIKit.CreateText("DamagePopup", popupLayer, string.Empty, enemyFontSize, Color.white, TextAlignmentOptions.Center, true);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.outlineWidth = 0.22f;
        text.outlineColor = new Color32(20, 10, 10, 255);
        UIKit.Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 50f));

        return new Popup { text = text, rect = text.rectTransform };
    }

    private void ReleasePopup(int index)
    {
        Popup popup = popups[index];
        popups.RemoveAt(index);
        popup.rect.gameObject.SetActive(false);
        popupPool.Push(popup);
    }

    private void SpawnIndicator(Vector3 source)
    {
        Indicator indicator = indicatorPool.Count > 0 ? indicatorPool.Pop() : CreateIndicator();

        indicator.source = source;
        indicator.age = 0f;
        indicator.rect.gameObject.SetActive(true);

        indicators.Add(indicator);

        // Batasi jumlah penanda supaya layar tidak penuh saat dikeroyok.
        if (indicators.Count > 6)
            ReleaseIndicator(0);
    }

    // Penanda berbentuk chevron (dua bilah membentuk "^") yang ujungnya menunjuk ke penyerang.
    private Indicator CreateIndicator()
    {
        RectTransform root = UIKit.CreateRect("HitIndicator", indicatorLayer);
        UIKit.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 40f));

        Image left = UIKit.CreateImage("Left", root, PlayerHitColor);
        UIKit.Place(left.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(3f, 0f), new Vector2(46f, 12f));
        left.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 30f);

        Image right = UIKit.CreateImage("Right", root, PlayerHitColor);
        UIKit.Place(right.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-3f, 0f), new Vector2(46f, 12f));
        right.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -30f);

        // Satu CanvasGroup supaya kedua bilah memudar bersama.
        CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        return new Indicator { rect = root, group = group };
    }

    private void ReleaseIndicator(int index)
    {
        Indicator indicator = indicators[index];
        indicators.RemoveAt(index);
        indicator.rect.gameObject.SetActive(false);
        indicatorPool.Push(indicator);
    }
}
