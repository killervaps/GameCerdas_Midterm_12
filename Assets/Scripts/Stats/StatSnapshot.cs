// Kumpulan nilai semua stat pada satu waktu. Dipakai untuk hasil Preview(gate) tanpa mengubah PlayerStats.
public readonly struct StatSnapshot
{
    private readonly float[] values;

    public StatSnapshot(float[] values)
    {
        this.values = values;
    }

    public bool IsValid => values != null;

    public float Get(StatType stat)
    {
        return values != null ? values[(int)stat] : 0f;
    }

    public float this[StatType stat] => Get(stat);
}
