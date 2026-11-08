using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Modal Pengaturan, dibuka dari menu utama dan menu jeda. Tab vertikal di kiri, isi tab di kanan:
//   Suara      slider Volume utama, Musik, dan Efek suara (GameAudioSettings)
//   Keyboard   tombol keyboard/mouse tiap aksi, termasuk gerak (KeyboardBindings)
//   Kontroler  getaran aktif/mati, intensitas getaran, dan tombol gamepad tiap aksi (ControllerBindings)
// Tab berganti saat difokuskan (atas/bawah di kolom tab) atau diklik. Kanan masuk ke isi tab, kiri kembali ke tab.
// Slider diubah dengan kiri/kanan di keyboard, atau LT/RT di gamepad (TriggerSlider). ATUR ULANG hanya
// mengembalikan isi tab yang sedang terbuka. Esc/Menu/B menutup (diurus GameUI).
public class SettingsScreen : UIScreen
{
    private const float TabWidth = 230f;
    private const float TabHeight = 64f;
    private const float ContentWidth = 760f;
    private const float ContentHeight = 640f;
    private const float RowHeight = 44f;
    private const float BindingButtonHeight = 40f;
    private const float BindingButtonWidth = 260f;
    private const float GlyphHeight = 32f;

    private static readonly string[] TabTitles = { "Suara", "Keyboard", "Kontroler" };
    private const int SoundTab = 0;
    private const int KeyboardTab = 1;
    private const int ControllerTab = 2;

    private readonly Button[] tabs = new Button[3];
    private readonly TextMeshProUGUI[] tabLabels = new TextMeshProUGUI[3];
    private readonly RectTransform[] pages = new RectTransform[3];
    private readonly List<Selectable>[] pageItems = { new List<Selectable>(), new List<Selectable>(), new List<Selectable>() };
    private int activeTab = SoundTab;

    // Suara
    private Slider masterSlider;
    private Slider musicSlider;
    private Slider sfxSlider;
    private TextMeshProUGUI masterValue;
    private TextMeshProUGUI musicValue;
    private TextMeshProUGUI sfxValue;

    // Keyboard
    private TextMeshProUGUI keyStatus;
    private readonly Dictionary<KeyAction, TextMeshProUGUI> keyLabels = new Dictionary<KeyAction, TextMeshProUGUI>();
    private KeyAction? rebindingKey;

    // Kontroler
    private Button vibrationToggle;
    private TextMeshProUGUI vibrationLabel;
    private Slider vibrationSlider;
    private TextMeshProUGUI vibrationValue;
    private TextMeshProUGUI padStatus;
    private readonly Dictionary<PadAction, TextMeshProUGUI> padLabels = new Dictionary<PadAction, TextMeshProUGUI>();
    private readonly Dictionary<PadAction, Image> padGlyphs = new Dictionary<PadAction, Image>();
    private readonly List<Button> padButtons = new List<Button>();
    private PadAction? rebindingPad;
    // Pesan sementara (mis. "hubungkan gamepad dulu") tidak langsung ditimpa status koneksi.
    private float statusHoldUntil;

    private Button resetButton;
    private Button closeButton;
    // Penghalang klik selama rebind, supaya klik mouse yang sedang direkam tidak ikut menekan tombol menu.
    private Image rebindBlocker;
    private Coroutine restoreNavigation;

    private bool IsRebinding => rebindingKey.HasValue || rebindingPad.HasValue;

    protected override void Build()
    {
        Image dim = UIKit.CreateImage("Dim", rect, UIKit.Backdrop, false);
        dim.raycastTarget = true;
        UIKit.Stretch(dim.rectTransform);

        Image panel = UIKit.CreatePanel("Panel", rect, UIKit.Panel);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(TabWidth + 28f + ContentWidth + 44f * 2f, 0f));
        UIKit.AddVertical(panel.gameObject, 16f, new RectOffset(44, 44, 60, 32));
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

        RectTransform body = UIKit.CreateRect("Body", panel.transform);
        HorizontalLayoutGroup bodyLayout = UIKit.AddHorizontal(body.gameObject, 28f, null, TextAnchor.UpperLeft);
        bodyLayout.childForceExpandHeight = false;

        BuildTabs(body);

        // Isi tab: sumur gelap bertinta dengan tinggi tetap, supaya panel tidak berubah ukuran saat tab berganti.
        Image content = UIKit.CreatePanel("Content", body, UIKit.Well, false);
        UIKit.Layout(content, ContentHeight, ContentWidth, 0f);

        pages[SoundTab] = CreatePage("SoundPage", content.transform);
        pages[KeyboardTab] = CreatePage("KeyboardPage", content.transform);
        pages[ControllerTab] = CreatePage("ControllerPage", content.transform);

        BuildSound(pages[SoundTab]);
        BuildKeyboard(pages[KeyboardTab]);
        BuildController(pages[ControllerTab]);

        UIKit.CreateDivider(panel.transform);

        TextMeshProUGUI hint = UIKit.CreateText("KeyHint", panel.transform, string.Empty, 18f, UIKit.TextMuted);
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        ControlHintLabel.Bind(hint, () => ControlHints.IsGamepad
            ? "<b>" + ControlHints.Navigate + "</b> pindah   |   <b>" + ControlHints.Confirm + "</b> pilih / ubah tombol   |   <b>" +
              ControlHints.Icon("LT", "LT") + " " + ControlHints.Icon("RT", "RT") + "</b> ubah nilai   |   <b>" + ControlHints.Back + "</b> tutup"
            : "<b>[Panah]</b> pindah   |   <b>[Enter]</b> pilih / ubah tombol   |   <b>[Kiri / Kanan]</b> ubah nilai   |   <b>[Esc]</b> tutup");

        RectTransform buttons = UIKit.CreateRect("Buttons", panel.transform);
        UIKit.AddHorizontal(buttons.gameObject, 14f, null, TextAnchor.MiddleRight);
        UIKit.Layout(buttons, 64f);

        resetButton = UIKit.CreateButton("ResetButton", buttons, "ATUR ULANG", UIKit.Safe(ResetActiveTab));
        UIKit.Layout(resetButton, 64f, 220f);

        closeButton = UIKit.CreateButton("CloseButton", buttons, "TUTUP", UIKit.Safe(ui.CloseSettings), 64f, true);
        UIKit.Layout(closeButton, 64f, 200f);

        rebindBlocker = UIKit.CreateImage("RebindBlocker", rect, new Color(0f, 0f, 0f, 0f), false);
        rebindBlocker.raycastTarget = true;
        UIKit.Stretch(rebindBlocker.rectTransform);
        rebindBlocker.gameObject.SetActive(false);

        ShowTab(SoundTab);
        firstSelected = tabs[SoundTab];
    }

    // ---------- Tab ----------

    private void BuildTabs(Transform parent)
    {
        RectTransform column = UIKit.CreateRect("Tabs", parent);
        // Padding atas/kanan memberi ruang untuk bayangan keras dan bingkai fokus tombol.
        UIKit.AddVertical(column.gameObject, 20f, new RectOffset(0, 8, 6, 0));
        UIKit.Layout(column, -1f, TabWidth, 0f);

        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            Button tab = UIKit.CreateButton("Tab_" + TabTitles[i], column, TabTitles[i], UIKit.Safe(() => SelectTab(index)), TabHeight);
            UISelectRelay.Attach(tab.gameObject, () => SelectTab(index));

            tabs[i] = tab;
            tabLabels[i] = tab.GetComponentInChildren<TextMeshProUGUI>();
        }
    }

    private void SelectTab(int index)
    {
        if (index == activeTab)
            return;

        CancelRebind();
        ShowTab(index);
    }

    private void ShowTab(int index)
    {
        activeTab = index;

        for (int i = 0; i < tabs.Length; i++)
        {
            bool active = i == index;
            pages[i].gameObject.SetActive(active);
            StyleTab(tabs[i], tabLabels[i], active);
        }

        BuildNavigation();

        if (index == ControllerTab)
            RefreshPadStatus();
    }

    // Tab aktif berwarna emas seperti tombol utama, tab lain abu-biru seperti tombol sekunder.
    private static void StyleTab(Button tab, TextMeshProUGUI label, bool active)
    {
        Color baseColor = active ? UIKit.Accent : UIKit.PanelLight;

        ColorBlock colors = tab.colors;
        colors.normalColor = baseColor;
        colors.selectedColor = baseColor;
        colors.highlightedColor = active ? UIKit.AccentHighlight : new Color(0.34f, 0.40f, 0.53f, 1f);
        colors.pressedColor = active ? UIKit.AccentDark : new Color(0.1f, 0.12f, 0.16f, 1f);
        tab.colors = colors;

        label.color = active ? new Color(0.12f, 0.09f, 0.02f) : UIKit.Text;
    }

    private static RectTransform CreatePage(string name, Transform parent)
    {
        RectTransform page = UIKit.CreateRect(name, parent);
        UIKit.Stretch(page);
        UIKit.AddVertical(page.gameObject, 8f, new RectOffset(28, 28, 22, 22));
        return page;
    }

    private static void CreateHeading(Transform parent, string title)
    {
        TextMeshProUGUI heading = UIKit.UseDisplayFont(UIKit.CreateText("Heading", parent, title, 28f, UIKit.Text, TextAlignmentOptions.Left, true));
        UIKit.Layout(heading, 34f);
    }

    private static TextMeshProUGUI CreateNote(Transform parent, string name, string text)
    {
        TextMeshProUGUI note = UIKit.CreateText(name, parent, text, 17f, UIKit.TextMuted);
        note.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(note, 22f);
        return note;
    }

    // ---------- Suara ----------

    private void BuildSound(RectTransform page)
    {
        CreateHeading(page, "Suara");
        CreateNote(page, "Note", "Volume langsung berlaku dan disimpan saat Pengaturan ditutup.");

        masterSlider = CreateSliderRow(page, "Volume utama", out masterValue);
        musicSlider = CreateSliderRow(page, "Musik", out musicValue);
        sfxSlider = CreateSliderRow(page, "Efek suara", out sfxValue);

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

        pageItems[SoundTab].AddRange(new Selectable[] { masterSlider, musicSlider, sfxSlider });
    }

    // ---------- Keyboard ----------

    private void BuildKeyboard(RectTransform page)
    {
        CreateHeading(page, "Keyboard");
        keyStatus = CreateNote(page, "KeyStatus", string.Empty);

        CreateNote(page, "MoveHeading", "Gerak");

        foreach (KeyAction action in KeyboardBindings.Movement)
            CreateKeyRow(page, action);

        CreateNote(page, "ActionHeading", "Aksi");

        foreach (KeyAction action in KeyboardBindings.Actions)
            CreateKeyRow(page, action);
    }

    private void CreateKeyRow(Transform parent, KeyAction action)
    {
        RectTransform row = CreateRow(parent, KeyboardBindings.Label(action));
        KeyAction captured = action;

        Button button = UIKit.CreateButton("Key_" + action, row, string.Empty, UIKit.Safe(() => BeginKeyRebind(captured)), BindingButtonHeight);
        UIKit.Layout(button, BindingButtonHeight, BindingButtonWidth);

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        label.fontSize = 22f;
        keyLabels[action] = label;
        pageItems[KeyboardTab].Add(button);
    }

    private void BeginKeyRebind(KeyAction action)
    {
        if (IsRebinding)
            return;

        BlockInput(true);

        if (!KeyboardBindings.StartRebind(action, success => HandleRebindFinished()))
        {
            BlockInput(false);
            return;
        }

        rebindingKey = action;
        keyLabels[action].text = "<color=" + UIKit.AccentHex + ">Tekan tombol...</color>";
        keyStatus.text = action == KeyAction.Pause
            ? "Tekan tombol keyboard atau mouse untuk <b>Jeda</b>. Batal sendiri setelah 6 detik."
            : "Tekan tombol keyboard atau mouse untuk <b>" + KeyboardBindings.Label(action) + "</b>.  <b>Esc</b> batal.";
    }

    // ---------- Kontroler ----------

    private void BuildController(RectTransform page)
    {
        CreateHeading(page, "Kontroler");
        padStatus = CreateNote(page, "PadStatus", string.Empty);

        // Getaran aktif/mati.
        RectTransform toggleRow = CreateRow(page, "Getaran");
        vibrationToggle = UIKit.CreateButton("VibrationToggle", toggleRow, "AKTIF", UIKit.Safe(ToggleVibration), BindingButtonHeight);
        UIKit.Layout(vibrationToggle, BindingButtonHeight, 200f);
        vibrationLabel = vibrationToggle.GetComponentInChildren<TextMeshProUGUI>();
        vibrationLabel.fontSize = 22f;

        vibrationSlider = CreateSliderRow(page, "Kekuatan", out vibrationValue);
        vibrationSlider.onValueChanged.AddListener(value =>
        {
            GamepadRumble.Intensity = value;
            vibrationValue.text = Percent(value);
            GamepadRumble.Preview();
        });

        CreateNote(page, "BindingHeading", "Tombol gamepad (pilih lalu tekan tombol baru)");

        foreach (PadAction action in ControllerBindings.All)
        {
            RectTransform row = CreateRow(page, ControllerBindings.Label(action));
            PadAction captured = action;

            Button button = UIKit.CreateButton("Bind_" + action, row, string.Empty, UIKit.Safe(() => BeginPadRebind(captured)), BindingButtonHeight);
            UIKit.Layout(button, BindingButtonHeight, BindingButtonWidth);

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.fontSize = 22f;
            // Cadangan teks berwarna butuh font biasa, karena font display tidak punya semua glyph.
            label.font = padStatus.font;
            padLabels[action] = label;

            // Ikon tombol (A/B/X/Y, LB, RT, Menu, ...) di tengah tombol.
            Image glyph = UIKit.CreateImage("Glyph", button.transform, Color.white, false);
            glyph.preserveAspect = true;
            UIKit.Place(glyph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(GlyphHeight, GlyphHeight));
            glyph.enabled = false;
            padGlyphs[action] = glyph;

            padButtons.Add(button);
        }
    }

    private void ToggleVibration()
    {
        GamepadRumble.Enabled = !GamepadRumble.Enabled;
        RefreshController();

        if (GamepadRumble.Enabled)
            GamepadRumble.Preview();
    }

    private void BeginPadRebind(PadAction action)
    {
        if (IsRebinding)
            return;

        if (Gamepad.all.Count == 0)
        {
            padStatus.text = "<color=" + UIKit.TradeOffHex + ">Hubungkan gamepad dulu untuk mengubah tombol.</color>";
            statusHoldUntil = Time.unscaledTime + 2.5f;
            return;
        }

        // Navigasi UI dimatikan selama menunggu tombol, supaya tombol A tidak ikut menekan tombol menu.
        BlockInput(true);

        if (!ControllerBindings.StartRebind(action, success => HandleRebindFinished()))
        {
            BlockInput(false);
            return;
        }

        rebindingPad = action;
        padGlyphs[action].enabled = false;
        padLabels[action].text = "<color=" + UIKit.AccentHex + ">Tekan tombol...</color>";
        padStatus.text = "Tekan tombol gamepad untuk <b>" + ControllerBindings.Label(action) + "</b>.  <b>Esc</b> batal.";
    }

    // ---------- Baris ----------

    private static RectTransform CreateRow(Transform parent, string title)
    {
        RectTransform row = UIKit.CreateRect(title, parent);
        UIKit.AddHorizontal(row.gameObject, 20f, null, TextAnchor.MiddleLeft);
        UIKit.Layout(row, RowHeight);

        TextMeshProUGUI label = UIKit.CreateText("Label", row, title, 24f, UIKit.Text);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(label, -1f, 230f, 0f);

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
        UIKit.Layout(label, -1f, 230f, 0f);

        Slider slider = UIKit.CreateSlider("Slider", row, RowHeight);

        value = UIKit.UseDisplayFont(UIKit.CreateText("Value", row, "100%", 24f, UIKit.Text, TextAlignmentOptions.Right, true));
        value.textWrappingMode = TextWrappingModes.NoWrap;
        UIKit.Layout(value, -1f, 84f, 0f);

        return slider;
    }

    // ---------- Rebind ----------

    private void HandleRebindFinished()
    {
        rebindingKey = null;
        rebindingPad = null;
        RefreshKeyboard();
        RefreshController();

        if (restoreNavigation != null)
            StopCoroutine(restoreNavigation);

        if (isActiveAndEnabled)
            restoreNavigation = StartCoroutine(RestoreInputWhenReleased());
        else
            BlockInput(false);
    }

    private void CancelRebind()
    {
        if (KeyboardBindings.IsRebinding)
            KeyboardBindings.CancelRebind();

        if (ControllerBindings.IsRebinding)
            ControllerBindings.CancelRebind();
    }

    // Input menu baru dinyalakan lagi setelah semua tombol dilepas, supaya tombol yang baru dipasang
    // (mis. A atau Enter) tidak langsung memicu Submit pada tombol yang sedang dipilih.
    private IEnumerator RestoreInputWhenReleased()
    {
        yield return null;

        while (AnyButtonHeld())
            yield return null;

        BlockInput(false);
        restoreNavigation = null;
    }

    private static bool AnyButtonHeld()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.isPressed)
            return true;

        Mouse mouse = Mouse.current;

        if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed))
            return true;

        foreach (Gamepad pad in Gamepad.all)
        {
            if (pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed || pad.buttonNorth.isPressed ||
                pad.leftShoulder.isPressed || pad.rightShoulder.isPressed || pad.leftTrigger.isPressed || pad.rightTrigger.isPressed ||
                pad.startButton.isPressed || pad.selectButton.isPressed)
                return true;
        }

        return false;
    }

    private void BlockInput(bool block)
    {
        if (EventSystem.current != null)
            EventSystem.current.sendNavigationEvents = !block;

        if (rebindBlocker != null)
        {
            rebindBlocker.gameObject.SetActive(block);
            rebindBlocker.transform.SetAsLastSibling();
        }
    }

    // ---------- Show / hide ----------

    protected override void OnShow()
    {
        ShowTab(activeTab);
        firstSelected = tabs[activeTab];
        Refresh();
    }

    // Nilai disimpan ke disk saat modal ditutup, bukan setiap slider bergeser.
    protected override void OnHide()
    {
        CancelRebind();

        if (restoreNavigation != null)
        {
            StopCoroutine(restoreNavigation);
            restoreNavigation = null;
        }

        BlockInput(false);
        GameAudioSettings.Save();
        PlayerPrefs.Save();
    }

    protected override void Update()
    {
        base.Update();

        if (IsVisible && !IsRebinding && activeTab == ControllerTab)
            RefreshPadStatus();
    }

    private void ResetActiveTab()
    {
        switch (activeTab)
        {
            case SoundTab:
                GameAudioSettings.ResetToDefault();
                break;
            case KeyboardTab:
                KeyboardBindings.ResetToDefault();
                break;
            case ControllerTab:
                GamepadRumble.ResetToDefault();
                ControllerBindings.ResetToDefault();
                break;
        }

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

        RefreshKeyboard();
        RefreshController();
    }

    private void RefreshKeyboard()
    {
        foreach (KeyValuePair<KeyAction, TextMeshProUGUI> pair in keyLabels)
        {
            if (rebindingKey.HasValue && rebindingKey.Value == pair.Key)
                continue;

            pair.Value.text = KeyboardBindings.Name(pair.Key);
        }

        keyStatus.text = "Pilih aksi, lalu tekan tombol keyboard atau mouse yang baru. <b>Esc</b> membatalkan.";
    }

    private void RefreshController()
    {
        bool vibration = GamepadRumble.Enabled;
        vibrationLabel.text = vibration ? "AKTIF" : "MATI";
        vibrationSlider.SetValueWithoutNotify(GamepadRumble.Intensity);
        vibrationSlider.interactable = vibration;
        vibrationValue.text = Percent(GamepadRumble.Intensity);

        foreach (KeyValuePair<PadAction, TextMeshProUGUI> pair in padLabels)
        {
            if (rebindingPad.HasValue && rebindingPad.Value == pair.Key)
                continue;

            string control = ControllerBindings.ControlName(ControllerBindings.GetPath(pair.Key));
            Sprite sprite = ControlGlyphs.Get(ControlGlyphs.ForControl(control));
            Image glyph = padGlyphs[pair.Key];

            if (sprite != null)
            {
                glyph.sprite = sprite;
                glyph.rectTransform.sizeDelta = new Vector2(GlyphHeight * sprite.rect.width / sprite.rect.height, GlyphHeight);
                glyph.enabled = true;
                pair.Value.text = string.Empty;
            }
            else
            {
                glyph.enabled = false;
                pair.Value.text = ControlHints.PadName(control);
            }
        }

        pageItems[ControllerTab].Clear();
        pageItems[ControllerTab].Add(vibrationToggle);

        // Slider Kekuatan dilewati saat getaran mati.
        if (vibrationSlider.interactable)
            pageItems[ControllerTab].Add(vibrationSlider);

        pageItems[ControllerTab].AddRange(padButtons);

        BuildNavigation();
        RefreshPadStatus();
    }

    // Navigasi eksplisit:
    //   kolom tab   atas/bawah pindah tab, kanan masuk ke item pertama tab aktif
    //   isi tab     atas/bawah pindah item, kiri kembali ke tab aktif, item terakhir turun ke tombol bawah
    private void BuildNavigation()
    {
        if (resetButton == null || closeButton == null)
            return;

        List<Selectable> items = pageItems[activeTab];
        Selectable first = items.Count > 0 ? items[0] : resetButton;
        Selectable last = items.Count > 0 ? items[items.Count - 1] : tabs[tabs.Length - 1];

        for (int i = 0; i < tabs.Length; i++)
        {
            SetNavigation(tabs[i],
                i > 0 ? tabs[i - 1] : null,
                i < tabs.Length - 1 ? tabs[i + 1] : resetButton,
                null,
                first);
        }

        for (int i = 0; i < items.Count; i++)
        {
            SetNavigation(items[i],
                i > 0 ? items[i - 1] : null,
                i < items.Count - 1 ? items[i + 1] : closeButton,
                tabs[activeTab],
                null);
        }

        SetNavigation(resetButton, last, null, tabs[activeTab], closeButton);
        SetNavigation(closeButton, last, null, resetButton, null);
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
        if (padStatus == null || Time.unscaledTime < statusHoldUntil)
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
