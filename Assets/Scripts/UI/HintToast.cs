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

    // Diingat selama aplikasi berjalan, juga melewati "Ulangi Run" dan "Menu Utama".
    private static readonly HashSet<string> seenThisSession = new HashSet<string>();

    private readonly Queue<string> queue = new Queue<string>();

    private TextMeshProUGUI label;
    private RectTransform timerFill;
    private float remaining;

    protected override void Build()
    {
        Image panel = UIKit.CreatePanel("Panel", rect, new Color(0.08f, 0.1f, 0.14f, 0.94f));
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(980f, 0f));
        UIKit.AddHorizontal(panel.gameObject, 18f, new RectOffset(24, 28, 18, 22), TextAnchor.MiddleLeft);
        UIKit.FitHeight(panel.gameObject);

        TextMeshProUGUI badge = UIKit.CreateText("Badge", panel.transform, "TIPS", 20f, UIKit.Accent, TextAlignmentOptions.Center, true);
        badge.characterSpacing = 3f;
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

        Image fill = UIKit.CreateImage("Fill", timerBack.transform, UIKit.Accent, false);
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
        Show();
    }

    protected override void Update()
    {
        base.Update();

        if (!IsVisible)
            return;

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
