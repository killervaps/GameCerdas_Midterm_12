using UnityEngine;
using UnityEngine.UI;

// Suara klik saat tombol ditekan (mouse, keyboard, atau gamepad), lewat slot UI Click di SfxManager.
// Didengarkan dari Button.onClick, bukan dari event pointer. Banyak tombol langsung menyembunyikan layarnya
// sendiri saat diklik, sehingga pengecekan interactable setelahnya selalu gagal dan suaranya hilang.
// Dipasang otomatis oleh UIKit.CreateButton.
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class UIButtonFeedback : MonoBehaviour
{
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(Play);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(Play);
    }

    private static void Play()
    {
        SfxManager.Play(SfxCue.UiClick);
    }
}
