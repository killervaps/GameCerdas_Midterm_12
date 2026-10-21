using System;
using System.Reflection;
using UnityEngine;

// Ditambahkan otomatis oleh EffectPool ke setiap salinan efek. Memutar ulang partikel saat dipakai,
// lalu menonaktifkan diri setelah durasi efek habis sehingga kembali ke pool.
public class PooledEffect : MonoBehaviour
{
    // Dipakai jika durasi tidak bisa dihitung dari partikel (mis. efek looping).
    private const float FallbackLifetime = 2f;
    private const float MaxLifetime = 10f;

    private ParticleSystem[] systems;
    private Vector3 baseScale;
    private float lifetime;
    private float releaseTime;
    private bool inPool = true;

    public GameObject Prefab { get; private set; }

    public float Lifetime => lifetime;

    internal void Initialize(GameObject prefab)
    {
        Prefab = prefab;
        baseScale = transform.localScale;
        systems = GetComponentsInChildren<ParticleSystem>(true);
        lifetime = ComputeLifetime();

        UseDisableClearBehavior();
    }

    internal void Play(Vector3 position, Quaternion rotation, float scale)
    {
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = baseScale * Mathf.Max(0.01f, scale);

        inPool = false;
        releaseTime = Time.time + lifetime;

        gameObject.SetActive(true);

        foreach (ParticleSystem system in systems)
        {
            // Hanya root partikel yang diputar, child ikut lewat withChildren.
            if (system.transform.parent != null && system.transform.parent.GetComponentInParent<ParticleSystem>() != null)
                continue;

            system.Clear(true);
            system.Play(true);
        }
    }

    private void Update()
    {
        if (Time.time >= releaseTime)
            gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        // Dimatikan oleh timer sendiri atau oleh CFXR_Effect (Clear Behavior = Disable).
        if (inPool)
            return;

        inPool = true;
        EffectPool.Return(this);
    }

    private float ComputeLifetime()
    {
        float longest = 0f;

        foreach (ParticleSystem system in systems)
        {
            ParticleSystem.MainModule main = system.main;

            if (main.loop)
                return FallbackLifetime;

            float duration = main.startDelay.constantMax + main.duration + main.startLifetime.constantMax;
            longest = Mathf.Max(longest, duration);
        }

        return longest > 0f ? Mathf.Min(longest, MaxLifetime) : FallbackLifetime;
    }

    // Prefab Cartoon FX (CFXR_Effect) defaultnya menghancurkan diri setelah selesai, yang merusak pooling.
    // Pada salinan runtime ini Clear Behavior diubah ke Disable. Prefab asli tidak diubah.
    // Memakai reflection supaya script ini tidak bergantung langsung pada assembly Cartoon FX.
    private void UseDisableClearBehavior()
    {
        foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "CFXR_Effect")
                continue;

            FieldInfo field = behaviour.GetType().GetField(
                "clearBehavior",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            if (field == null || !field.FieldType.IsEnum)
                continue;

            try
            {
                field.SetValue(behaviour, Enum.Parse(field.FieldType, "Disable"));
            }
            catch (ArgumentException)
            {
                // Versi Cartoon FX tanpa opsi Disable. Efek tetap jalan, hanya tidak dipakai ulang.
            }
        }
    }
}
