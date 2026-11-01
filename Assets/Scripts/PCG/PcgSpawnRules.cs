using System;
using UnityEngine;

// Parameter controlled randomness, diatur dari Inspector. Sama dengan bagian Validation di modul praktikum,
// ditambah satu aturan khusus project ini (tempat tinggi).
[Serializable]
public class PcgSpawnRules
{
    // Rule 1: jarak minimal dari MC (titik masuk arena), supaya musuh tidak muncul tepat di samping pemain.
    [Min(0f)] public float minDistanceFromPlayer = 4f;

    // Rule 2: radius CheckSphere terhadap obstacle. Kira-kira setengah lebar badan objek yang di-spawn.
    [Min(0.05f)] public float checkRadius = 0.35f;

    // Layer obstacle. Nothing = memakai layer NavBlock (tembok, menara, rintangan).
    public LayerMask obstacleMask;

    // Rule 3: jarak minimal dari objek procedural lain (musuh dan item), supaya tidak bertumpuk.
    [Min(0f)] public float minimumSpacing = 1.2f;

    // Batas percobaan per objek. Jika semua percobaan gagal, objek itu dilewati supaya tidak terjadi loop tanpa akhir.
    [Min(1)] public int maxAttemptsPerObject = 50;

    // Aturan tambahan: titik di atas menara (ElevatedPlatform) ditolak. Charger dan Guardian tidak bisa turun dari menara.
    public bool allowElevated = false;

    // Aturan tambahan: titik harus berada di NavMesh dan terhubung dengan titik masuk MC lewat jalur NavMesh.
    // Menolak lantai di luar tembok (pengisi celah), lubang, dan sudut tertutup yang tidak bisa dicapai.
    public bool requireReachable = true;
    // Seberapa jauh titik boleh bergeser saat dicari padanannya di NavMesh.
    [Min(0.05f)] public float navMeshSnapDistance = 0.6f;
    // Area NavMesh yang tidak boleh dipakai jalur pengecekan, mis. link lompat Shooter.
    public string excludedNavMeshArea = "archer_jump";

    public int ObstacleMask => obstacleMask.value != 0 ? obstacleMask.value : GameLayers.ObstacleMask.value;

    public int NavMeshAreaMask
    {
        get
        {
            int area = string.IsNullOrEmpty(excludedNavMeshArea) ? -1 : UnityEngine.AI.NavMesh.GetAreaFromName(excludedNavMeshArea);
            return area >= 0 ? UnityEngine.AI.NavMesh.AllAreas & ~(1 << area) : UnityEngine.AI.NavMesh.AllAreas;
        }
    }

    public PcgSpawnRules Clone()
    {
        return (PcgSpawnRules)MemberwiseClone();
    }
}
