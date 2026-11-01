using System;
using UnityEngine;

// Dasar semua item yang bisa diambil MC. Item baru cukup menurunkan kelas ini dan mengisi TryApply.
// Tidak memakai collider: MC dianggap mengambil item jika jaraknya di bawah Pickup Radius.
// Dengan begitu item tidak menghalangi proyektil, line of sight musuh, CheckSphere PCG, atau lock-on.
public abstract class PickupItem : MonoBehaviour
{
    [Header("Pickup")]
    [SerializeField] private string displayName = "Item";
    // Jarak datar dari kaki MC ke item.
    [SerializeField, Min(0.1f)] private float pickupRadius = 0.9f;
    // Selisih tinggi maksimal, supaya MC di atas menara tidak mengambil item di bawahnya.
    [SerializeField, Min(0.1f)] private float pickupHeight = 1.6f;
    // Opsional. Efek sekali jalan saat item diambil.
    [SerializeField] private GameObject pickupEffect;

    [Header("Motion")]
    // Kosong = child pertama. Hanya visual ini yang bergerak, posisi item (hasil PCG) tetap.
    [SerializeField] private Transform visual;
    [SerializeField] private float hoverHeight = 0.45f;
    [SerializeField] private float bobHeight = 0.12f;
    [SerializeField] private float bobSpeed = 2.2f;
    [SerializeField] private float spinSpeed = 90f;
    // Muncul dari kecil supaya kemunculannya terlihat saat ronde mulai.
    [SerializeField, Min(0.01f)] private float spawnPopDuration = 0.35f;

    private Vector3 visualScale = Vector3.one;
    private float age;
    private float phase;
    private bool collected;

    public string DisplayName => displayName;

    // Dikirim tepat sebelum item dihancurkan karena diambil.
    public event Action<PickupItem> PickedUp;

    // Efek item ke MC. False = item tidak terpakai (mis. HP sudah penuh), item tetap di tempat.
    protected abstract bool TryApply(PlayerHealth player);

    protected virtual void Awake()
    {
        if (visual == null && transform.childCount > 0)
            visual = transform.GetChild(0);

        if (visual != null)
        {
            visualScale = visual.localScale;
            visual.localScale = Vector3.zero;
        }

        // Fase gerak dari posisi, supaya beberapa item tidak naik turun serempak tanpa memakai Random global.
        phase = (transform.position.x * 1.7f + transform.position.z * 2.3f) % (Mathf.PI * 2f);
    }

    protected virtual void Update()
    {
        age += Time.deltaTime;
        Animate();

        if (!collected)
            CheckPickup();
    }

    private void Animate()
    {
        if (visual == null)
            return;

        // Ease-out-back: membesar dari 0, sedikit melewati ukuran asli, lalu kembali ke 1.
        float t = Mathf.Clamp01(age / spawnPopDuration) - 1f;
        float scale = 1f + 2.70158f * t * t * t + 1.70158f * t * t;

        visual.localScale = visualScale * scale;
        visual.localPosition = Vector3.up * (hoverHeight + Mathf.Sin(age * bobSpeed + phase) * bobHeight);
        visual.localRotation = Quaternion.Euler(0f, age * spinSpeed, 0f);
    }

    private void CheckPickup()
    {
        PlayerHealth player = PlayerHealth.Current;

        if (player == null || player.IsDead)
            return;

        Vector3 offset = player.transform.position - transform.position;

        if (Mathf.Abs(offset.y) > pickupHeight)
            return;

        offset.y = 0f;

        if (offset.sqrMagnitude > pickupRadius * pickupRadius)
            return;

        if (TryApply(player))
            Collect();
    }

    private void Collect()
    {
        collected = true;

        SfxManager.Play(SfxCue.ItemPickup);

        if (pickupEffect != null)
        {
            Vector3 point = visual != null ? visual.position : transform.position;
            Instantiate(pickupEffect, point, Quaternion.identity);
        }

        PickedUp?.Invoke(this);
        Destroy(gameObject);
    }

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
