using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Meneruskan event "dipilih" (fokus keyboard/gamepad atau klik) sebuah Selectable ke sebuah aksi.
// Dipakai tab vertikal Pengaturan: tab langsung berganti saat difokuskan, tanpa tombol tambahan.
[DisallowMultipleComponent]
public class UISelectRelay : MonoBehaviour, ISelectHandler
{
    private Action onSelect;

    public static UISelectRelay Attach(GameObject target, Action onSelect)
    {
        UISelectRelay relay = target.GetComponent<UISelectRelay>();

        if (relay == null)
            relay = target.AddComponent<UISelectRelay>();

        relay.onSelect = onSelect;
        return relay;
    }

    public void OnSelect(BaseEventData eventData)
    {
        onSelect?.Invoke();
    }
}
