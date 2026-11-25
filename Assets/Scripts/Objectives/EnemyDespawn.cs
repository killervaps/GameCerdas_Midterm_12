using UnityEngine;
using UnityEngine.AI;

// Efek musuh yang dihilangkan tanpa dibunuh (objective selesai saat musuh masih hidup): AI, gerak, dan collider
// dimatikan, lalu model mengecil sampai hilang dan objeknya dinonaktifkan. EnemySpawner menghapusnya saat wave dibersihkan.
public class EnemyDespawn : MonoBehaviour
{
    private float duration;
    private float elapsed;
    private Vector3 startScale;

    public static void Begin(EnemyHealth enemy, float duration)
    {
        if (enemy == null || enemy.GetComponent<EnemyDespawn>() != null)
            return;

        EnemyDespawn despawn = enemy.gameObject.AddComponent<EnemyDespawn>();
        despawn.duration = duration;
        despawn.startScale = enemy.transform.localScale;

        EnemyController controller = enemy.GetComponent<EnemyController>();

        if (controller != null)
            controller.enabled = false;

        NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();

        if (agent != null && agent.enabled)
        {
            if (agent.isOnNavMesh)
                agent.isStopped = true;

            agent.enabled = false;
        }

        foreach (Collider collider in enemy.GetComponentsInChildren<Collider>())
            collider.enabled = false;

        if (duration <= 0f)
            enemy.gameObject.SetActive(false);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;

        // Sedikit membesar dulu, lalu mengecil cepat.
        float scale = t < 0.2f ? Mathf.Lerp(1f, 1.15f, t / 0.2f) : Mathf.Lerp(1.15f, 0f, (t - 0.2f) / 0.8f);
        transform.localScale = startScale * scale;

        if (t >= 1f)
            gameObject.SetActive(false);
    }
}
