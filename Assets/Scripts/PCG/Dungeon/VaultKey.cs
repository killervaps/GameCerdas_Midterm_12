using UnityEngine;

// Kunci vault. Diambil seperti item lain (jarak, tanpa collider), lalu pintu vault bisa dibuka.
// Dibuat DungeonGenerator: visual (prefab kunci atau bentuk bawaan) dipasang sebagai child pertama sebelum komponen ini ditambahkan.
public class VaultKey : PickupItem
{
    private DungeonVault vault;

    public void Bind(DungeonVault owner)
    {
        vault = owner;
    }

    protected override bool TryApply(PlayerHealth player)
    {
        if (vault == null)
            return false;

        vault.CollectKey();
        return true;
    }
}
