using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Dasar semua layar/panel UI. Tampil dan sembunyi lewat CanvasGroup dengan fade
// berbasis unscaled time supaya tetap jalan saat game dijeda (Time.timeScale = 0).
[RequireComponent(typeof(CanvasGroup))]
public abstract class UIScreen : MonoBehaviour
{
    [SerializeField] private float fadeSpeed = 9f;

    protected GameUI ui;
    protected CanvasGroup group;
    protected RectTransform rect;

    // Elemen yang dipilih saat layar dibuka, untuk navigasi keyboard/gamepad.
    protected Selectable firstSelected;

    private bool interactive = true;

    // Tombol terakhir yang dipilih di layar ini, untuk mengembalikan fokus setelah seleksi lepas.
    private GameObject lastSelected;

    public bool IsVisible { get; private set; }

    public void Initialize(GameUI owner, bool interactiveScreen)
    {
        ui = owner;
        interactive = interactiveScreen;
        rect = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();

        Build();

        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        IsVisible = false;
        gameObject.SetActive(false);
    }

    protected abstract void Build();

    public void Show()
    {
        if (IsVisible)
            return;

        IsVisible = true;
        gameObject.SetActive(true);
        group.interactable = interactive;
        group.blocksRaycasts = interactive;

        OnShow();
        FocusFirst();
    }

    // Memilih tombol pertama supaya layar bisa dioperasikan dengan keyboard/gamepad.
    public void FocusFirst()
    {
        if (interactive && IsVisible && firstSelected != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
    }

    public void Hide()
    {
        if (!IsVisible)
            return;

        IsVisible = false;
        group.interactable = false;
        group.blocksRaycasts = false;

        OnHide();
    }

    public void SetVisible(bool visible)
    {
        if (visible)
            Show();
        else
            Hide();
    }

    public void HideInstant()
    {
        Hide();
        group.alpha = 0f;
        gameObject.SetActive(false);
    }

    protected virtual void OnShow()
    {
    }

    protected virtual void OnHide()
    {
    }

    protected virtual void Update()
    {
        float target = IsVisible ? 1f : 0f;

        if (!Mathf.Approximately(group.alpha, target))
            group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);

        if (!IsVisible && group.alpha <= 0f)
            gameObject.SetActive(false);

        if (IsVisible)
            KeepFocus();
    }

    // Klik di area kosong (backdrop) membuat EventSystem melepas seleksi, sehingga panah dan Enter
    // berhenti bekerja. Hal yang sama terjadi jika seleksi tertinggal di tombol nonaktif atau di layar
    // yang sedang menutup. Seleksi dikembalikan ke tombol terakhir, hanya oleh layar yang paling atas.
    private void KeepFocus()
    {
        if (!interactive || EventSystem.current == null)
            return;

        GameObject current = EventSystem.current.currentSelectedGameObject;

        if (IsSelectable(current))
        {
            if (current.transform.IsChildOf(transform))
                lastSelected = current;

            return;
        }

        if (IsCoveredByLaterScreen())
            return;

        GameObject target = IsSelectable(lastSelected)
            ? lastSelected
            : firstSelected != null ? firstSelected.gameObject : null;

        if (target != null)
            EventSystem.current.SetSelectedGameObject(target);
    }

    private static bool IsSelectable(GameObject candidate)
    {
        return candidate != null &&
               candidate.activeInHierarchy &&
               candidate.TryGetComponent(out Selectable selectable) &&
               selectable.IsInteractable();
    }

    // Layar yang dibuat belakangan digambar di atas (mis. Tutorial di atas Jeda), jadi layar ini
    // tidak boleh merebut fokus jika ada layar interaktif sesudahnya yang sedang tampil.
    private bool IsCoveredByLaterScreen()
    {
        Transform parent = transform.parent;

        if (parent == null)
            return false;

        for (int i = transform.GetSiblingIndex() + 1; i < parent.childCount; i++)
        {
            if (parent.GetChild(i).TryGetComponent(out UIScreen other) && other.IsVisible && other.interactive)
                return true;
        }

        return false;
    }
}
