using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerHealth health;
    [SerializeField] private Slider healthBar;
    [SerializeField] private TMP_Text hpText;

    private void Update()
    {
        if (health == null)
            return;

        if (healthBar != null)
            healthBar.value = health.CurrentHealth / health.MaxHealth;

        if (hpText != null)
            hpText.text = "HP " + health.CurrentHealth.ToString("0") +
                          "/" + health.MaxHealth.ToString("0");
    }
}
