using System;
using System.Collections.Generic;
using UnityEngine;

public enum VaultState
{
    Locked,
    KeyFound,
    Open,
    Looted
}

public enum VaultEvent
{
    KeyFound,
    DoorLocked,
    DoorOpened,
    Looted
}

// Vault room (Treasure Room) satu dungeon: room dead-end yang ditutup pintu, kunci di room lain, dan chest di dalamnya.
// Dibuat DungeonGenerator. Chest memberi satu gate gratis dengan rarity minimal Min Rarity:
// hanya efek buff gate itu yang dipakai (trade-off dibuang), dan gate ini tidak dihitung sebagai gate pilihan Safezone.
// Hadiah diacak dari seed vault, jadi seed sama + build sama = hadiah sama.
public class DungeonVault : MonoBehaviour
{
    private GateRarity minRarity = GateRarity.VeryRare;
    private int rewardSeed;

    public static DungeonVault Active { get; private set; }

    // Dipakai UI untuk banner (kunci didapat, pintu terkunci, vault terbuka, hadiah).
    public static event Action<DungeonVault, VaultEvent> Changed;

    public VaultState State { get; private set; }

    public MathGateData Reward { get; private set; }

    public GateRarity MinRarity => minRarity;

    public DungeonRoom Room { get; private set; }

    public VaultDoor Door { get; private set; }

    public VaultChest Chest { get; private set; }

    public VaultKey Key { get; private set; }

    public Vector3 KeyPosition { get; private set; }

    public void Setup(DungeonRoom room, GateRarity minimum, int seed, Vector3 keyPosition)
    {
        Room = room;
        minRarity = minimum;
        rewardSeed = seed;
        KeyPosition = keyPosition;
        State = VaultState.Locked;
    }

    public void AttachParts(VaultDoor door, VaultChest chest, VaultKey key)
    {
        Door = door;
        Chest = chest;
        Key = key;
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
            Active = this;
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = null;
    }

    public void CollectKey()
    {
        if (State != VaultState.Locked)
            return;

        State = VaultState.KeyFound;
        Raise(VaultEvent.KeyFound);
    }

    // Dipanggil pintu saat MC mendekat tanpa kunci.
    public void NotifyDoorLocked()
    {
        Raise(VaultEvent.DoorLocked);
    }

    public void NotifyDoorOpened()
    {
        if (State >= VaultState.Open)
            return;

        State = VaultState.Open;
        Raise(VaultEvent.DoorOpened);
    }

    // Dipanggil chest. Mengacak gate hadiah lalu memasukkan efek buff-nya ke build MC.
    public MathGateData Loot()
    {
        if (State == VaultState.Looted)
            return Reward;

        State = VaultState.Looted;

        GateGenerator generator = FindAnyObjectByType<GateGenerator>();
        PlayerStats stats = PlayerHealth.Current != null ? PlayerHealth.Current.GetComponent<PlayerStats>() : null;

        if (stats == null)
            stats = FindAnyObjectByType<PlayerStats>();

        Reward = RollReward(generator, stats, minRarity, new SeededRandom(rewardSeed));

        if (Reward != null && stats != null)
            stats.ApplyVaultGate(Reward);
        else
            Debug.LogWarning("DungeonVault: tidak ada gate " + GateRarityStyle.Label(minRarity) + " ke atas di pool GateGenerator, chest kosong.", this);

        Raise(VaultEvent.Looted);
        return Reward;
    }

    private void Raise(VaultEvent vaultEvent)
    {
        Changed?.Invoke(this, vaultEvent);
    }

    // Dua tahap seperti GateGenerator: rarity dulu sesuai peluang gate biasa (hanya rarity >= minimum yang punya kandidat),
    // lalu satu gate acak dari rarity itu. Gate yang sudah ada di build dihindari selama masih ada pilihan lain.
    public static MathGateData RollReward(GateGenerator generator, PlayerStats stats, GateRarity minimum, SeededRandom random)
    {
        if (generator == null || generator.Pool == null)
            return null;

        List<MathGateData> candidates = Candidates(generator, stats, minimum, true);

        if (candidates.Count == 0)
            candidates = Candidates(generator, stats, minimum, false);

        if (candidates.Count == 0)
            return null;

        float total = 0f;

        for (int r = (int)minimum; r < GateRarityStyle.Count; r++)
        {
            if (candidates.Exists(g => (int)g.Rarity == r))
                total += generator.ChanceSetting((GateRarity)r);
        }

        GateRarity picked = candidates[0].Rarity;

        if (total > 0f)
        {
            float roll = random.Value * total;

            for (int r = (int)minimum; r < GateRarityStyle.Count; r++)
            {
                if (!candidates.Exists(g => (int)g.Rarity == r))
                    continue;

                picked = (GateRarity)r;
                roll -= generator.ChanceSetting(picked);

                if (roll <= 0f)
                    break;
            }
        }

        List<MathGateData> ofRarity = candidates.FindAll(g => g.Rarity == picked);
        return ofRarity[random.Range(0, ofRarity.Count)];
    }

    // Peluang tiap rarity untuk chest (0-1), untuk panel debug dan demo.
    public static float ChanceOf(GateGenerator generator, GateRarity rarity, GateRarity minimum)
    {
        if (generator == null || rarity < minimum || generator.CountInPool(rarity) == 0)
            return 0f;

        float total = 0f;

        for (int r = (int)minimum; r < GateRarityStyle.Count; r++)
        {
            if (generator.CountInPool((GateRarity)r) > 0)
                total += generator.ChanceSetting((GateRarity)r);
        }

        return total > 0f ? generator.ChanceSetting(rarity) / total : 0f;
    }

    private static List<MathGateData> Candidates(GateGenerator generator, PlayerStats stats, GateRarity minimum, bool skipOwned)
    {
        List<MathGateData> list = new List<MathGateData>();

        foreach (MathGateData gate in generator.Pool)
        {
            if (gate == null || gate.Rarity < minimum || !gate.HasBuff || list.Contains(gate))
                continue;

            if (skipOwned && stats != null && (Contains(stats.SelectedGates, gate) || Contains(stats.VaultGates, gate)))
                continue;

            list.Add(gate);
        }

        return list;
    }

    private static bool Contains(IReadOnlyList<MathGateData> list, MathGateData gate)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == gate)
                return true;
        }

        return false;
    }

    public string StatusText()
    {
        switch (State)
        {
            case VaultState.KeyFound: return "kunci didapat, pintu masih tertutup";
            case VaultState.Open: return "pintu terbuka, chest belum dibuka";
            case VaultState.Looted: return Reward != null ? "dibuka: " + Reward.DisplayName + " (" + GateRarityStyle.Label(Reward.Rarity) + ")" : "dibuka, chest kosong";
            default: return "terkunci, kunci belum diambil";
        }
    }
}
