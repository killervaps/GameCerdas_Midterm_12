using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

// Pool proyektil supaya tidak ada Instantiate/Destroy setiap tembakan.
// Proyektil diletakkan di objek root tersendiri, jadi pool aman dipasang di objek yang bergerak (mis. MC).
public class ProjectilePool : MonoBehaviour
{
    [SerializeField] private Projectile prefab;
    [SerializeField] private int defaultCapacity = 16;
    [SerializeField] private int maxSize = 64;
    // Jumlah proyektil yang dibuat di awal supaya tembakan pertama tidak memicu Instantiate.
    [SerializeField] private int prewarmCount = 8;

    private ObjectPool<Projectile> pool;
    private Transform container;

    public int CountActive => pool != null ? pool.CountActive : 0;

    public int CountInactive => pool != null ? pool.CountInactive : 0;

    private void Awake()
    {
        if (prefab == null)
        {
            Debug.LogError("ProjectilePool " + name + ": prefab belum diisi.", this);
            enabled = false;
            return;
        }

        container = new GameObject("Pool_" + prefab.name).transform;

        pool = new ObjectPool<Projectile>(
            CreateItem,
            item => item.gameObject.SetActive(true),
            item => item.gameObject.SetActive(false),
            DestroyItem,
            true,
            defaultCapacity,
            maxSize
        );

        Prewarm();
    }

    private void OnDestroy()
    {
        // Kosongkan pool selagi masih bisa. Unity juga membersihkan semua ObjectPool saat keluar Play Mode,
        // dan saat itu proyektil sudah ikut hancur bersama scene.
        pool?.Dispose();
        pool = null;

        if (container != null)
            Destroy(container.gameObject);
    }

    public Projectile Spawn()
    {
        return pool != null ? pool.Get() : null;
    }

    public void Release(Projectile projectile)
    {
        if (pool == null)
        {
            Destroy(projectile.gameObject);
            return;
        }

        pool.Release(projectile);
    }

    private Projectile CreateItem()
    {
        Projectile item = Instantiate(prefab, container);
        item.gameObject.SetActive(false);
        return item;
    }

    private static void DestroyItem(Projectile item)
    {
        // Proyektil bisa sudah dihancurkan lebih dulu oleh unload scene.
        if (item != null)
            Destroy(item.gameObject);
    }

    private void Prewarm()
    {
        List<Projectile> warm = new List<Projectile>(prewarmCount);

        for (int i = 0; i < prewarmCount; i++)
            warm.Add(pool.Get());

        foreach (Projectile item in warm)
            pool.Release(item);
    }
}
