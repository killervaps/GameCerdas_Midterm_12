// Stat MC yang bisa diubah gate. Semua stat bersifat "makin besar makin bagus".
// Urutan nilai dipakai sebagai indeks array, jadi tambahkan stat baru di akhir dan perbarui StatTypes.Count.
public enum StatType
{
    MaxHP,
    Damage,
    AttackRate,
    Range,
    Defense,
    MoveSpeed
}

public static class StatTypes
{
    public const int Count = 6;
}
