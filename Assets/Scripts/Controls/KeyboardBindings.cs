using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Aksi yang tombol keyboard/mouse-nya bisa diubah dari Pengaturan > Keyboard.
public enum KeyAction
{
    MoveUp,
    MoveDown,
    MoveLeft,
    MoveRight,
    Attack,
    Interact,
    AuraBurst,
    CycleTarget,
    Pause
}

// Binding keyboard/mouse yang bisa diubah pemain, disimpan di PlayerPrefs. Hanya binding utama yang diubah:
//   Gerak       bagian up/down/left/right komposit WASD (panah tetap jadi cadangan)
//   Aksi        binding keyboard/mouse pertama di Project-wide Actions (Serangan = Klik Kiri, Interaksi = E, dst.)
//   Jeda        binding keyboard action Jeda milik ControllerBindings (default Esc)
// Esc selalu tetap menutup menu (action Kembali), jadi menu tetap bisa ditutup walau Jeda dipindah.
public static class KeyboardBindings
{
    private const string PrefsPrefix = "MathGate.Key.Binding.";

    public static readonly KeyAction[] Movement = { KeyAction.MoveUp, KeyAction.MoveDown, KeyAction.MoveLeft, KeyAction.MoveRight };

    public static readonly KeyAction[] Actions =
    {
        KeyAction.Attack, KeyAction.Interact, KeyAction.AuraBurst, KeyAction.CycleTarget, KeyAction.Pause
    };

    public static readonly KeyAction[] All =
    {
        KeyAction.MoveUp, KeyAction.MoveDown, KeyAction.MoveLeft, KeyAction.MoveRight,
        KeyAction.Attack, KeyAction.Interact, KeyAction.AuraBurst, KeyAction.CycleTarget, KeyAction.Pause
    };

    private static bool loaded;
    private static InputActionRebindingExtensions.RebindingOperation operation;
    private static int lastRebindFrame = -10;

    public static event Action Changed;

    public static bool IsRebinding => operation != null;

    // True selama rebind dan satu frame sesudahnya, supaya Esc (batal) tidak ikut menutup menu.
    public static bool BlocksMenuInput => IsRebinding || Time.frameCount - lastRebindFrame <= 1;

    public static string Label(KeyAction action)
    {
        switch (action)
        {
            case KeyAction.MoveUp: return "Maju";
            case KeyAction.MoveDown: return "Mundur";
            case KeyAction.MoveLeft: return "Kiri";
            case KeyAction.MoveRight: return "Kanan";
            case KeyAction.Attack: return "Serangan";
            case KeyAction.Interact: return "Interaksi (tahan)";
            case KeyAction.AuraBurst: return "Aura Burst";
            case KeyAction.CycleTarget: return "Ganti target";
            case KeyAction.Pause: return "Jeda";
            default: return action.ToString();
        }
    }

    private static string PartName(KeyAction action)
    {
        switch (action)
        {
            case KeyAction.MoveUp: return "up";
            case KeyAction.MoveDown: return "down";
            case KeyAction.MoveLeft: return "left";
            case KeyAction.MoveRight: return "right";
            default: return null;
        }
    }

    private static string ActionPath(KeyAction action)
    {
        switch (action)
        {
            case KeyAction.MoveUp:
            case KeyAction.MoveDown:
            case KeyAction.MoveLeft:
            case KeyAction.MoveRight:
                return "Player/Move";
            case KeyAction.Attack: return "Player/Attack";
            case KeyAction.Interact: return "Player/Interact";
            case KeyAction.AuraBurst: return "Player/AuraBurst";
            case KeyAction.CycleTarget: return "Player/CycleTarget";
            default: return null;
        }
    }

    public static void EnsureLoaded()
    {
        if (loaded)
            return;

        // Action Jeda dibuat oleh ControllerBindings.
        ControllerBindings.EnsureLoaded();
        loaded = true;

        foreach (KeyAction action in All)
        {
            string saved = PlayerPrefs.GetString(PrefsPrefix + action, string.Empty);

            if (!string.IsNullOrEmpty(saved) && TryGet(action, out InputAction inputAction, out int index))
                inputAction.ApplyBindingOverride(index, saved);
        }

        Changed?.Invoke();
    }

    // Action dan indeks binding keyboard/mouse utamanya.
    public static bool TryGet(KeyAction action, out InputAction inputAction, out int bindingIndex)
    {
        bindingIndex = -1;

        if (action == KeyAction.Pause)
            inputAction = ControllerBindings.PauseAction;
        else
            inputAction = InputSystem.actions != null ? InputSystem.actions.FindAction(ActionPath(action)) : null;

        if (inputAction == null)
            return false;

        string part = PartName(action);
        var bindings = inputAction.bindings;

        for (int i = 0; i < bindings.Count; i++)
        {
            InputBinding binding = bindings[i];

            if (binding.isComposite || !IsKeyboardOrMouse(binding.path))
                continue;

            if (part != null)
            {
                if (binding.isPartOfComposite && string.Equals(binding.name, part, StringComparison.OrdinalIgnoreCase))
                {
                    bindingIndex = i;
                    return true;
                }
            }
            else if (!binding.isPartOfComposite)
            {
                bindingIndex = i;
                return true;
            }
        }

        return false;
    }

    private static bool IsKeyboardOrMouse(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               (path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMousePath(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               (path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("<Pointer>", StringComparison.OrdinalIgnoreCase));
    }

    // Path efektif (dengan override), mis. "<Keyboard>/space".
    public static string GetPath(KeyAction action)
    {
        EnsureLoaded();
        return TryGet(action, out InputAction inputAction, out int index) ? inputAction.bindings[index].effectivePath : string.Empty;
    }

    // Nama tombol untuk petunjuk, mis. "Space", "E", "Klik Kiri".
    public static string Name(KeyAction action)
    {
        return KeyName(GetPath(action));
    }

    public static string KeyName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "-";

        string control = ControllerBindings.ControlName(path);

        if (IsMousePath(path))
        {
            switch (control)
            {
                case "leftButton": return "Klik Kiri";
                case "rightButton": return "Klik Kanan";
                case "middleButton": return "Klik Tengah";
                case "backButton": return "Mouse 4";
                case "forwardButton": return "Mouse 5";
            }
        }
        else
        {
            switch (control)
            {
                case "escape": return "Esc";
                case "space": return "Space";
                case "tab": return "Tab";
                case "enter": return "Enter";
                case "numpadEnter": return "Num Enter";
                case "backspace": return "Backspace";
                case "capsLock": return "Caps Lock";
                case "leftShift": return "Shift Kiri";
                case "rightShift": return "Shift Kanan";
                case "leftCtrl": return "Ctrl Kiri";
                case "rightCtrl": return "Ctrl Kanan";
                case "leftAlt": return "Alt Kiri";
                case "rightAlt": return "Alt Kanan";
                case "upArrow": return "Panah Atas";
                case "downArrow": return "Panah Bawah";
                case "leftArrow": return "Panah Kiri";
                case "rightArrow": return "Panah Kanan";
            }
        }

        string readable = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);

        if (string.IsNullOrEmpty(readable))
            readable = control;

        return readable.Length == 1 ? readable.ToUpperInvariant() : readable;
    }

    // Kunci pembanding untuk mendeteksi dua aksi memakai tombol yang sama.
    private static string ControlKey(string path)
    {
        return (IsMousePath(path) ? "m:" : "k:") + ControllerBindings.ControlName(path).ToLowerInvariant();
    }

    // Menunggu pemain menekan tombol keyboard atau mouse untuk aksi ini. Esc membatalkan (kecuali untuk Jeda,
    // supaya Jeda bisa dikembalikan ke Esc), dan batal sendiri setelah 6 detik.
    // Jika tombol baru sudah dipakai aksi lain, kedua aksi bertukar tombol.
    public static bool StartRebind(KeyAction action, Action<bool> finished)
    {
        EnsureLoaded();

        if (IsRebinding || ControllerBindings.IsRebinding || !TryGet(action, out InputAction inputAction, out int index))
            return false;

        string oldPath = inputAction.bindings[index].effectivePath;
        bool wasEnabled = inputAction.enabled;

        inputAction.Disable();

        operation = inputAction.PerformInteractiveRebinding(index)
            .WithExpectedControlType("Button")
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsHavingToMatchPath("<Mouse>")
            .WithControlsExcluding("<Keyboard>/anyKey")
            .WithControlsExcluding("<Mouse>/press")
            .WithControlsExcluding("<Pointer>/press")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithTimeout(6f)
            .OnMatchWaitForAnother(0.05f)
            .OnComplete(op => Finish(action, inputAction, oldPath, wasEnabled, true, finished))
            .OnCancel(op => Finish(action, inputAction, oldPath, wasEnabled, false, finished));

        if (action != KeyAction.Pause)
            operation.WithCancelingThrough("<Keyboard>/escape");

        operation.Start();
        return true;
    }

    public static void CancelRebind()
    {
        operation?.Cancel();
    }

    private static void Finish(KeyAction action, InputAction inputAction, string oldPath, bool wasEnabled, bool success, Action<bool> finished)
    {
        operation?.Dispose();
        operation = null;
        lastRebindFrame = Time.frameCount;

        if (success)
        {
            string newKey = ControlKey(GetPath(action));

            foreach (KeyAction other in All)
            {
                if (other != action && ControlKey(GetPath(other)) == newKey)
                    SetPath(other, oldPath);
            }

            Save();
        }

        if (wasEnabled)
            inputAction.Enable();

        Changed?.Invoke();
        finished?.Invoke(success);
    }

    private static void SetPath(KeyAction action, string path)
    {
        if (!TryGet(action, out InputAction inputAction, out int index))
            return;

        if (inputAction.bindings[index].path == path)
            inputAction.RemoveBindingOverride(index);
        else
            inputAction.ApplyBindingOverride(index, path);
    }

    public static void ResetToDefault()
    {
        EnsureLoaded();
        CancelRebind();

        foreach (KeyAction action in All)
        {
            if (TryGet(action, out InputAction inputAction, out int index))
                inputAction.RemoveBindingOverride(index);
        }

        Save();
        Changed?.Invoke();
    }

    public static void Save()
    {
        foreach (KeyAction action in All)
        {
            string key = PrefsPrefix + action;

            if (TryGet(action, out InputAction inputAction, out int index) &&
                !string.IsNullOrEmpty(inputAction.bindings[index].overridePath))
                PlayerPrefs.SetString(key, inputAction.bindings[index].overridePath);
            else
                PlayerPrefs.DeleteKey(key);
        }

        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        operation?.Dispose();
        operation = null;
        lastRebindFrame = -10;
        loaded = false;
    }
}
