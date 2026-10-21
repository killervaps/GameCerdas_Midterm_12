using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Menampilkan bingkai fokus saat tombol dipilih lewat EventSystem (keyboard, gamepad, atau klik).
// Hover mouse tidak memicu bingkai, jadi fokus dan hover terlihat berbeda.
// Dipasang otomatis oleh UIKit.CreateButton.
[DisallowMultipleComponent]
public class UIFocusFrame : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    [SerializeField] private Graphic frame;

    public void Bind(Graphic target)
    {
        frame = target;
        SetVisible(IsSelected());
    }

    public void OnSelect(BaseEventData eventData)
    {
        SetVisible(true);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        SetVisible(false);
    }

    // EventSystem tidak memanggil OnSelect lagi jika tombol yang sama dipilih ulang setelah layarnya
    // disembunyikan lalu dibuka, jadi status seleksi dicek ulang saat aktif.
    private void OnEnable()
    {
        SetVisible(IsSelected());
    }

    private void OnDisable()
    {
        SetVisible(false);
    }

    private bool IsSelected()
    {
        return EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
    }

    private void SetVisible(bool visible)
    {
        if (frame != null)
            frame.enabled = visible;
    }
}
