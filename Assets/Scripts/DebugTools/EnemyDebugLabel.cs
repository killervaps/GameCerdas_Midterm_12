using System.Text;
using TMPro;
using UnityEngine;

// Label in-game di atas kepala musuh untuk AI Debug Mode: role, brain, aksi, HP, status pandangan, dan memori.
// Teks TextMeshPro dibuat sendiri saat Awake, jadi tidak perlu menyiapkan Canvas di prefab.
[RequireComponent(typeof(EnemyController))]
public class EnemyDebugLabel : MonoBehaviour
{
    [SerializeField] private float heightAboveHead = 0.4f;
    [SerializeField] private float fontSize = 2.5f;
    [SerializeField] private float refreshInterval = 0.2f;

    private readonly StringBuilder builder = new StringBuilder();

    private EnemyController controller;
    private TextMeshPro label;
    private Transform cameraTransform;
    private float nextRefresh;

    private void Awake()
    {
        controller = GetComponent<EnemyController>();

        GameObject go = new GameObject("DebugLabel");
        go.transform.SetParent(transform, false);

        label = go.AddComponent<TextMeshPro>();
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.richText = true;
        label.rectTransform.sizeDelta = new Vector2(4f, 2f);

        if (Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void LateUpdate()
    {
        // Controller nonaktif (mis. Role Data kosong) belum punya referensi komponen.
        bool show = AIDebugSettings.ShowLabels && controller.enabled && controller.Perception != null;

        if (label.gameObject.activeSelf != show)
            label.gameObject.SetActive(show);

        if (!show)
            return;

        // Posisi dihitung di ruang dunia supaya tidak ikut skala dan rotasi model.
        float top = controller.Perception.EyePosition.y + heightAboveHead;
        label.transform.position = new Vector3(transform.position.x, top, transform.position.z);

        if (cameraTransform != null)
            label.transform.rotation = cameraTransform.rotation;

        if (Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + refreshInterval;
        label.text = BuildText();
    }

    private string BuildText()
    {
        EnemyBlackboard bb = controller.Blackboard;
        EnemyBrain brain = controller.Brain;

        builder.Clear();

        builder.Append("<color=").Append(RoleColors.Hex(controller.Role)).Append("><b>")
            .Append(controller.Role).Append("</b></color>");

        if (brain != null)
            builder.Append(" <size=70%>[").Append(brain.BrainName).Append("]</size>");

        builder.Append('\n');

        if (brain != null)
        {
            builder.Append("<b>").Append(brain.CurrentAction).Append("</b>");

            string detail = brain.DebugDetail;

            if (!string.IsNullOrEmpty(detail))
                builder.Append(" <size=70%>").Append(detail).Append("</size>");

            builder.Append('\n');
        }

        builder.Append("<size=70%>HP ").Append(controller.Health.CurrentHealth.ToString("0"))
            .Append('/').Append(controller.Health.MaxHealth.ToString("0")).Append("  ");

        if (bb.CanSeePlayer)
            builder.Append("<color=#ff5555>LIHAT</color>");
        else if (bb.HasLastKnownPosition)
            builder.Append("<color=#ffaa33>INGAT ").Append(bb.MemoryRemaining.ToString("0.0")).Append("s</color>");
        else
            builder.Append("<color=#888888>tidak tahu</color>");

        builder.Append("</size>");

        return builder.ToString();
    }
}
