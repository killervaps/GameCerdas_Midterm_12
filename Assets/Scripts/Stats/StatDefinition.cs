// Nilai dasar dan batas bawah satu stat MC.
[System.Serializable]
public struct StatDefinition
{
    public StatType stat;
    public float baseValue;
    public float clampMin;

    public StatDefinition(StatType stat, float baseValue, float clampMin)
    {
        this.stat = stat;
        this.baseValue = baseValue;
        this.clampMin = clampMin;
    }
}
