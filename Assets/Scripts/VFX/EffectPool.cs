using System.Collections.Generic;
using UnityEngine;

// Pool efek visual sekali jalan (impact, hit, ledakan) per prefab, supaya tidak ada Instantiate/Destroy setiap hit.
// Pemakaian: EffectPool.Spawn(prefab, posisi, rotasi, skala). Efek kembali ke pool sendiri setelah selesai.
public static class EffectPool
{
    private static readonly Dictionary<GameObject, Stack<PooledEffect>> pools = new Dictionary<GameObject, Stack<PooledEffect>>();

    private static Transform container;

    public static PooledEffect Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale = 1f)
    {
        if (prefab == null)
            return null;

        if (!pools.TryGetValue(prefab, out Stack<PooledEffect> stack))
        {
            stack = new Stack<PooledEffect>();
            pools[prefab] = stack;
        }

        PooledEffect effect = null;

        // Efek di pool bisa sudah hancur (scene diganti, atau efek menghancurkan dirinya sendiri).
        while (effect == null && stack.Count > 0)
            effect = stack.Pop();

        if (effect == null)
            effect = Create(prefab);

        effect.Play(position, rotation, scale);

        return effect;
    }

    internal static void Return(PooledEffect effect)
    {
        if (effect == null || effect.Prefab == null)
            return;

        if (pools.TryGetValue(effect.Prefab, out Stack<PooledEffect> stack))
            stack.Push(effect);
    }

    private static PooledEffect Create(GameObject prefab)
    {
        if (container == null)
            container = new GameObject("EffectPool").transform;

        GameObject instance = Object.Instantiate(prefab, container);
        instance.name = prefab.name;
        instance.SetActive(false);

        PooledEffect effect = instance.AddComponent<PooledEffect>();
        effect.Initialize(prefab);

        return effect;
    }
}
