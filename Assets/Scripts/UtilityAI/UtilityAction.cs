using System;
using UnityEngine;

// Satu kandidat aksi: fungsi skor (0-1) dan fungsi eksekusi.
public class UtilityAction
{
    private readonly Func<float> scoreFunc;
    private readonly Action execute;

    public EnemyAction Id { get; }

    // Skor hasil Evaluate() terakhir (tanpa bonus inertia).
    public float Score { get; private set; }

    public UtilityAction(
        EnemyAction id,
        Func<float> score,
        Action execute)
    {
        Id = id;
        scoreFunc = score;
        this.execute = execute;
    }

    public float Evaluate()
    {
        Score = Mathf.Clamp01(scoreFunc());

        return Score;
    }

    public void Execute()
    {
        execute();
    }
}
