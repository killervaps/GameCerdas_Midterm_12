// Aksi yang dipakai bersama oleh semua "otak" enemy (Behavior Tree, Utility AI).
// Nilai numerik harus sama dengan parameter integer "State" pada Animator (EnemyAnimation mengirim nilai ini ke Animator).
public enum EnemyAction
{
    Patrol = 0,
    Chase = 1,
    Attack = 2,
    Flee = 3,
    Dead = 4,
    Search = 5
}
