using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Modal Pengaturan, dibuka dari menu utama dan menu jeda. Dua kolom:
//   Suara      slider Volume utama, Musik, dan Efek suara (GameAudioSettings)
//   Kontroler  getaran aktif/mati, intensitas getaran, dan tombol gamepad tiap aksi (ControllerBindings)
// Nilai langsung berlaku dan disimpan ke PlayerPrefs saat ditutup.
// Navigasi: panah/D-pad/stik pindah item. Slider diubah dengan kiri/kanan di keyboard, atau LT/RT di gamepad
// (TriggerSlider). Esc/Menu/B menutup (diurus GameUI).
public class SettingsScreen : UIScreen
{
    private const float ColumnWidth = 640f;
    private const float RowHeight = 50f;

    private Slider masterSlider;
    private Slider musicSlider;
    private Slider sfxSlider;
    private TextMeshProUGUI masterValue;
    private TextMeshProUGUI musicValue;
    private TextMeshProUGUI sfxValue;

    private Button vibrationToggle;
    private TextMeshProUGUI vibrationLabel;
    private Slider vibrationSlider;
    private TextMeshProUGUI vibrationValue;

    private readonly Dictionary<PadAction, TextMeshProUGUI> bindingLabels = new Dictionary<PadAction, TextMeshProUGUI>();
    private TextMeshProUGUI padStatus;
    // Pesan sementara (mis. "hubungkan gamepad dulu") tidak langsung ditimpa status koneksi.
    private float statusHoldUntil;

    // Aksi yang sedang menunggu tombol baru (rebind).
    private PadAction? rebinding;

    private readonly List<Button> bindingButtons = new List<Button>();
    private Button resetButton;
    private Button closeButton;
    private Coroutine restoreNavigation;

    protected override void Build()
    {
        Image dim = UIKit.CreateImage("Dim", rect, UIKit.Backdrop, false);
        dim.raycastTarget = true;
        UIKit.Stretch(dim.rectTransform);

        Image panel = UIKit.CreatePanel("Panel", rect, UIKit.Panel);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ColumnWidth * 2f + 48f * 2f + 56f, 0f));
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

        RectTransform columns = UIKit.CreateRect("Columns", panel.transform);
        HorizontalLayoutGroup columnLayout = UIKit.AddHorizontal(columns.gameObject, 56f, null, TextAnchor.UpperLeft);
        columnLayout.childForceExpandHeight = false;

        RectTransform sound = CreateColumn("Sound", columns);
        RectTransform pad = CreateColumn("Controller", columns);

        BuildSound(sound);
        BuildController(pad);

        UIKit.CreateDivider(panel.transform);

        TextMeshProUGUI hint = UIKit.CreateText("KeyHint", panel.transform, string.Empty, 18f, UIKit.TextMuted);
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        ControlHintLabel.Bind(hint, () => ControlHints.IsGamepad
            ? "<b>" + ControlHints.Navigate + "</b> pindah   |   <b>" + ControlHints.Confirm + "</b> pilih / ubah tombol   |   <b>(LT) / (RT)</b> ubah nilai   |   <b>" +
              ControlHints.Back + "</b> tutup"
            : "<b>Panah atas / bawah</b> pilih   |   <b>kiri / kanan</b> ubah nilai   |   <b>Enter</b> ubah tombol   |   <b>Esc</b> tutup");

        RectTransform buttons = UIKit.CreateRect("Buttons", panel.transform);
        UIKit.AddHorizontal(buttons.gameObject, 14f, null, TextAnchor.MiddleRight);
        UIKit.Layout(buttons, 64f);

        Button reset = UIKit.CreateButton("ResetButton", buttons, "ATUR ULANG", UIKit.Safe(ResetAll));
        resetButton = reset;
        UIKit.Layout(reset, 64f, 220f);

        Button close = UIKit.CreateButton("CloseButton", buttons, "TUTUP", UIKit.Safe(ui.CloseSettings), 64f, true);
        closeButton = close;
        UIKit.Layout(close, 64f, 200f);

        // Fokus awal di slider pertama, jadi panah kiri/kanan langsung mengubah volume.
        firstSelected = masterSlider;
    }

    private static RectTransform CreateColumn(string name, Transform parent)
    {
        RectTransform column = UIKit.CreateRect(name, parent);
        UIKit.AddVertical(column.gameObject, 12f);
        UIKit.Layout(column, -1f, ColumnWidth, 0f);
        return column;
    }

    // ---------- Suara ----------

    private void BuildSound(RectTransform column)
    {
        UIKit.UseDisplayFont(UIKit.CreateText("SoundHeading", column, "Suara", 28f, UIKit.Text, TextAlignmentOptions.Left, true));

        masterSlider = CreateSliderRow(column, "Volume utama", out masterValue);
        musicSlider = CreateSliderRow(column, "Musik", out musicValue);
        sfxSlider = CreateSliderRow(column, "Efek suara", out sfxValue);

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
    }

    // ---------- Kontroler ----------

    private void BuildController(RectTransform column)
    {
        UIKit.UseDisplayFont(UIKit.CreateText("ControllerHeading", column, "Kontroler", 28f, UIKit.Text, TextAlignmentOptions.Left, true));

        padStatus = UIKit.CreateText("PadStatus", column, string.Empty, 17f, UIKit.TextMuted);
        padStatus.textWrappingMode = TextWrappingModes.NoWrap;

        // Getaran aktif/mati.
        RectTransform toggleRow = CreateRow(column, "Getaran");
        vibrationToggle = UIKit.CreateButton("VibrationToggle", toggleRow, "AKTIF", UIKit.Safe(ToggleVibration), 48f);
        UIKit.Layout(vibrationToggle, 48f, 200f);
        vibrationLabel = vibrationToggle.GetComponentInChildren<TextMeshProUGUI>();
        vibrationLabel.fontSize = 22f;

        vibrationSlider = CreateSliderRow(column, "Kekuatan", out vibrationValue);
        vibrationSlider.onValueChanged.AddListener(value =>
        {
            GamepadRumble.Intensity = value;
            vibrationValue.text = Percent(value);
            GamepadRumble.Preview();
        });

        UIKit.CreateText("BindingHeading", column, "Tombol gamepad (pilih lalu tekan tombol baru)", 18f, UIKit.TextMuted, TextAlignmentOptions.Left, true);

        foreach (PadAction action in ControllerBindings.All)
        {
            RectTransform row = CreateRow(column, ControllerBindings.Label(action));
            PadAction captured = action;

            Button button = UIKit.CreateButton("Bind_" + action, row, string.Empty, UIKit.Safe(() => BeginRebind(captured)), 44f);
            UIKit.Layout(button, 44f, 260f);

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.fontSize = 22f;
            // Huruf tombol berwarna (Y kuning dst.) butuh font biasa, karena font display tidak punya semua glyph.
            label.font = padStatus.font;
            bindingLabels[action] = label;
            bindingButtons.Add(button);
        }
    }

    private static RectTransform CreateRow(Transform parent, string title)
    {
        RectTransform row = UIKit.CreateRect(title, parent);
        UIKit.AddHorizontal(row.gameObject, 20f, null, TextAnchor.MiddleLeft);
        UIKit.Layout(row, RowHeight);

        TextMeshProUGUI label = UIKit.CreateText("Label", row, title, 24f, UIKit.Text);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(label, -1f, 220f, 1f);

        return row;
    }

    // Baris: label di kiri, slider melebar di tengah, angka persen di kanan.
    private static Slider CreateSliderRow(Transform parent, string title, out TextMeshProUGUI value)
    {
        RectTransform row = UIKit.CreateRect(title, parent);
        UIKit.AddHorizontal(row.gameObject, 20f, null, TextAnchor.MiddleLeft);
        UIKit.Layout(row, RowHeight);

        TextMeshProUGUI label = UIKit.CreateText("Label", row, title, 24f, UIKit.Text);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(label, -1f, 190f, 0f);

        Slider slider = UIKit.CreateSlider("Slider", row);

        value = UIKit.UseDisplayFont(UIKit.CreateText("Value", row, "100%", 24f, UIKit.Text, TextAlignmentOptions.Right, true));
        value.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(value, -1f, 84f, 0f);

        return slider;
    }

    private void ToggleVibration()
    {
        GamepadRumble.Enabled = !GamepadRumble.Enabled;
        RefreshController();

        if (GamepadRumble.Enabled)
            GamepadRumble.Preview();
    }

    // ---------- Rebind ----------

    private void BeginRebind(PadAction action)
    {
        if (ControllerBindings.IsRebinding)
            return;

        if (Gamepad.all.Count == 0)
        {
            padStatus.text = "<color=" + UIKit.TradeOffHex + ">Hubungkan gamepad dulu untuk mengubah tombol.</color>";
            statusHoldUntil = Time.unscaledTime + 2.5f;
            return;
        }

        // Navigasi UI dimatikan selama menunggu tombol, supaya tombol A tidak ikut menekan tombol menu.
        SetNavigation(false);

        if (!ControllerBindings.StartRebind(action, success => HandleRebindFinished()))
        {
            SetNavigation(true);
            return;
        }

        rebinding = action;
        bindingLabels[action].text = "<color=" + UIKit.AccentHex + ">Tekan tombol...</color>";
        padStatus.text = "Tekan tombol gamepad untuk <b>" + ControllerBindings.Label(action) + "</b>.  <b>Esc</b> batal.";
    }

    private void HandleRebindFinished()
    {
        rebinding = null;
        RefreshController();

        if (restoreNavigation != null)
            StopCoroutine(restoreNavigation);

        if (isActiveAndEnabled)
            restoreNavigation = StartCoroutine(RestoreNavigationWhenReleased());
        else
            SetNavigation(true);
    }

    // Navigasi baru dinyalakan lagi setelah semua tombol gamepad dilepas, supaya tombol yang baru dipasang
    // (mis. A) tidak langsung memicu Submit pada tombol yang sedang dipilih.
    private IEnumerator RestoreNavigationWhenReleased()
    {
        yield return null;

        while (AnyGamepadButtonHeld())
            yield return null;

        SetNavigation(true);
        restoreNavigation = null;
    }

    private static bool AnyGamepadButtonHeld()
    {
        foreach (Gamepad pad in Gamepad.all)
        {
            if (pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed || pad.buttonNorth.isPressed ||
                pad.leftShoulder.isPressed || pad.rightShoulder.isPressed || pad.leftTrigger.isPressed || pad.rightTrigger.isPressed ||
                pad.startButton.isPressed || pad.selectButton.isPressed)
                return true;
        }

        return false;
    }

    private static void SetNavigation(bool enabled)
    {
        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = enabled;
    }

    // ---------- Show / hide ----------

    protected override void OnShow()
    {
        Refresh();
    }

    // Nilai disimpan ke disk saat modal ditutup, bukan setiap slider bergeser.
    protected override void OnHide()
    {
        if (ControllerBindings.IsRebinding)
            ControllerBindings.CancelRebind();

        if (restoreNavigation != null)
        {
            StopCoroutine(restoreNavigation);
            restoreNavigation = null;
        }

        SetNavigation(true);
        GameAudioSettings.Save();
        PlayerPrefs.Save();
    }

    protected override void Update()
    {
        base.Update();

        if (IsVisible && rebinding == null)
            RefreshPadStatus();
    }

    private void ResetAll()
    {
        GameAudioSettings.ResetToDefault();
        GamepadRumble.ResetToDefault();
        ControllerBindings.ResetToDefault();
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

        RefreshController();
    }

    private void RefreshController()
    {
        bool vibration = GamepadRumble.Enabled;
        vibrationLabel.text = vibration ? "AKTIF" : "MATI";
        vibrationSlider.SetValueWithoutNotify(GamepadRumble.Intensity);
        vibrationSlider.interactable = vibration;
        vibrationValue.text = Percent(GamepadRumble.Intensity);

        foreach (KeyValuePair<PadAction, TextMeshProUGUI> pair in bindingLabels)
        {
            if (rebinding.HasValue && rebinding.Value == pair.Key)
                continue;

            pair.Value.text = ControlHints.PadGlyph(pair.Key);
        }

        BuildNavigation();
        RefreshPadStatus();
    }

    // Navigasi eksplisit: atas/bawah di dalam kolom, kiri/kanan pindah kolom pada baris yang sejajar.
    // Navigasi otomatis Unity melewati slider Kekuatan karena letaknya tidak sejajar dengan tombol di atasnya.
    // Slider Kekuatan dilewati saat getaran mati.
    private void BuildNavigation()
    {
        List<Selectable> leftColumn = new List<Selectable> { masterSlider, musicSlider, sfxSlider };
        List<Selectable> rightColumn = new List<Selectable> { vibrationToggle };

        if (vibrationSlider.interactable)
            rightColumn.Add(vibrationSlider);

        rightColumn.AddRange(bindingButtons);

        for (int i = 0; i < leftColumn.Count; i++)
        {
            SetNavigation(leftColumn[i],
                i > 0 ? leftColumn[i - 1] : null,
                i < leftColumn.Count - 1 ? leftColumn[i + 1] : resetButton,
                null,
                rightColumn[Mathf.Min(i, rightColumn.Count - 1)]);
        }

        for (int i = 0; i < rightColumn.Count; i++)
        {
            SetNavigation(rightColumn[i],
                i > 0 ? rightColumn[i - 1] : null,
                i < rightColumn.Count - 1 ? rightColumn[i + 1] : closeButton,
                leftColumn[Mathf.Min(i, leftColumn.Count - 1)],
                null);
        }

        SetNavigation(resetButton, leftColumn[leftColumn.Count - 1], null, null, closeButton);
        SetNavigation(closeButton, rightColumn[rightColumn.Count - 1], null, resetButton, null);
    }

    private static void SetNavigation(Selectable selectable, Selectable up, Selectable down, Selectable left, Selectable right)
    {
        selectable.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = up,
            selectOnDown = down,
            selectOnLeft = left,
            selectOnRight = right
        };
    }

    private void RefreshPadStatus()
    {
        if (Time.unscaledTime < statusHoldUntil)
            return;

        Gamepad pad = ControlScheme.ActiveGamepad ?? Gamepad.current;
        string text;

        if (pad == null)
            text = "<color=" + UIKit.MutedHex + ">Belum ada gamepad terhubung.</color>";
        else if (JoystickGamepadBridge.IsVirtual(pad))
            text = "Terhubung: <b>" + JoystickGamepadBridge.SourceName(pad) + "</b> <color=" + UIKit.MutedHex +
                   ">(mode DirectInput, tanpa getaran)</color>";
        else
            text = "Terhubung: <b>" + pad.displayName + "</b>";

        int count = Gamepad.all.Count;

        if (count > 1)
            text += " <color=" + UIKit.MutedHex + ">+" + (count - 1) + " lainnya</color>";

        if (padStatus.text != text)
            padStatus.text = text;
    }

    private static string Percent(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
    }
}
