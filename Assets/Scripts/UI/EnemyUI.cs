using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EnemyUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyHealth health;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private Slider healthBar;

    private IEnemyBrain brain;
    private Camera cam;

    private void Awake()
    {
        cam = Camera.main;

        // Bisa EnemyBTController maupun EnemyUtilityController.
        brain = GetComponentInParent<IEnemyBrain>();

        if (health == null)
            health = GetComponentInParent<EnemyHealth>();
    }

    private void LateUpdate()
    {
        // Billboard: selalu menghadap kamera
        if (cam != null)
            transform.forward = cam.transform.forward;

        if (brain != null && stateText != null)
        {
            EnemyAction action = brain.CurrentAction;

            // Label otak membedakan enemy BT dan Utility saat tampil bersamaan.
            stateText.text = "[" + brain.BrainName + "] " + action;
            stateText.color = ActionColor(action);
        }

        if (health != null && healthBar != null)
            healthBar.value = health.CurrentHealth / health.MaxHealth;
    }

    private Color ActionColor(EnemyAction action)
    {
        switch (action)
        {
            case EnemyAction.Patrol: return Color.green;
            case EnemyAction.Chase: return Color.yellow;
            case EnemyAction.Attack: return Color.red;
            case EnemyAction.Flee: return Color.cyan;
            case EnemyAction.Search: return new Color(1f, 0.5f, 0f);
            default: return Color.gray;
        }
    }
}
