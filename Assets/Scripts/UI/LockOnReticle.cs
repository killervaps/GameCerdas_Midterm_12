using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Penanda target MC di layar, menggantikan piringan marker di lantai.
//   Auto-target  sigil ungu mengelilingi badan musuh: cincin luar titik-titik, cincin dalam putus panjang
//                yang berputar berlawanan arah, dan empat permata. Bernapas pelan.
//   Lock (Tab)   animasi lock-in: siku menjepit dari besar sambil berputar, kilatan, lalu cincin putus-putus
//                berputar, empat segitiga menunjuk ke dalam, titik tengah, dan label LOCK. Berdenyut halus.
//   Ganti target penanda meluncur dari musuh lama ke musuh baru.
//   Lepas lock   crosshair melebar dan memudar, penanda auto-target muncul lagi.
// Ukuran mengikuti besar musuh di layar (bounds collider). Membaca TargetSelector, tidak mengubah logika target.
[DefaultExecutionOrder(200)]
public class LockOnReticle : UIScreen
{
    private const int Corners = 4;
    private const int Triangles = 4;
    private const int Gems = 4;

    [Header("Ukuran (satuan Canvas 1920x1080)")]
    [SerializeField] private float minSize = 70f;
    [SerializeField] private float maxSize = 240f;
    // Crosshair lock sedikit lebih besar dari badan musuh.
    [SerializeField] private float padding = 1.1f;

    [Header("Auto-target")]
    // Diameter sigil dibanding ukuran crosshair lock. Sedikit lebih besar supaya cincin tidak menutupi badan.
    [SerializeField] private float sigilScale = 1.2f;
    [SerializeField] private Color autoColor = new Color(0.498f, 0.467f, 0.867f, 1f);
    [SerializeField] private Color autoAccent = new Color(0.686f, 0.663f, 0.925f, 1f);
    [SerializeField] private Color autoInk = new Color(0.149f, 0.129f, 0.361f, 1f);
    // Derajat per detik. Cincin dalam berputar berlawanan arah.
    [SerializeField] private float sigilSpin = 40f;

    [Header("Lock")]
    [SerializeField] private Color lockColor = new Color(1f, 0.33f, 0.22f, 1f);
    [SerializeField] private string lockLabel = "LOCK";
    [SerializeField] private float ringSpin = 70f;

    [Header("Animasi")]
    [SerializeField] private float lockInDuration = 0.22f;
    [SerializeField] private float unlockDuration = 0.18f;
    // Makin besar makin cepat penanda meluncur ke target baru.
    [SerializeField] private float followSharpness = 16f;

    private struct Layered
    {
        public RectTransform rect;
        public Image ink;
        public Image fill;
    }

    private readonly Layered[] corners = new Layered[Corners];
    private readonly Layered[] triangles = new Layered[Triangles];
    private readonly Layered[] gems = new Layered[Gems];
    private readonly Vector3[] corners3D = new Vector3[8];

    private RectTransform root;
    private CanvasGroup rootGroup;

    private CanvasGroup autoGroup;
    private RectTransform sigil;
    private RectTransform sigilOuter;
    private RectTransform sigilInner;

    private CanvasGroup lockGroup;
    private RectTransform bracketPivot;
    private RectTransform ring;
    private Image ringFill;
    private Image dot;
    private Image flash;
    private TextMeshProUGUI label;

    private TargetSelector selector;
    private Camera cam;
    private EnemyHealth shownTarget;
    private Collider shownCollider;

    private Vector2 position;
    private float size = 100f;
    private float alpha;
    private bool wasLocked;
    private float lockTimer;
    private float lockAmount;
    private float ringAngle;
    private float sigilAngle;

    protected override void Build()
    {
        root = CreateCentered("Reticle", rect);
        rootGroup = root.gameObject.AddComponent<CanvasGroup>();
        rootGroup.alpha = 0f;

        // Di luar lapisan lock supaya kilatan tetap terang walau lapisan lock baru mulai muncul.
        flash = CreateImage("Flash", root, UIKit.Glow);
        flash.color = Color.clear;

        BuildAutoTarget();
        BuildLock();
    }

    private void BuildAutoTarget()
    {
        RectTransform autoLayer = CreateCentered("Auto", root);
        autoGroup = autoLayer.gameObject.AddComponent<CanvasGroup>();

        sigil = CreateCentered("Sigil", autoLayer);

        sigilOuter = CreateCentered("Outer", sigil);
        Image outer = CreateImage("Ring", sigilOuter, ReticleSprites.DottedRing);
        UIKit.Stretch(outer.rectTransform);
        outer.color = autoColor;

        sigilInner = CreateCentered("Inner", sigil);
        Image inner = CreateImage("Ring", sigilInner, ReticleSprites.LongDashRing);
        UIKit.Stretch(inner.rectTransform);
        inner.color = new Color(autoAccent.r, autoAccent.g, autoAccent.b, 0.75f);

        for (int i = 0; i < Gems; i++)
        {
            gems[i] = CreateLayered("Gem", sigilInner, ReticleSprites.DiamondInk, ReticleSprites.Diamond);
            gems[i].ink.color = autoInk;
            gems[i].fill.color = autoAccent;
        }
    }

    private void BuildLock()
    {
        RectTransform lockLayer = CreateCentered("Lock", root);
        lockGroup = lockLayer.gameObject.AddComponent<CanvasGroup>();
        lockGroup.alpha = 0f;

        ring = CreateCentered("Ring", lockLayer);
        Image ringInk = CreateImage("Ink", ring, ReticleSprites.DashedRingInk);
        UIKit.Stretch(ringInk.rectTransform);
        ringInk.color = UIKit.Ink;
        ringFill = CreateImage("Fill", ring, ReticleSprites.DashedRing);
        UIKit.Stretch(ringFill.rectTransform);

        for (int i = 0; i < Triangles; i++)
            triangles[i] = CreateLayered("Triangle", lockLayer, ReticleSprites.TriangleInk, ReticleSprites.Triangle);

        Image dotInk = CreateImage("DotInk", lockLayer, UIKit.Circle);
        dotInk.rectTransform.sizeDelta = new Vector2(14f, 14f);
        dotInk.color = UIKit.Ink;
        dot = CreateImage("Dot", lockLayer, UIKit.Circle);
        dot.rectTransform.sizeDelta = new Vector2(8f, 8f);

        label = UIKit.UseDisplayFont(UIKit.CreateText("Label", lockLayer, lockLabel, 20f, lockColor, TextAlignmentOptions.Center, true));
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        label.rectTransform.sizeDelta = new Vector2(120f, 28f);
        label.outlineWidth = 0.25f;
        label.outlineColor = UIKit.Ink;

        bracketPivot = CreateCentered("Brackets", lockLayer);

        for (int i = 0; i < Corners; i++)
        {
            corners[i] = CreateLayered("Corner", bracketPivot, ReticleSprites.CornerInk, ReticleSprites.Corner);
            // Kiri atas, kanan atas, kanan bawah, kiri bawah. Sprite dasar adalah siku kiri atas.
            corners[i].rect.localRotation = Quaternion.Euler(0f, 0f, -90f * i);
        }
    }

    protected override void OnHide()
    {
        shownTarget = null;
        shownCollider = null;
        alpha = 0f;
        lockAmount = 0f;
        wasLocked = false;

        if (rootGroup != null)
            rootGroup.alpha = 0f;
    }

    private void LateUpdate()
    {
        if (!IsVisible)
            return;

        if (cam == null || !cam.isActiveAndEnabled)
            cam = Camera.main;

        if (selector == null)
            selector = ui.Health != null ? ui.Health.GetComponent<TargetSelector>() : FindAnyObjectByType<TargetSelector>();

        if (cam == null || selector == null)
            return;

        float dt = Time.unscaledDeltaTime;
        float time = Time.unscaledTime;

        EnemyHealth target = selector.CurrentTarget;
        bool hasTarget = target != null && target.isActiveAndEnabled && !target.IsDead;
        bool locked = hasTarget && selector.IsLocked && selector.LockedTarget == target;

        if (hasTarget)
            TrackTarget(target, dt);

        // Lock baru, atau pindah lock ke musuh lain: animasi lock-in diputar ulang.
        if (locked && (!wasLocked || target != shownTarget))
            lockTimer = 0f;

        if (hasTarget)
            shownTarget = target;

        wasLocked = locked;

        alpha = Mathf.MoveTowards(alpha, hasTarget ? 1f : 0f, dt * 10f);
        rootGroup.alpha = alpha;

        if (alpha <= 0f)
        {
            shownTarget = null;
            return;
        }

        root.anchoredPosition = position;

        UpdateLockAmount(locked, dt, out float lockIn);
        AnimateAuto(dt, time);
        AnimateLock(locked, lockIn, dt, time);
    }

    private void TrackTarget(EnemyHealth target, float dt)
    {
        if (target != shownTarget)
            shownCollider = FindBodyCollider(target);

        if (!Measure(target, out Vector2 center, out float measuredSize))
            return;

        // Muncul pertama kali langsung di tempat. Setelah itu meluncur halus, termasuk saat ganti target.
        if (shownTarget == null || alpha <= 0f)
        {
            position = center;
            size = measuredSize;
            return;
        }

        float follow = 1f - Mathf.Exp(-followSharpness * dt);
        position = Vector2.Lerp(position, center, follow);
        size = Mathf.Lerp(size, measuredSize, follow);
    }

    private void UpdateLockAmount(bool locked, float dt, out float lockIn)
    {
        lockIn = 1f;

        if (locked)
        {
            lockTimer += dt;
            lockIn = lockInDuration > 0f ? Mathf.Clamp01(lockTimer / lockInDuration) : 1f;
            lockAmount = Mathf.Max(lockAmount, EaseOut(lockIn));
        }
        else
        {
            lockAmount = unlockDuration > 0f ? Mathf.MoveTowards(lockAmount, 0f, dt / unlockDuration) : 0f;
        }
    }

    // Auto-target memudar saat lock masuk, sigil sedikit menyusut ke badan musuh.
    private void AnimateAuto(float dt, float time)
    {
        float autoAmount = 1f - lockAmount;
        autoGroup.alpha = autoAmount;

        if (autoAmount <= 0f)
            return;

        float shrink = Mathf.Lerp(0.6f, 1f, autoAmount);
        float breathe = 1f + 0.03f * Mathf.Sin(time * 2.5f);
        float width = size * sigilScale * shrink * breathe;

        sigil.sizeDelta = new Vector2(width, width);

        sigilAngle += sigilSpin * dt;
        sigilOuter.sizeDelta = new Vector2(width, width);
        sigilOuter.localRotation = Quaternion.Euler(0f, 0f, sigilAngle);
        sigilInner.sizeDelta = new Vector2(width, width);
        sigilInner.localRotation = Quaternion.Euler(0f, 0f, -sigilAngle * 1.5f);

        float gemSize = Mathf.Clamp(width * 0.12f, 10f, 18f);
        float gemRadius = width * 0.5f;

        for (int i = 0; i < Gems; i++)
        {
            float radians = i * Mathf.PI * 0.5f;
            gems[i].rect.anchoredPosition = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * gemRadius;
            gems[i].rect.sizeDelta = new Vector2(gemSize, gemSize);
        }
    }

    private void AnimateLock(bool locked, float lockIn, float dt, float time)
    {
        lockGroup.alpha = lockAmount;

        // Kilatan singkat di awal lock-in.
        float flashAmount = locked ? 1f - lockIn : 0f;
        float flashSize = size * (1.4f + lockIn * 0.6f);
        flash.rectTransform.sizeDelta = new Vector2(flashSize, flashSize);
        flash.color = new Color(lockColor.r, lockColor.g, lockColor.b, flashAmount * 0.7f);

        if (lockAmount <= 0f)
            return;

        float eased = EaseOut(lockIn);

        // Siku menjepit dari 1,9x sambil berputar 45°. Saat lepas lock siku melebar sambil memudar.
        float spread = locked ? Mathf.Lerp(1.9f, 1f, eased) : 1f + (1f - lockAmount) * 0.4f;
        float bracketRotation = locked ? Mathf.Lerp(45f, 0f, eased) : 0f;

        float half = size * 0.5f * spread;
        float armLength = Mathf.Clamp(size * 0.3f, 16f, 44f);
        float offset = half - armLength * 0.5f;

        bracketPivot.localRotation = Quaternion.Euler(0f, 0f, bracketRotation);

        for (int i = 0; i < Corners; i++)
        {
            // Urutan pojok sama dengan rotasi di Build: kiri atas, kanan atas, kanan bawah, kiri bawah.
            float x = i == 0 || i == 3 ? -offset : offset;
            float y = i < 2 ? offset : -offset;

            corners[i].rect.anchoredPosition = new Vector2(x, y);
            corners[i].rect.sizeDelta = new Vector2(armLength, armLength);
            corners[i].fill.color = lockColor;
        }

        float ringScale = locked ? Mathf.Lerp(1.7f, 1f, eased) : 1f + (1f - lockAmount) * 0.5f;
        float pulse = locked && lockIn >= 1f ? 1f + 0.03f * Mathf.Sin(time * 6f) : 1f;
        float ringSize = size * 0.92f * ringScale * pulse;

        ringAngle -= ringSpin * dt;
        ring.localRotation = Quaternion.Euler(0f, 0f, ringAngle);
        ring.sizeDelta = new Vector2(ringSize, ringSize);
        ringFill.color = lockColor;

        float triangleSize = Mathf.Clamp(size * 0.16f, 12f, 26f);
        float bob = locked ? Mathf.Sin(time * 8f) * 2.5f : 0f;
        float triangleDistance = ringSize * 0.5f + triangleSize * 0.75f + bob;

        for (int i = 0; i < Triangles; i++)
        {
            // Atas, kanan, bawah, kiri. Sprite menunjuk ke bawah, diputar supaya menunjuk ke tengah.
            float angle = 90f - 90f * i;
            float radians = angle * Mathf.Deg2Rad;

            triangles[i].rect.anchoredPosition = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * triangleDistance;
            triangles[i].rect.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            triangles[i].rect.sizeDelta = new Vector2(triangleSize, triangleSize);
            triangles[i].fill.color = lockColor;
        }

        dot.color = lockColor;

        label.color = lockColor;
        label.rectTransform.anchoredPosition = new Vector2(0f, -(triangleDistance + triangleSize + 6f));
    }

    // Pusat dan ukuran badan musuh di Canvas, dari delapan sudut bounds collider badannya.
    private bool Measure(EnemyHealth target, out Vector2 center, out float measuredSize)
    {
        center = position;
        measuredSize = size;

        Bounds bounds;

        if (shownCollider != null && shownCollider.enabled)
        {
            bounds = shownCollider.bounds;
        }
        else
        {
            // Tanpa collider badan, pakai perkiraan setinggi 2 dan selebar 1 dari pivot.
            Vector3 pivot = target.transform.position;
            bounds = new Bounds(pivot + Vector3.up, new Vector3(1f, 2f, 1f));
        }

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        corners3D[0] = new Vector3(min.x, min.y, min.z);
        corners3D[1] = new Vector3(max.x, min.y, min.z);
        corners3D[2] = new Vector3(min.x, max.y, min.z);
        corners3D[3] = new Vector3(max.x, max.y, min.z);
        corners3D[4] = new Vector3(min.x, min.y, max.z);
        corners3D[5] = new Vector3(max.x, min.y, max.z);
        corners3D[6] = new Vector3(min.x, max.y, max.z);
        corners3D[7] = new Vector3(max.x, max.y, max.z);

        Vector2 low = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 high = new Vector2(float.MinValue, float.MinValue);

        for (int i = 0; i < corners3D.Length; i++)
        {
            Vector3 screen = cam.WorldToScreenPoint(corners3D[i]);

            if (screen.z <= 0f)
                return false;

            low = Vector2.Min(low, screen);
            high = Vector2.Max(high, screen);
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, low, null, out Vector2 localLow) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, high, null, out Vector2 localHigh))
            return false;

        center = (localLow + localHigh) * 0.5f;
        measuredSize = Mathf.Clamp(Mathf.Max(localHigh.x - localLow.x, localHigh.y - localLow.y) * padding, minSize, maxSize);
        return true;
    }

    private static float EaseOut(float t)
    {
        float inverse = 1f - t;
        return 1f - inverse * inverse * inverse;
    }

    // Collider badan musuh (bukan trigger). Dipakai untuk mengukur besar musuh di layar.
    private static Collider FindBodyCollider(EnemyHealth target)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>();

        foreach (Collider candidate in colliders)
        {
            if (!candidate.isTrigger)
                return candidate;
        }

        return null;
    }

    private static RectTransform CreateCentered(string name, Transform parent)
    {
        RectTransform created = UIKit.CreateRect(name, parent);
        created.anchorMin = created.anchorMax = new Vector2(0.5f, 0.5f);
        created.pivot = new Vector2(0.5f, 0.5f);
        created.sizeDelta = Vector2.zero;
        return created;
    }

    private Layered CreateLayered(string name, Transform parent, Sprite inkSprite, Sprite fillSprite)
    {
        RectTransform holder = CreateCentered(name, parent);

        Image ink = CreateImage("Ink", holder, inkSprite);
        UIKit.Stretch(ink.rectTransform);
        ink.color = UIKit.Ink;

        Image fill = CreateImage("Fill", holder, fillSprite);
        UIKit.Stretch(fill.rectTransform);

        return new Layered { rect = holder, ink = ink, fill = fill };
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite)
    {
        Image image = UIKit.CreateImage(name, parent, Color.white, false);
        image.sprite = sprite;
        image.type = Image.Type.Simple;

        RectTransform imageRect = image.rectTransform;
        imageRect.anchorMin = imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);

        return image;
    }
}
