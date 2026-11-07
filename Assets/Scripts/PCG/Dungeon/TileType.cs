// Isi satu cell grid dungeon.
public enum TileType
{
    // Tidak bisa dilewati. Seluruh grid dimulai sebagai Wall, lalu room dan corridor "diukir" menjadi Floor.
    Wall,
    Floor,
    // Posisi awal MC.
    Start,
    // Tujuan dungeon.
    Goal
}
