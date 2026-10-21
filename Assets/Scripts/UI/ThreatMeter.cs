using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Widget meter ancaman gaya stiker: cap level ancaman (miring), lalu bar tiga zona (Aman / Sedang / Bahaya)
// bergaris tinta dengan penanda krem untuk rasio build sekarang, plus penanda kedua yang lebih pudar untuk
// rasio setelah gate yang sedang dilihat.
// Bukan MonoBehaviour. Pemiliknya memanggil Tick() setiap frame untuk animasi penanda.
public class ThreatMeter
{
    // Tebal garis tinta di antara zona dan di tepi bar.
    private const float Gap = 3f;

    private readonly RectTransform bar;
    private readonly RectTransform safeZone;
    private readonly RectTransform mediumZone;
    private readonly RectTransform dangerZone;
    private readonly RectTransform marker;
    private readonly RectTransform ghostMarker;
    private readonly Image nowStamp;
    private readonly TextMeshProUGUI nowLabel;
    private readonly GameObject arrow;
    private readonly Image afterStamp;
    private readonly TextMeshProUGUI afterLabel;
    private readonly TextMeshProUGUI detail;

    private float maxRatio = 2f;
    private float shown;
    private float target;
    private float ghostShown;
    private float ghostTarget;
    private bool hasGhost;

    public ThreatMeter(Transform parent)
    {
        RectTransform headline = UIKit.CreateRect("ThreatHeadline", parent);
        UIKit.AddHorizontal(headline.gameObject, 10f, new RectOffset(0, 0, 2, 2), TextAnchor.MiddleLeft);

        nowStamp = UIKit.CreateStamp("NowStamp", headline, 24f, -4f, out nowLabel);

        TextMeshProUGUI arrowText = UIKit.UseDisplayFont(UIKit.CreateText("Arrow", headline, "→", 26f, UIKit.TextMuted, TextAlignmentOptions.Center, true));
        arrow = arrowText.gameObject;

        afterStamp = UIKit.CreateStamp("AfterStamp", headline, 24f, 3f, out afterLabel);

        RectTransform holder = UIKit.CreateRect("ThreatBar", parent);
        UIKit.Layout(holder, 34f);

        bar = UIKit.SetCorners(UIKit.CreateImage("Track", holder, UIKit.Ink), UIKit.RoundedSmall).rectTransform;
        bar.anchorMin = new Vector2(0f, 0.5f);
        bar.anchorMax = new Vector2(1f, 0.5f);
        bar.sizeDelta = new Vector2(0f, 20f);
        bar.anchoredPosition = Vector2.zero;

        safeZone = UIKit.CreateImage("Safe", bar, UIKit.ThreatColor(ThreatLevel.Safe), false).rectTransform;
        mediumZone = UIKit.CreateImage("Medium", bar, UIKit.ThreatColor(ThreatLevel.Medium), false).rectTransform;
        dangerZone = UIKit.CreateImage("Danger", bar, UIKit.ThreatColor(ThreatLevel.Danger), false).rectTransform;

        ghostMarker = UIKit.SetCorners(UIKit.CreateImage("GhostMarker", bar, new Color(UIKit.Cream.r, UIKit.Cream.g, UIKit.Cream.b, 0.6f)), UIKit.InkSmall).rectTransform;
        ghostMarker.sizeDelta = new Vector2(10f, 24f);

        marker = UIKit.SetCorners(UIKit.CreateImage("Marker", bar, UIKit.Cream), UIKit.InkSmall).rectTransform;
        marker.sizeDelta = new Vector2(12f, 32f);

        detail = UIKit.CreateText("ThreatDetail", parent, string.Empty, 18f, UIKit.TextMuted);
    }

    public void Configure(float safeBelow, float dangerAbove)
    {
        maxRatio = Mathf.Max(2f, dangerAbove * 1.6f);

        SetZone(safeZone, 0f, safeBelow / maxRatio, Gap, Gap * 0.5f);
        SetZone(mediumZone, safeBelow / maxRatio, dangerAbove / maxRatio, Gap * 0.5f, Gap * 0.5f);
        SetZone(dangerZone, dangerAbove / maxRatio, 1f, Gap * 0.5f, Gap);
    }

    // Zona diinset supaya track tinta terlihat sebagai garis di tepi dan di antara zona.
    private static void SetZone(RectTransform zone, float from, float to, float leftInset, float rightInset)
    {
        UIKit.SetHorizontalSpan(zone, from, to);
        zone.offsetMin = new Vector2(leftInset, Gap);
        zone.offsetMax = new Vector2(-rightInset, -Gap);
    }

    public void SetEmpty(string message)
    {
        UIKit.SetStamp(nowStamp, nowLabel, "-", UIKit.Well);
        nowLabel.color = UIKit.TextMuted;
        ShowAfter(false);
        detail.text = message;
        hasGhost = false;
        ghostMarker.gameObject.SetActive(false);
        target = 0f;
    }

    public void Set(ThreatResult now, ThreatResult? after)
    {
        target = Normalize(now.ratio);
        UIKit.SetStamp(nowStamp, nowLabel, now.Label, UIKit.ThreatColor(now.level));

        if (after.HasValue)
        {
            ThreatResult next = after.Value;
            hasGhost = true;
            ghostTarget = Normalize(next.ratio);

            if (!ghostMarker.gameObject.activeSelf)
            {
                ghostMarker.gameObject.SetActive(true);
                ghostShown = shown;
            }

            ShowAfter(true);
            UIKit.SetStamp(afterStamp, afterLabel, next.Label, UIKit.ThreatColor(next.level));

            string change = next.ratio < now.ratio - 0.005f ? "turun" : next.ratio > now.ratio + 0.005f ? "naik" : "tetap";
            detail.text = "Jika gate ini dipilih, ancaman <b>" + change + "</b> (" + UIKit.FormatRatio(now.ratio) + " → " +
                          UIKit.FormatRatio(next.ratio) + "). Musuh " + Compact(next.enemyPower) + " vs kamu " + Compact(next.playerPower);
        }
        else
        {
            hasGhost = false;
            ghostMarker.gameObject.SetActive(false);
            ShowAfter(false);
            detail.text = "Rasio " + UIKit.FormatRatio(now.ratio) + " · musuh " + Compact(now.enemyPower) + " vs kamu " + Compact(now.playerPower);
        }
    }

    private void ShowAfter(bool visible)
    {
        if (arrow.activeSelf != visible)
            arrow.SetActive(visible);

        if (afterStamp.gameObject.activeSelf != visible)
            afterStamp.gameObject.SetActive(visible);
    }

    public void Tick(float deltaTime)
    {
        shown = Mathf.Lerp(shown, target, 1f - Mathf.Exp(-10f * deltaTime));
        PlaceMarker(marker, shown);

        if (hasGhost)
        {
            ghostShown = Mathf.Lerp(ghostShown, ghostTarget, 1f - Mathf.Exp(-10f * deltaTime));
            PlaceMarker(ghostMarker, ghostShown);
        }
    }

    private float Normalize(float ratio)
    {
        if (float.IsNaN(ratio) || float.IsInfinity(ratio))
            return 1f;

        return Mathf.Clamp01(ratio / maxRatio);
    }

    private static void PlaceMarker(RectTransform target, float value)
    {
        target.anchorMin = new Vector2(value, 0.5f);
        target.anchorMax = new Vector2(value, 0.5f);
        target.pivot = new Vector2(0.5f, 0.5f);
        target.anchoredPosition = Vector2.zero;
    }

    // Angka power bisa ribuan, jadi disingkat (mis. 12.3k).
    private static string Compact(float value)
    {
        if (float.IsInfinity(value) || float.IsNaN(value))
            return "-";

        if (value >= 1000000f)
            return (value / 1000000f).ToString("0.#") + "M";

        if (value >= 1000f)
            return (value / 1000f).ToString("0.#") + "k";

        return value.ToString("0");
    }
}
