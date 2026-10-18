// Aksi yang dipakai bersama oleh semua "otak" enemy (Behavior Tree, Utility AI).
// Nilai numerik harus sama dengan parameter integer "State" pada Animator (EnemyAnimation mengirim nilai ini ke Animator).
// Nilai 0-5 berasal dari praktikum. Aksi baru ditambahkan di belakang supaya Animator lama tidak bergeser.
public enum EnemyAction
{
    Patrol = 0,
    Chase = 1,
    Attack = 2,
    Flee = 3,
    Dead = 4,
    Search = 5,
    Idle = 6,

    // Charger
    Windup = 7,
    Lunge = 8,
    Recover = 9,

    // Shooter
    Kite = 10,
    Reposition = 11,
    Hold = 12,

    // Guardian
    Guard = 13,
    Return = 14,
    Enraged = 15
}
