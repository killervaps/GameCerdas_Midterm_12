using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Modal Pengaturan, dibuka dari menu utama dan menu jeda. Berisi slider Volume utama, Musik, dan Efek suara.
// Nilai langsung terdengar saat slider digeser dan disimpan ke PlayerPrefs (GameAudioSettings) saat ditutup.
// Navigasi: panah atas/bawah pindah slider, kiri/kanan ubah volume. Esc menutup (diurus GameUI).
public class SettingsScreen : UIScreen
{
    private const float PanelWidth = 760f;

    private Slider masterSlider;
    private Slider musicSlider;
    private Slider sfxSlider;
    private TextMeshProUGUI masterValue;
    private TextMeshProUGUI musicValue;
    private TextMeshProUGUI sfxValue;

    protected override void Build()
    {
        Image dim = UIKit.CreateImage("Dim", rect, UIKit.Backdrop, false);
        dim.raycastTarget = true;
        UIKit.Stretch(dim.rectTransform);

        Image panel = UIKit.CreatePanel("Panel", rect, UIKit.Panel);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelWidth, 0f));
        UIKit.AddVertical(panel.gameObject, 16f, new RectOffset(48, 48, 60, 36));
        UIKit.FitHeight(panel.gameObject);

        // Tab stiker krem di tepi atas panel, sama seperti Cara Bermain. Di luar layout panel.
        Image header = UIKit.CreateFreeStamp("Header", panel.transform, "Pengaturan", 30f, UIKit.Cream, -3f, out _);
        UIKit.SetShadowDistance(header, new Vector2(5f, -6f));
        UIKit.Layout(header).ignoreLayout = true;
        RectTransform headerRect = header.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(0f, 1f);
        headerRect.pivot = new Vector2(0f, 0.5f);
        headerRect.anchoredPosition = new Vector2(44f, 2f);

        UIKit.UseDisplayFont(UIKit.CreateText("SoundHeading", panel.transform, "Suara", 28f, UIKit.Text, TextAlignmentOptions.Left, true));

        masterSlider = CreateRow(panel.transform, "Volume utama", out masterValue);
        musicSlider = CreateRow(panel.transform, "Musik", out musicValue);
        sfxSlider = CreateRow(panel.transform, "Efek suara", out sfxValue);

        masterSlider.onValueChanged.AddListener(value =>
        {
            GameAudioSettings.Master = value;
            masterValue.text = Percent(value);
        });

        musicSlider.onValueChanged.AddListener(value =>
        {
            GameAudioSettings.Music = value;
            musicValue.text = Percent(value);
        });

        sfxSlider.onValueChanged.AddListener(value =>
        {
            GameAudioSettings.Sfx = value;
            sfxValue.text = Percent(value);

            // Contoh suara supaya pemain langsung mendengar volume barunya.
            SfxManager.Play(SfxCue.UiClick);
        });

        UIKit.CreateDivider(panel.transform);

        TextMeshProUGUI hint = UIKit.CreateText("KeyHint", panel.transform,
            "<b>Panah atas / bawah</b> pilih   |   <b>kiri / kanan</b> ubah volume   |   <b>Esc</b> tutup",
            18f, UIKit.TextMuted);
        hint.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform buttons = UIKit.CreateRect("Buttons", panel.transform);
        UIKit.AddHorizontal(buttons.gameObject, 14f, null, TextAnchor.MiddleRight);
        UIKit.Layout(buttons, 64f);

        Button reset = UIKit.CreateButton("ResetButton", buttons, "ATUR ULANG", UIKit.Safe(ResetVolumes));
        UIKit.Layout(reset, 64f, 220f);

        Button close = UIKit.CreateButton("CloseButton", buttons, "TUTUP", UIKit.Safe(ui.CloseSettings), 64f, true);
        UIKit.Layout(close, 64f, 200f);

        // Fokus awal di slider pertama, jadi panah kiri/kanan langsung mengubah volume.
        firstSelected = masterSlider;
    }

    // Baris: label di kiri, slider melebar di tengah, angka persen di kanan.
    private static Slider CreateRow(Transform parent, string title, out TextMeshProUGUI value)
    {
        RectTransform row = UIKit.CreateRect(title, parent);
        UIKit.AddHorizontal(row.gameObject, 20f, null, TextAnchor.MiddleLeft);
        UIKit.Layout(row, 56f);

        TextMeshProUGUI label = UIKit.CreateText("Label", row, title, 24f, UIKit.Text);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(label, -1f, 190f, 0f);

        Slider slider = UIKit.CreateSlider("Slider", row);

        value = UIKit.UseDisplayFont(UIKit.CreateText("Value", row, "100%", 24f, UIKit.Text, TextAlignmentOptions.Right, true));
        value.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(value, -1f, 84f, 0f);

        return slider;
    }

    protected override void OnShow()
    {
        Refresh();
    }

    // Nilai disimpan ke disk saat modal ditutup, bukan setiap slider bergeser.
    protected override void OnHide()
    {
        GameAudioSettings.Save();
    }

    private void ResetVolumes()
    {
        GameAudioSettings.ResetToDefault();
        Refresh();
    }

    // Slider diisi dari nilai tersimpan tanpa memicu onValueChanged (supaya tidak ada suara contoh).
    private void Refresh()
    {
        masterSlider.SetValueWithoutNotify(GameAudioSettings.Master);
        musicSlider.SetValueWithoutNotify(GameAudioSettings.Music);
        sfxSlider.SetValueWithoutNotify(GameAudioSettings.Sfx);

        masterValue.text = Percent(GameAudioSettings.Master);
        musicValue.text = Percent(GameAudioSettings.Music);
        sfxValue.text = Percent(GameAudioSettings.Sfx);
    }

    private static string Percent(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
    }
}
