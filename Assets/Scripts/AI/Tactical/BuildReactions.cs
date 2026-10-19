using System;
using System.Text;
using UnityEngine;

// Reaksi musuh terhadap build MC. Gate mengubah keputusan AI, bukan hanya angka stat.
//   Baja (tahan, lambat)          Charger tetap menyerbu, Shooter berani dekat (KiteEffect tinggi)
//   Kaca (damage tinggi, rapuh)   Charger menunggu rekan, Shooter jaga jarak lebih jauh dan cepat kabur, Guardian maju jadi tameng
//   Pelari (cepat, damage turun)  Charger mencegat dengan prediksi posisi, Shooter pindah posisi (bukan kiting), pincer lebih kuat
//   Pembagi (HP besar, damage /2) Charger hit-and-run (recover singkat), Shooter lebih sabar, leash Guardian dilonggarkan, penyerang lebih cepat bergantian
// Semua dibaca dari BuildIndicators di GroupBlackboard.
[Serializable]
public class BuildReactions
{
    // Dipakai brain saat tidak ada TacticalCoordinator. Semua reaksi netral.
    public static readonly BuildReactions Disabled = new BuildReactions { enabled = false };

    [SerializeField] private bool enabled = true;

    [Header("Thresholds (1 = build dasar)")]
    [SerializeField] private float highOffense = 1.5f;
    [SerializeField] private float lowOffense = 0.75f;
    [SerializeField] private float highMobility = 1.2f;
    [SerializeField] private float highDurability = 1.3f;

    [Header("Charger")]
    [SerializeField] private bool chargerWaitsForAlly = true;
    // Batas menunggu rekan. Setelah ini Charger maju sendiri sebagai umpan, mencegah semua saling menunggu.
    [SerializeField] private float chargerMaxAllyWait = 2.5f;
    // Detik ke depan untuk memprediksi posisi MC cepat (chase dan arah lunge).
    [SerializeField] private float chargerPredictionTime = 0.4f;
    [SerializeField, Range(0.2f, 1f)] private float chargerRecoverMultiplier = 0.6f;

    [Header("Shooter")]
    // Offset jarak ideal × (1 + k × (DayaSerang − 1)).
    [SerializeField] private float shooterIdealPerOffense = 1f;
    // Ambang HP kabur bergeser sebesar k × (DayaSerang − 1). Positif = lebih cepat kabur.
    [SerializeField] private float shooterFleeShiftPerOffense = 0.25f;

    [Header("Guardian")]
    [SerializeField] private float guardianGuardDistanceMultiplier = 1.5f;
    [SerializeField] private float guardianLeashMultiplier = 1.5f;
    [SerializeField] private float guardianAggroMultiplier = 1.3f;

    [Header("Group")]
    [SerializeField] private float pincerBonusMultiplier = 2f;
    [SerializeField, Range(0.2f, 1f)] private float tokenTimeoutMultiplier = 0.6f;

    public bool Enabled => enabled;

    public bool IsHighOffense(BuildIndicators b) => enabled && b.offense >= highOffense;
    public bool IsLowOffense(BuildIndicators b) => enabled && b.offense <= lowOffense;
    public bool IsFast(BuildIndicators b) => enabled && b.mobility >= highMobility;
    public bool IsDurable(BuildIndicators b) => enabled && b.durability >= highDurability;

    public bool ChargerWaitsForAlly(BuildIndicators b) => chargerWaitsForAlly && IsHighOffense(b);

    public float ChargerMaxAllyWait => chargerMaxAllyWait;

    public float ChargerPredictionTime(BuildIndicators b) => IsFast(b) ? chargerPredictionTime : 0f;

    public float ChargerRecoverMultiplier(BuildIndicators b) => IsLowOffense(b) ? chargerRecoverMultiplier : 1f;

    public float ShooterIdealOffsetMultiplier(BuildIndicators b) =>
        enabled ? Mathf.Clamp(1f + shooterIdealPerOffense * (b.offense - 1f), 0.5f, 2.5f) : 1f;

    public float ShooterFleeShift(BuildIndicators b) =>
        enabled ? Mathf.Clamp(shooterFleeShiftPerOffense * (b.offense - 1f), -0.15f, 0.25f) : 0f;

    public float GuardianGuardDistanceMultiplier(BuildIndicators b) => IsHighOffense(b) ? guardianGuardDistanceMultiplier : 1f;

    public float GuardianLeashMultiplier(BuildIndicators b) => IsDurable(b) ? guardianLeashMultiplier : 1f;

    public float GuardianAggroMultiplier(BuildIndicators b) => IsDurable(b) ? guardianAggroMultiplier : 1f;

    public float PincerBonusMultiplier(BuildIndicators b) => IsFast(b) ? pincerBonusMultiplier : 1f;

    public float TokenTimeoutMultiplier(BuildIndicators b) => IsLowOffense(b) ? tokenTimeoutMultiplier : 1f;

    // Ringkasan reaksi aktif untuk overlay debug.
    public string Describe(BuildIndicators b)
    {
        if (!enabled)
            return "reaksi build mati";

        StringBuilder builder = new StringBuilder();

        if (IsHighOffense(b))
            builder.Append("DayaSerang tinggi: Charger tunggu rekan, Shooter jaga jarak, Guardian maju. ");

        if (IsLowOffense(b))
            builder.Append("DayaSerang rendah: Charger hit-and-run, Shooter sabar, penyerang cepat bergantian. ");

        if (IsFast(b))
            builder.Append("Mobilitas tinggi: Charger mencegat, pincer diperkuat. ");

        if (IsDurable(b))
            builder.Append("Ketahanan tinggi: leash dan aggro Guardian dilonggarkan. ");

        return builder.Length > 0 ? builder.ToString().TrimEnd() : "build netral";
    }
}
