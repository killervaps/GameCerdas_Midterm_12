using System.Text;
using UnityEngine;

// Readout OnGUI sementara untuk combat MC: HP, Defense, target, lock, cooldown burst, dan pool.
// Teks diperbarui beberapa kali per detik, bukan setiap frame, supaya tidak menghasilkan string baru tiap frame.
public class CombatDebugPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerMotor playerMotor;
    [SerializeField] private TargetSelector targetSelector;
    [SerializeField] private AuraCaster auraCaster;
    [SerializeField] private AuraBurst auraBurst;
    [SerializeField] private ProjectilePool projectilePool;

    [Header("Layout")]
    [SerializeField] private bool showPanel = true;
    // Aktif = panel hanya tampil saat AI Debug Mode menyala (F1).
    [SerializeField] private bool followDebugToggle = true;
    [SerializeField] private Vector2 position = new Vector2(10f, 420f);
    [SerializeField] private float uiScale = 1f;
    [SerializeField] private float refreshInterval = 0.1f;

    private readonly StringBuilder builder = new StringBuilder();

    private string text = string.Empty;
    private float nextRefresh;
    private GUIStyle style;

    private bool Visible => showPanel && (!followDebugToggle || AIDebugSettings.Enabled);

    private void Update()
    {
        if (!Visible || Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + refreshInterval;
        text = BuildText();
    }

    private string BuildText()
    {
        builder.Clear();
        builder.Append("<b>Combat MC</b>\n");

        if (playerHealth != null)
        {
            builder.Append("HP ").Append(playerHealth.CurrentHealth.ToString("0.#"))
                .Append('/').Append(playerHealth.MaxHealth.ToString("0"))
                .Append("   Def ").Append(playerHealth.Defense.ToString("0.#")).Append('\n');
        }

        if (auraCaster != null)
        {
            builder.Append("Bola Aura  dmg ").Append(auraCaster.Damage.ToString("0.#"))
                .Append("  rate ").Append(auraCaster.AttackRate.ToString("0.##")).Append("/s\n");

            builder.Append("Mode ").Append(auraCaster.FireMode)
                .Append(auraCaster.IsFiring ? "  <color=#ffaa33>MENEMBAK</color>" : "  diam").Append('\n');
        }

        if (playerMotor != null)
        {
            builder.Append("Speed ").Append(playerMotor.EffectiveMoveSpeed.ToString("0.##"))
                .Append(" / ").Append(playerMotor.MoveSpeed.ToString("0.##")).Append('\n');
        }

        if (targetSelector != null)
        {
            builder.Append("Range ").Append(targetSelector.Range.ToString("0.#"))
                .Append("  kandidat ").Append(targetSelector.Candidates.Count).Append('\n');

            EnemyHealth target = targetSelector.CurrentTarget;

            if (target != null)
            {
                builder.Append(targetSelector.IsLocked ? "<color=#ff66ff>LOCK</color> " : "<color=#66ff66>AUTO</color> ")
                    .Append(target.name).Append("  HP ").Append(target.CurrentHealth.ToString("0.#"))
                    .Append('/').Append(target.MaxHealth.ToString("0")).Append('\n');
            }
            else
            {
                builder.Append("Target  -\n");
            }
        }

        if (auraBurst != null)
        {
            builder.Append("Aura Burst  ");

            if (auraBurst.IsReady)
                builder.Append("<color=#ffee55>SIAP</color>");
            else
                builder.Append(auraBurst.CooldownRemaining.ToString("0.0")).Append(" s");

            builder.Append("  dmg ").Append(auraBurst.Damage.ToString("0.#")).Append('\n');
        }

        if (projectilePool != null)
        {
            builder.Append("Pool aktif ").Append(projectilePool.CountActive)
                .Append("  idle ").Append(projectilePool.CountInactive).Append('\n');
        }

        return builder.ToString();
    }

    private void OnGUI()
    {
        if (!Visible)
            return;

        if (style == null)
            style = new GUIStyle(GUI.skin.box) { richText = true, alignment = TextAnchor.UpperLeft, wordWrap = true };

        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
        GUI.Box(new Rect(position.x, position.y, 260f, 170f), text, style);
    }
}
