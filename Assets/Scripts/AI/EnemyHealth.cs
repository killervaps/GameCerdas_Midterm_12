using System.Collections.Generic;
using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    // Daftar enemy yang sedang aktif, dipakai player untuk memilih target terdekat. 
    // Enemy yang di-SetActive(false) otomatis tidak ikut.
    private static readonly List<EnemyHealth> active = new List<EnemyHealth>();

    public static IReadOnlyList<EnemyHealth> Active => active;

    [SerializeField] private float maxHealth = 100f;

    public float CurrentHealth { get; private set; }

    public float MaxHealth => maxHealth;

    public bool IsDead =>
        CurrentHealth <= 0f;

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    private void OnEnable()
    {
        active.Add(this);
    }

    private void OnDisable()
    {
        active.Remove(this);
    }

    public void TakeDamage(float damage)
    {
        if (IsDead)
            return;

        CurrentHealth -= damage;

        CurrentHealth = Mathf.Clamp(
            CurrentHealth,
            0f,
            maxHealth
        );

        Debug.Log(
            name + " HP: " + CurrentHealth
        );
    }

    [ContextMenu("Test Damage 25")]
    private void TestDamage25()
    {
        TakeDamage(25f);
    }

    [ContextMenu("Reset Health")]
    private void ResetHealth()
    {
        CurrentHealth = maxHealth;
    }
}
