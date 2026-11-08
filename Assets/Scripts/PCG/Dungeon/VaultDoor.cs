using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Pintu vault selebar bukaan room dead-end. BoxCollider menahan MC dan proyektil, NavMeshObstacle (carve) menahan musuh,
// karena NavMesh dungeon di-bake dari lantai saja dan tidak mengenal pintu.
// MC yang membawa kunci dan mendekat membuka pintu: collider dan obstacle mati, bagian pintu turun ke lantai lalu disembunyikan.
// Bagian pintu = child "gate" pada prefab wall-gate (temboknya tetap berdiri), atau seluruh visual jika tidak ada.
public class VaultDoor : MonoBehaviour
{
    [SerializeField] private float openDuration = 0.7f;
    // Jeda pesan "pintu terkunci" supaya tidak muncul setiap frame.
    [SerializeField] private float lockedMessageCooldown = 4f;

    private readonly List<Transform> parts = new List<Transform>();
    private readonly List<float> sinkDepths = new List<float>();
    private readonly List<Vector3> partStarts = new List<Vector3>();

    private DungeonVault vault;
    private BoxCollider blocker;
    private NavMeshObstacle obstacle;
    private float triggerRadius;
    private float openTime = -1f;
    private float lockedMessageTime = -999f;

    public bool IsOpen { get; private set; }

    // MC berada dalam radius pintu yang belum terbuka. Dipakai kartu "pintu terkunci" di HUD.
    public bool PlayerNear { get; private set; }

    public void Setup(DungeonVault owner, Transform visualRoot, IReadOnlyList<Transform> openParts, Vector3 size, float radius)
    {
        vault = owner;
        triggerRadius = radius;

        parts.Clear();
        sinkDepths.Clear();

        if (openParts != null)
        {
            foreach (Transform part in openParts)
            {
                if (part == null)
                    continue;

                parts.Add(part);
                sinkDepths.Add(HeightOf(part, size.y) + 0.1f);
            }
        }

        if (parts.Count == 0 && visualRoot != null)
        {
            parts.Add(visualRoot);
            sinkDepths.Add(size.y + 0.1f);
        }

        blocker = gameObject.AddComponent<BoxCollider>();
        blocker.size = size;
        blocker.center = Vector3.up * size.y * 0.5f;

        obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = size;
        obstacle.center = blocker.center;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
    }

    // Tinggi bagian pintu di dunia dari bounds renderer-nya, supaya turunnya pas sampai tersembunyi di bawah lantai.
    private static float HeightOf(Transform part, float fallback)
    {
        Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
            return fallback;

        Bounds bounds = renderers[0].bounds;

        foreach (Renderer target in renderers)
            bounds.Encapsulate(target.bounds);

        return Mathf.Max(0.1f, bounds.size.y);
    }

    private void Update()
    {
        if (openTime >= 0f)
        {
            AnimateOpen();
            return;
        }

        PlayerNear = false;

        if (vault == null || IsOpen)
            return;

        PlayerHealth player = PlayerHealth.Current;

        if (player == null || player.IsDead)
            return;

        Vector3 offset = player.transform.position - transform.position;
        offset.y = 0f;

        if (offset.sqrMagnitude > triggerRadius * triggerRadius)
            return;

        PlayerNear = true;

        if (vault.State == VaultState.KeyFound)
        {
            Open();
        }
        else if (vault.State == VaultState.Locked && Time.time - lockedMessageTime > lockedMessageCooldown)
        {
            lockedMessageTime = Time.time;
            vault.NotifyDoorLocked();
        }
    }

    public void Open()
    {
        if (IsOpen)
            return;

        IsOpen = true;
        openTime = 0f;

        if (blocker != null)
            blocker.enabled = false;

        if (obstacle != null)
            obstacle.enabled = false;

        partStarts.Clear();

        foreach (Transform part in parts)
            partStarts.Add(part != null ? part.position : Vector3.zero);

        SfxManager.Play(SfxCue.GateChoose);
        vault.NotifyDoorOpened();
    }

    private void AnimateOpen()
    {
        openTime += Time.deltaTime;

        float t = Mathf.Clamp01(openTime / Mathf.Max(0.01f, openDuration));
        float eased = t * t * (3f - 2f * t);

        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] == null)
                continue;

            parts[i].position = partStarts[i] + Vector3.down * sinkDepths[i] * eased;

            if (t >= 1f)
                parts[i].gameObject.SetActive(false);
        }

        if (t >= 1f)
            openTime = -1f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsOpen ? Color.green : new Color(0.31f, 0.64f, 1f);
        Gizmos.DrawWireSphere(transform.position, triggerRadius);
    }
}
