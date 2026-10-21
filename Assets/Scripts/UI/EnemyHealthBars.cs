using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Health bar di atas kepala setiap musuh. Digambar di Canvas screen-space milik GameUI
// (posisi diproyeksikan dari dunia setiap frame), jadi tidak perlu mengubah prefab musuh.
// Musuh dibaca dari EnemyHealth.Active. Bar memudar setelah musuh mati dan disembunyikan
// saat label AI Debug (F1) tampil supaya tidak bertumpuk.
public class EnemyHealthBars : UIScreen
{
    [SerializeField] private Vector2 barSize = new Vector2(96f, 14f);
    // Jarak bar di atas titik mata musuh (dunia), di atas lencana huruf role.
    [SerializeField] private float worldOffset = 0.85f;
    // Turunnya bar "ghost" (damage yang baru diterima) per detik, dalam fraksi HP.
    [SerializeField] private float ghostSpeed = 0.6f;

    private class Bar
    {
        public EnemyHealth health;
        public EnemyController controller;
        public RectTransform root;
        public CanvasGroup group;
        public RectTransform fill;
        public Image fillImage;
        public RectTransform ghost;
        public Image roleTab;
        public Image frame;
        public float ghostValue;
        public float lastRatio;
        public float flash;
        public bool seen;
    }

    private readonly Dictionary<EnemyHealth, Bar> bars = new Dictionary<EnemyHealth, Bar>();
    private readonly Stack<Bar> pool = new Stack<Bar>();
    private readonly List<EnemyHealth> removeList = new List<EnemyHealth>();

    private static readonly Color FrameColor = new Color(0.04f, 0.05f, 0.08f, 0.85f);

    private Camera cam;

    protected override void Build()
    {
        // Wadah kosong. Bar dibuat saat musuh muncul.
    }

    protected override void OnHide()
    {
        foreach (Bar bar in bars.Values)
            Release(bar);

        bars.Clear();
    }

    private void LateUpdate()
    {
        if (!IsVisible)
            return;

        if (cam == null || !cam.isActiveAndEnabled)
            cam = Camera.main;

        if (cam == null)
            return;

        foreach (Bar bar in bars.Values)
            bar.seen = false;

        IReadOnlyList<EnemyHealth> active = EnemyHealth.Active;

        for (int i = 0; i < active.Count; i++)
        {
            EnemyHealth health = active[i];

            if (health == null)
                continue;

            if (!bars.TryGetValue(health, out Bar bar))
            {
                bar = Acquire(health);
                bars.Add(health, bar);
            }

            bar.seen = true;
            UpdateBar(bar);
        }

        // Musuh yang sudah dinonaktifkan (keluar dari Active) dilepas.
        removeList.Clear();

        foreach (KeyValuePair<EnemyHealth, Bar> pair in bars)
        {
            if (!pair.Value.seen || pair.Key == null)
                removeList.Add(pair.Key);
        }

        foreach (EnemyHealth key in removeList)
        {
            Release(bars[key]);
            bars.Remove(key);
        }
    }

    private void UpdateBar(Bar bar)
    {
        EnemyHealth health = bar.health;
        bool hiddenForDebug = AIDebugSettings.ShowLabels;

        Vector3 anchor = bar.controller != null && bar.controller.Perception != null
            ? bar.controller.Perception.EyePosition
            : health.transform.position + Vector3.up * 2f;

        Vector3 screen = cam.WorldToScreenPoint(anchor + Vector3.up * worldOffset);
        bool onScreen = screen.z > 0f;

        if (onScreen && RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, null, out Vector2 local))
            bar.root.anchoredPosition = local;

        float ratio = Mathf.Clamp01(health.HealthRatio);
        float dt = Time.unscaledDeltaTime;

        // Kilatan putih + sedikit membesar saat musuh terkena damage.
        if (ratio < bar.lastRatio - 0.0001f)
            bar.flash = 1f;

        bar.lastRatio = ratio;
        bar.flash = Mathf.MoveTowards(bar.flash, 0f, 5f * dt);
        bar.frame.color = Color.Lerp(FrameColor, Color.white, bar.flash * 0.85f);
        float punch = 1f + 0.18f * bar.flash;
        bar.root.localScale = new Vector3(punch, punch, 1f);

        UIKit.SetBarFill(bar.fill, ratio);
        bar.ghostValue = ratio > bar.ghostValue ? ratio : Mathf.MoveTowards(bar.ghostValue, ratio, ghostSpeed * dt);
        UIKit.SetBarFill(bar.ghost, bar.ghostValue);

        bar.fillImage.color = ratio > 0.5f
            ? Color.Lerp(new Color(1f, 0.82f, 0.25f), UIKit.Buff, (ratio - 0.5f) * 2f)
            : Color.Lerp(UIKit.TradeOff, new Color(1f, 0.82f, 0.25f), ratio * 2f);

        float targetAlpha = !onScreen || hiddenForDebug || health.IsDead ? 0f : 1f;
        bar.group.alpha = Mathf.MoveTowards(bar.group.alpha, targetAlpha, (health.IsDead ? 2f : 8f) * dt);
    }

    private Bar Acquire(EnemyHealth health)
    {
        Bar bar = pool.Count > 0 ? pool.Pop() : CreateBar();

        bar.health = health;
        bar.controller = health.GetComponent<EnemyController>();

        if (bar.controller == null)
            bar.controller = health.GetComponentInParent<EnemyController>();

        EnemyRole role = bar.controller != null ? bar.controller.Role : EnemyRole.Charger;
        bar.roleTab.color = RoleColors.Get(role);
        bar.ghostValue = Mathf.Clamp01(health.HealthRatio);
        bar.lastRatio = bar.ghostValue;
        bar.flash = 0f;
        bar.group.alpha = 0f;
        bar.root.gameObject.SetActive(true);
        bar.root.SetAsLastSibling();

        return bar;
    }

    private void Release(Bar bar)
    {
        bar.health = null;
        bar.controller = null;
        bar.root.gameObject.SetActive(false);
        pool.Push(bar);
    }

    private Bar CreateBar()
    {
        Bar bar = new Bar();

        Image frame = UIKit.SetCorners(UIKit.CreateImage("EnemyHealthBar", rect, FrameColor), UIKit.RoundedSmall);
        bar.frame = frame;
        bar.root = frame.rectTransform;
        UIKit.Place(bar.root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, barSize);
        bar.group = frame.gameObject.AddComponent<CanvasGroup>();
        bar.group.blocksRaycasts = false;
        bar.group.interactable = false;

        // Penanda warna role di sisi kiri.
        bar.roleTab = UIKit.SetCorners(UIKit.CreateImage("RoleTab", frame.transform, Color.white), UIKit.RoundedSmall);
        RectTransform tab = bar.roleTab.rectTransform;
        tab.anchorMin = new Vector2(0f, 0f);
        tab.anchorMax = new Vector2(0f, 1f);
        tab.pivot = new Vector2(0f, 0.5f);
        tab.offsetMin = new Vector2(2f, 2f);
        tab.offsetMax = new Vector2(10f, -2f);

        // Track HP.
        RectTransform track = UIKit.CreateRect("Track", frame.transform);
        track.anchorMin = Vector2.zero;
        track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(13f, 3f);
        track.offsetMax = new Vector2(-3f, -3f);

        Image back = UIKit.CreateImage("Back", track, new Color(1f, 1f, 1f, 0.08f), false);
        UIKit.Stretch(back.rectTransform);

        Image ghost = UIKit.CreateImage("Ghost", track, new Color(1f, 0.95f, 0.85f, 0.75f), false);
        bar.ghost = ghost.rectTransform;
        UIKit.SetBarFill(bar.ghost, 1f);

        bar.fillImage = UIKit.CreateImage("Fill", track, UIKit.Buff, false);
        bar.fill = bar.fillImage.rectTransform;
        UIKit.SetBarFill(bar.fill, 1f);

        return bar;
    }
}
