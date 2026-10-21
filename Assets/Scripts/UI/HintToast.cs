using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Tutorial kontekstual: petunjuk singkat di atas layar yang muncul sekali di momen pertama
// (masuk Safezone, mendekati gate, pertempuran pertama, ronde bersih). Antre jika muncul bersamaan.
public class HintToast : UIScreen
{
    private const string PrefsPrefix = "MathGate.Hint.";

    [SerializeField] private float duration = 6.5f;

    [Header("Lebar")]
    [SerializeField] private float maxWidth = 980f;
    [SerializeField] private float minWidth = 520f;
    // Jarak minimum dari tepi kanan kartu HUD kiri atas. Toast selebar 980 menimpa kartu itu
    // di layar 16:10 dan 4:3, karena lebar canvas di sana lebih sempit dari 1920.
    [SerializeField] private float hudGap = 24f;
    // Dipakai jika HUD tidak ditemukan (posisi kartu 28 + lebar 430 + jarak 24).
    [SerializeField] private float fallbackSideClearance = 482f;

    // Diingat selama aplikasi berjalan, juga melewati "Ulangi Run" dan "Menu Utama".
    private static readonly HashSet<string> seenThisSession = new HashSet<string>();

    private readonly Queue<string> queue = new Queue<string>();

    private TextMeshProUGUI label;
    private RectTransform timerFill;
    private RectTransform panelRect;
    private float remaining;

    protected override void Build()
    {
        Image panel = UIKit.CreatePanel("Panel", rect, UIKit.Panel);
        panelRect = panel.rectTransform;
        UIKit.Place(panelRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(maxWidth, 0f));
        UIKit.AddHorizontal(panel.gameObject, 18f, new RectOffset(24, 28, 18, 22), TextAnchor.MiddleLeft);
        UIKit.FitHeight(panel.gameObject);

        TextMeshProUGUI badge = UIKit.CreateText("Badge", panel.transform, "Tips", 20f, UIKit.TextMuted, TextAlignmentOptions.Center, true);
        UIKit.Layout(badge, 30f, 70f, 0f);

        label = UIKit.CreateText("Label", panel.transform, string.Empty, 24f, UIKit.Text);
        UIKit.Layout(label, -1f, -1f, 1f);

        // Bar tipis di bawah sebagai penanda sisa waktu.
        Image timerBack = UIKit.CreateImage("Timer", panel.transform, new Color(1f, 1f, 1f, 0.06f), false);
        LayoutElement timerLayout = timerBack.gameObject.AddComponent<LayoutElement>();
        timerLayout.ignoreLayout = true;
        RectTransform timerRect = timerBack.rectTransform;
        timerRect.anchorMin = new Vector2(0f, 0f);
        timerRect.anchorMax = new Vector2(1f, 0f);
        timerRect.pivot = new Vector2(0.5f, 0f);
        timerRect.offsetMin = new Vector2(14f, 6f);
        timerRect.offsetMax = new Vector2(-14f, 10f);

        Image fill = UIKit.CreateImage("Fill", timerBack.transform, new Color(1f, 1f, 1f, 0.35f), false);
        timerFill = fill.rectTransform;
        UIKit.SetBarFill(timerFill, 1f);
    }

    public void ShowOnce(string key, string message, bool remember)
    {
        if (seenThisSession.Contains(key))
            return;

        if (remember && PlayerPrefs.GetInt(PrefsPrefix + key, 0) == 1)
            return;

        seenThisSession.Add(key);

        if (remember)
        {
            PlayerPrefs.SetInt(PrefsPrefix + key, 1);
            PlayerPrefs.Save();
        }

        queue.Enqueue(message);

        if (!IsVisible)
            ShowNext();
    }

    // Untuk pengujian: semua petunjuk muncul lagi.
    public static void ResetSeen()
    {
        seenThisSession.Clear();

        foreach (string key in new[] { "safezone", "gate", "battle", "cleared" })
            PlayerPrefs.DeleteKey(PrefsPrefix + key);
    }

    protected override void OnHide()
    {
        queue.Clear();
    }

    private void ShowNext()
    {
        if (queue.Count == 0)
        {
            Hide();
            return;
        }

        label.text = queue.Dequeue();
        remaining = duration;
        UIKit.SetBarFill(timerFill, 1f);
        FitWidth();
        Show();
    }

    // Toast di tengah atas dibatasi supaya tidak masuk ke area kartu HUD kiri atas. Kartu timer
    // di kanan lebih sempit, jadi cukup menjaga sisi kiri lalu dicerminkan ke kanan.
    private void FitWidth()
    {
        float side = fallbackSideClearance;

        if (ui != null && ui.Hud != null)
        {
            RectTransform card = ui.Hud.CardRect;
            side = card.anchoredPosition.x + card.rect.width + hudGap;
        }

        float width = Mathf.Clamp(rect.rect.width - 2f * side, minWidth, maxWidth);

        if (!Mathf.Approximately(panelRect.sizeDelta.x, width))
            panelRect.sizeDelta = new Vector2(width, panelRect.sizeDelta.y);
    }

    protected override void Update()
    {
        base.Update();

        if (!IsVisible)
            return;

        // Dicek tiap frame karena resolusi bisa berubah (mis. ukuran Game view diubah saat Play).
        FitWidth();

        // Waktu berhenti saat jeda supaya petunjuk tidak hilang tanpa terbaca.
        if (ui != null && ui.IsPaused)
            return;

        remaining -= Time.unscaledDeltaTime;
        UIKit.SetBarFill(timerFill, remaining / duration);

        if (remaining <= 0f)
        {
            if (queue.Count > 0)
                ShowNext();
            else
                Hide();
        }
    }
}
