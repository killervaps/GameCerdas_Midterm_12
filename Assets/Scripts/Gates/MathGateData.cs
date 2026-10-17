using System.Collections.Generic;
using UnityEngine;

// Satu kartu gate. Wajib berisi minimal satu buff dan satu trade-off.
[CreateAssetMenu(fileName = "Gate_New", menuName = "MathGate/Math Gate")]
public class MathGateData : ScriptableObject
{
    [Header("Info")]
    [SerializeField] private string gateName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private GateRarity rarity = GateRarity.Common;
    // Kata kunci singkat di kartu, mis. "Tahan", "Lambat".
    [SerializeField] private string[] previewTags;

    [Header("Effects")]
    [SerializeField] private ModifierEffect[] effects;

    public string DisplayName => string.IsNullOrEmpty(gateName) ? name : gateName;

    public string Description => description;

    public GateRarity Rarity => rarity;

    public IReadOnlyList<string> PreviewTags => previewTags ?? System.Array.Empty<string>();

    public IReadOnlyList<ModifierEffect> Effects => effects ?? System.Array.Empty<ModifierEffect>();

    public bool HasBuff
    {
        get
        {
            foreach (ModifierEffect effect in Effects)
            {
                if (effect.IsBuff)
                    return true;
            }

            return false;
        }
    }

    public bool HasTradeOff
    {
        get
        {
            foreach (ModifierEffect effect in Effects)
            {
                if (effect.IsTradeOff)
                    return true;
            }

            return false;
        }
    }

    public bool IsValid => HasBuff && HasTradeOff;

    private void OnValidate()
    {
        if (effects == null || effects.Length == 0)
            return;

        foreach (ModifierEffect effect in effects)
        {
            if (!ModifierEffect.IsValidValue(effect.op, effect.value))
                Debug.LogWarning("Gate " + name + ": efek " + effect + " tidak valid (kali/bagi nol) dan akan diabaikan.", this);
        }

        if (!IsValid)
            Debug.LogWarning("Gate " + name + ": harus punya minimal satu buff dan satu trade-off.", this);
    }

#if UNITY_EDITOR
    // Dipakai menu Editor untuk membuat gate contoh.
    public void EditorSetup(
        string newName,
        string newDescription,
        GateRarity newRarity,
        string[] newTags,
        ModifierEffect[] newEffects)
    {
        gateName = newName;
        description = newDescription;
        rarity = newRarity;
        previewTags = newTags;
        effects = newEffects;
    }
#endif
}
