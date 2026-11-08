using System;
using TMPro;
using UnityEngine;

// Menulis ulang teks TMP saat skema kontrol atau binding berubah. Teksnya dibangun oleh fungsi,
// mis. () => "Aura Burst " + ControlHints.Glyph(PadAction.AuraBurst).
[DisallowMultipleComponent]
public class ControlHintLabel : MonoBehaviour
{
    private TMP_Text label;
    private Func<string> builder;
    private int version = -1;

    public static ControlHintLabel Bind(TMP_Text label, Func<string> builder)
    {
        if (label == null)
            return null;

        ControlHintLabel hint = label.GetComponent<ControlHintLabel>();

        if (hint == null)
            hint = label.gameObject.AddComponent<ControlHintLabel>();

        hint.label = label;
        hint.builder = builder;
        hint.Refresh();
        return hint;
    }

    public void Refresh()
    {
        if (label == null || builder == null)
            return;

        version = ControlHints.Version;
        // Ikon tombol gamepad ditulis sebagai <sprite name=...>, jadi label butuh sprite asset ikon.
        ControlGlyphs.Apply(label);
        label.text = builder();
    }

    private void OnEnable()
    {
        if (builder != null && version != ControlHints.Version)
            Refresh();
    }

    private void LateUpdate()
    {
        if (builder != null && version != ControlHints.Version)
            Refresh();
    }
}
