using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

// Aksi yang tombol gamepad-nya bisa diubah dari menu Pengaturan.
public enum PadAction
{
    Attack,
    Interact,
    AuraBurst,
    CycleTarget,
    Pause,
    // Khusus menu.
    Select,
    Back
}

// Binding gamepad yang bisa diubah pemain (rebind), disimpan di PlayerPrefs.
// Empat aksi pertama memakai binding grup "Gamepad" di Project-wide Actions (InputSystem_Actions).
// Jeda, Pilih, dan Kembali memakai action sendiri yang selalu aktif, karena map Player dimatikan selama menu terbuka.
// Default: Attack = X, Interact (tahan) = RT, Aura Burst = Y, Ganti target = RB, Jeda = Menu, Pilih = A, Kembali = B.
// Pilih dipasang sebagai Submit di InputSystemUIInputModule. Kembali hanya berlaku di menu (diurus GameUI).
public static class ControllerBindings
{
    private const string PrefsPrefix = "MathGate.Pad.Binding.";
    private const string DefaultPausePath = "<Gamepad>/start";
    private const string DefaultSelectPath = "<Gamepad>/buttonSouth";
    private const string DefaultBackPath = "<Gamepad>/buttonEast";

    public static readonly PadAction[] All =
    {
        PadAction.Attack, PadAction.Interact, PadAction.AuraBurst, PadAction.CycleTarget, PadAction.Pause,
        PadAction.Select, PadAction.Back
    };

    private static bool loaded;
    private static InputAction pauseAction;
    private static InputAction selectAction;
    private static InputAction backAction;
    // Asset runtime untuk Jeda/Pilih/Kembali. InputActionReference (untuk Submit modul UI) hanya bisa dibuat
    // dari action yang berada di dalam sebuah InputActionAsset.
    private static InputActionAsset menuAsset;
    private static InputActionReference selectReference;
    private static InputActionRebindingExtensions.RebindingOperation operation;
    private static int lastRebindFrame = -10;

    // Dikirim setiap kali binding berubah (rebind, atur ulang, atau dimuat dari PlayerPrefs).
    public static event Action Changed;

    public static bool IsRebinding => operation != null;

    // True selama rebind dan satu frame sesudahnya, supaya tombol yang dipakai rebind (mis. Esc untuk batal,
    // atau Menu yang baru dipasang ke Jeda) tidak ikut menutup menu.
    public static bool BlocksMenuInput => IsRebinding || Time.frameCount - lastRebindFrame <= 1;

    public static InputAction PauseAction
    {
        get
        {
            EnsureLoaded();
            return pauseAction;
        }
    }

    // Kembali (B): menutup menu teratas atau melanjutkan game dari menu jeda. Tidak berlaku saat bermain.
    public static InputAction BackAction
    {
        get
        {
            EnsureLoaded();
            return backAction;
        }
    }

    // Aksi gameplay dan aksi menu boleh memakai tombol yang sama (A bisa jadi Pilih dan Serangan sekaligus),
    // jadi pertukaran tombol saat rebind hanya terjadi di dalam konteks yang sama. Jeda dipakai di keduanya.
    private static bool IsGameplay(PadAction action)
    {
        return action != PadAction.Select && action != PadAction.Back;
    }

    private static bool IsMenu(PadAction action)
    {
        return action == PadAction.Select || action == PadAction.Back || action == PadAction.Pause;
    }

    private static bool SharesContext(PadAction a, PadAction b)
    {
        return (IsGameplay(a) && IsGameplay(b)) || (IsMenu(a) && IsMenu(b));
    }

    // Memasang action Pilih sebagai Submit modul UI, supaya A (atau tombol hasil rebind) memilih tombol menu.
    // Dicek tiap frame karena EventSystem bisa dibuat ulang saat scene berganti.
    public static void ApplyToUIModule()
    {
        EnsureLoaded();

        if (EventSystem.current == null ||
            !EventSystem.current.TryGetComponent(out InputSystemUIInputModule module))
            return;

        if (selectReference == null)
            selectReference = InputActionReference.Create(selectAction);

        if (module.submit != selectReference)
            module.submit = selectReference;

        if (!selectAction.enabled && !IsRebinding)
            selectAction.Enable();
    }

    public static string Label(PadAction action)
    {
        switch (action)
        {
            case PadAction.Attack: return "Serangan";
            case PadAction.Interact: return "Interaksi (tahan)";
            case PadAction.AuraBurst: return "Aura Burst";
            case PadAction.CycleTarget: return "Ganti target";
            case PadAction.Pause: return "Jeda";
            case PadAction.Select: return "Pilih (menu)";
            case PadAction.Back: return "Kembali (menu)";
            default: return action.ToString();
        }
    }

    private static string ActionPath(PadAction action)
    {
        switch (action)
        {
            case PadAction.Attack: return "Player/Attack";
            case PadAction.Interact: return "Player/Interact";
            case PadAction.AuraBurst: return "Player/AuraBurst";
            case PadAction.CycleTarget: return "Player/CycleTarget";
            default: return null;
        }
    }

    public static void EnsureLoaded()
    {
        if (loaded)
            return;

        loaded = true;

        menuAsset = ScriptableObject.CreateInstance<InputActionAsset>();
        menuAsset.name = "MathGate Menu Actions";
        InputActionMap menuMap = menuAsset.AddActionMap("Menu");

        pauseAction = menuMap.AddAction("Pause", InputActionType.Button);
        pauseAction.AddBinding(DefaultPausePath).WithGroup("Gamepad");

        // Enter tetap memilih di keyboard. Binding gamepad yang bisa di-rebind.
        selectAction = menuMap.AddAction("Select", InputActionType.Button);
        selectAction.AddBinding("<Keyboard>/enter").WithGroup("Keyboard&Mouse");
        selectAction.AddBinding("<Keyboard>/numpadEnter").WithGroup("Keyboard&Mouse");
        selectAction.AddBinding(DefaultSelectPath).WithGroup("Gamepad");

        backAction = menuMap.AddAction("Back", InputActionType.Button);
        backAction.AddBinding(DefaultBackPath).WithGroup("Gamepad");

        foreach (PadAction action in All)
        {
            string saved = PlayerPrefs.GetString(PrefsPrefix + action, string.Empty);

            if (!string.IsNullOrEmpty(saved) && TryGet(action, out InputAction inputAction, out int index))
                inputAction.ApplyBindingOverride(index, saved);
        }

        pauseAction.Enable();
        selectAction.Enable();
        backAction.Enable();
        Changed?.Invoke();
    }

    // Action dan indeks binding gamepad-nya.
    public static bool TryGet(PadAction action, out InputAction inputAction, out int bindingIndex)
    {
        bindingIndex = -1;

        if (action == PadAction.Pause)
        {
            inputAction = pauseAction;
        }
        else if (action == PadAction.Select)
        {
            inputAction = selectAction;
        }
        else if (action == PadAction.Back)
        {
            inputAction = backAction;
        }
        else
        {
            inputAction = InputSystem.actions != null ? InputSystem.actions.FindAction(ActionPath(action)) : null;
        }

        if (inputAction == null)
            return false;

        var bindings = inputAction.bindings;

        for (int i = 0; i < bindings.Count; i++)
        {
            InputBinding binding = bindings[i];

            if (binding.isComposite || binding.isPartOfComposite)
                continue;

            if (IsGamepadPath(binding.path) || (binding.groups != null && binding.groups.Contains("Gamepad")))
            {
                bindingIndex = i;
                return true;
            }
        }

        return false;
    }

    private static bool IsGamepadPath(string path)
    {
        return !string.IsNullOrEmpty(path) && path.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase);
    }

    // Path efektif (dengan override) binding gamepad aksi ini, mis. "<Gamepad>/buttonNorth".
    public static string GetPath(PadAction action)
    {
        EnsureLoaded();
        return TryGet(action, out InputAction inputAction, out int index) ? inputAction.bindings[index].effectivePath : string.Empty;
    }

    // Nama kontrol tanpa layout, mis. "<XInputController>/buttonNorth" -> "buttonNorth", "<Gamepad>/dpad/up" -> "dpad/up".
    public static string ControlName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        int slash = path.IndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    // Menunggu pemain menekan tombol gamepad untuk aksi ini. Esc membatalkan, dan batal sendiri setelah 6 detik.
    // Jika tombol baru sudah dipakai aksi lain, kedua aksi bertukar tombol.
    public static bool StartRebind(PadAction action, Action<bool> finished)
    {
        EnsureLoaded();

        if (IsRebinding || !TryGet(action, out InputAction inputAction, out int index))
            return false;

        string oldPath = inputAction.bindings[index].effectivePath;
        bool wasEnabled = inputAction.enabled;

        // Rebind hanya boleh pada action yang tidak aktif.
        inputAction.Disable();

        operation = inputAction.PerformInteractiveRebinding(index)
            .WithControlsHavingToMatchPath("<Gamepad>")
            .WithControlsExcluding("<Gamepad>/leftStick")
            .WithControlsExcluding("<Gamepad>/rightStick")
            .WithControlsExcluding("<Gamepad>/leftStick/*")
            .WithControlsExcluding("<Gamepad>/rightStick/*")
            .WithControlsExcluding("<Gamepad>/dpad")
            .WithCancelingThrough("<Keyboard>/escape")
            .WithTimeout(6f)
            .OnMatchWaitForAnother(0.1f)
            .OnComplete(op => Finish(action, inputAction, oldPath, wasEnabled, true, finished))
            .OnCancel(op => Finish(action, inputAction, oldPath, wasEnabled, false, finished));

        operation.Start();
        return true;
    }

    public static void CancelRebind()
    {
        operation?.Cancel();
    }

    private static void Finish(PadAction action, InputAction inputAction, string oldPath, bool wasEnabled, bool success, Action<bool> finished)
    {
        operation?.Dispose();
        operation = null;
        lastRebindFrame = Time.frameCount;

        if (success)
        {
            string newName = ControlName(GetPath(action));

            // Tukar dengan aksi lain yang sudah memakai tombol yang sama.
            foreach (PadAction other in All)
            {
                if (other != action && SharesContext(action, other) && ControlName(GetPath(other)) == newName)
                    SetPath(other, oldPath);
            }

            Save();
        }

        if (wasEnabled)
            inputAction.Enable();

        Changed?.Invoke();
        finished?.Invoke(success);
    }

    private static void SetPath(PadAction action, string path)
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

        foreach (PadAction action in All)
        {
            if (TryGet(action, out InputAction inputAction, out int index))
                inputAction.RemoveBindingOverride(index);
        }

        Save();
        Changed?.Invoke();
    }

    public static void Save()
    {
        foreach (PadAction action in All)
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
        if (menuAsset != null)
            menuAsset.Disable();

        menuAsset = null;
        pauseAction = null;
        selectAction = null;
        backAction = null;
        selectReference = null;
        loaded = false;
    }
}
