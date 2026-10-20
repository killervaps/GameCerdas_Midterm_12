using TMPro;
using UnityEngine;

// Widget meter ancaman: bar tiga zona (Aman / Sedang / Bahaya) dengan penanda rasio build sekarang,
// plus penanda kedua (garis putus) untuk rasio setelah gate yang sedang dilihat.
// Bukan MonoBehaviour. Pemiliknya memanggil Tick() setiap frame untuk animasi penanda.
public class ThreatMeter
{
    private readonly RectTransform bar;
    private readonly RectTransform safeZone;
    private readonly RectTransform mediumZone;
    private readonly RectTransform dangerZone;
    private readonly RectTransform marker;
    private readonly RectTransform ghostMarker;
    private readonly TextMeshProUGUI headline;
    private readonly TextMeshProUGUI detail;

    private float maxRatio = 2f;
    private float shown;
    private float target;
    private float ghostShown;
    private float ghostTarget;
    private bool hasGhost;

    public ThreatMeter(Transform parent)
    {
        headline = UIKit.CreateText("ThreatHeadline", parent, string.Empty, 26f, UIKit.Text, TextAlignmentOptions.Left, true);

        RectTransform holder = UIKit.CreateRect("ThreatBar", parent);
        UIKit.Layout(holder, 34f);

        bar = UIKit.CreateImage("Track", holder, new Color(0f, 0f, 0f, 0.4f)).rectTransform;
        bar.anchorMin = new Vector2(0f, 0.5f);
        bar.anchorMax = new Vector2(1f, 0.5f);
        bar.sizeDelta = new Vector2(0f, 14f);
        bar.anchoredPosition = Vector2.zero;

        safeZone = UIKit.CreateImage("Safe", bar, Faded(ThreatLevel.Safe), false).rectTransform;
        mediumZone = UIKit.CreateImage("Medium", bar, Faded(ThreatLevel.Medium), false).rectTransform;
        dangerZone = UIKit.CreateImage("Danger", bar, Faded(ThreatLevel.Danger), false).rectTransform;

        ghostMarker = UIKit.CreateImage("GhostMarker", bar, new Color(1f, 1f, 1f, 0.55f)).rectTransform;
        ghostMarker.sizeDelta = new Vector2(6f, 18f);

        marker = UIKit.CreateImage("Marker", bar, Color.white).rectTransform;
        marker.sizeDelta = new Vector2(8f, 26f);

        detail = UIKit.CreateText("ThreatDetail", parent, string.Empty, 19f, UIKit.TextMuted);
    }

    private static Color Faded(ThreatLevel level)
    {
        Color color = UIKit.ThreatColor(level);
        color.a = 0.55f;
        return color;
    }

    public void Configure(float safeBelow, float dangerAbove)
    {
        maxRatio = Mathf.Max(2f, dangerAbove * 1.6f);

        UIKit.SetHorizontalSpan(safeZone, 0f, safeBelow / maxRatio);
        UIKit.SetHorizontalSpan(mediumZone, safeBelow / maxRatio, dangerAbove / maxRatio);
        UIKit.SetHorizontalSpan(dangerZone, dangerAbove / maxRatio, 1f);
    }

    public void SetEmpty(string message)
    {
        headline.text = "Ancaman  <color=" + UIKit.MutedHex + ">-</color>";
        detail.text = message;
        hasGhost = false;
        ghostMarker.gameObject.SetActive(false);
        target = 0f;
    }

    public void Set(ThreatResult now, ThreatResult? after)
    {
        target = Normalize(now.ratio);

        headline.text = "Ancaman  <color=" + now.ColorHex + ">" + now.Label.ToUpperInvariant() + "</color>" +
                        "  <size=75%><color=" + UIKit.MutedHex + ">" + UIKit.FormatRatio(now.ratio) + "</color></size>";

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

            string arrow = next.ratio < now.ratio - 0.005f ? "turun" : next.ratio > now.ratio + 0.005f ? "naik" : "tetap";

            headline.text += "  <size=75%><color=" + UIKit.MutedHex + ">-></color></size>  <color=" + next.ColorHex + ">" +
                             next.Label.ToUpperInvariant() + "</color>  <size=75%><color=" + UIKit.MutedHex + ">" +
                             UIKit.FormatRatio(next.ratio) + "</color></size>";

            detail.text = "Jika gate ini dipilih, ancaman <b>" + arrow + "</b>.  Musuh " + Compact(next.enemyPower) +
                          " vs MC " + Compact(next.playerPower);
        }
        else
        {
            hasGhost = false;
            ghostMarker.gameObject.SetActive(false);
            detail.text = "Power musuh " + Compact(now.enemyPower) + " vs Power MC " + Compact(now.playerPower);
        }
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
