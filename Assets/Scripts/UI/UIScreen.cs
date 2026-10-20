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
    }
}
