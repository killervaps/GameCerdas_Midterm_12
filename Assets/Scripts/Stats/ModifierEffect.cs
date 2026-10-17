using UnityEngine;

// Satu operasi matematika pada satu stat, mis. Defense ×1,5.
[System.Serializable]
public struct ModifierEffect
{
    public StatType stat;
    public ModifierOp op;
    public float value;

    public ModifierEffect(StatType stat, ModifierOp op, float value)
    {
        this.stat = stat;
        this.op = op;
        this.value = value;
    }

    // True jika efek menaikkan stat. Karena semua stat "makin besar makin bagus", naik berarti buff.
    public bool IsBuff
    {
        get
        {
            switch (op)
            {
                case ModifierOp.Add: return value > 0f;
                case ModifierOp.Subtract: return value < 0f;
                case ModifierOp.Multiply: return value > 1f;
                case ModifierOp.Divide: return value > 0f && value < 1f;
                default: return false;
            }
        }
    }

    // True jika efek menurunkan stat.
    public bool IsTradeOff
    {
        get
        {
            switch (op)
            {
                case ModifierOp.Add: return value < 0f;
                case ModifierOp.Subtract: return value > 0f;
                case ModifierOp.Multiply: return value < 1f;
                case ModifierOp.Divide: return value > 1f;
                default: return false;
            }
        }
    }

    public string Symbol
    {
        get
        {
            switch (op)
            {
                case ModifierOp.Add: return "+";
                case ModifierOp.Subtract: return "-";
                case ModifierOp.Multiply: return "×";
                case ModifierOp.Divide: return "÷";
                default: return "?";
            }
        }
    }

    // Teks untuk kartu gate, mis. "Defense ×1.5".
    public override string ToString()
    {
        return stat + " " + Symbol + value.ToString("0.##");
    }

    public static bool IsValidValue(ModifierOp op, float value)
    {
        // Bagi nol tidak terdefinisi, kali nol membuat stat langsung jatuh ke batas bawah.
        if (op == ModifierOp.Divide || op == ModifierOp.Multiply)
            return !Mathf.Approximately(value, 0f);

        return true;
    }
}
