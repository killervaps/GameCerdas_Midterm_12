using TMPro;
using UnityEngine;

// Ikon role di atas kepala musuh saat bertarung, supaya pemain bisa membaca komposisi musuh.
// Disembunyikan saat label AI Debug tampil, karena label debug sudah memuat role.
[RequireComponent(typeof(EnemyController))]
public class EnemyRoleBadge : MonoBehaviour
{
    [SerializeField] private float heightAboveHead = 0.25f;
    [SerializeField] private float fontSize = 3f;

    private EnemyController controller;
    private TextMeshPro badge;
    private Transform cameraTransform;

    private void Awake()
    {
        controller = GetComponent<EnemyController>();

        GameObject go = new GameObject("RoleBadge");
        go.transform.SetParent(transform, false);

        badge = go.AddComponent<TextMeshPro>();
        badge.fontSize = fontSize;
        badge.alignment = TextAlignmentOptions.Center;
        badge.richText = true;
        badge.rectTransform.sizeDelta = new Vector2(1f, 1f);

        if (Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Start()
    {
        badge.text = "<b>" + EnemyPreviewBoard.RoleBadgeLetter(controller.Role) + "</b>";
        badge.color = RoleColors.Get(controller.Role);
    }

    private void LateUpdate()
    {
        bool show = !AIDebugSettings.ShowLabels && controller.enabled && !controller.Health.IsDead;

        if (badge.gameObject.activeSelf != show)
            badge.gameObject.SetActive(show);

        if (!show)
            return;

        Vector3 position = transform.position;
        position.y = controller.Perception.EyePosition.y + heightAboveHead;
        badge.transform.position = position;

        if (cameraTransform != null)
            badge.transform.rotation = cameraTransform.rotation;
    }
}
