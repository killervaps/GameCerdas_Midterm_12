using System.Text;

// Ukuran kuantitatif satu dungeon (Metrics Dungeon di modul), supaya hasil PCG bisa dibandingkan dengan angka,
// bukan hanya "terlihat bagus". Dihitung DungeonGenerator setiap kali dungeon dibangun.
public struct DungeonMetrics
{
    public int seed;
    // Seed percobaan yang akhirnya dibangun setelah generate-and-test.
    public int builtSeed;
    public int attempts;
    public bool valid;

    public int leafCount;
    public int roomCount;
    public int floorCells;
    // Wall yang dibangun (bersebelahan dengan Floor).
    public int wallCells;
    // Floor di luar room = panjang total corridor dalam cell.
    public int corridorCells;
    public int totalCells;
    public float averageRoomArea;
    public int smallestRoomSide;
    public int largestRoomSide;

    // Eksperimen 6: panjang jalur BFS dari Start untuk tiga strategi Goal.
    //   A = room terakhir di list, B = room terjauh garis lurus, C = room terjauh jarak jalan (yang dipakai).
    public int pathLastRoom;
    public int pathGeometric;
    public int pathBfs;
    public float straightDistance;

    // Vault Room: jumlah room dead-end (kandidat vault), vault dibangun atau tidak, jarak BFS Start ke kunci.
    public int deadEndCount;
    public bool hasVault;
    public int keyDistance;

    // Dekorasi: semua props, yang menghalangi jalan, dan yang dibatalkan karena memutus jalur BFS.
    public int propCount;
    public int blockingProps;
    public int rejectedProps;

    public int builtObjects;
    public float generateMs;
    public float buildMs;
    public float bakeMs;

    public float FloorPercent => totalCells > 0 ? floorCells * 100f / totalCells : 0f;

    public static string TableHeader =>
        "| Seed | Valid | Percobaan | Leaf | Room | Floor | Floor % | Wall | Corridor | Start-Goal (A / B / C) | Dead-end | Vault (kunci) | Props (blok / ditolak) | Objek | Generate + Build ms |";

    public static string TableDivider =>
        "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |";

    public string TableRow()
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("| ").Append(seed).Append(builtSeed != seed ? " (" + builtSeed + ")" : string.Empty)
            .Append(" | ").Append(valid ? "Ya" : "Tidak")
            .Append(" | ").Append(attempts)
            .Append(" | ").Append(leafCount)
            .Append(" | ").Append(roomCount)
            .Append(" | ").Append(floorCells)
            .Append(" | ").Append(FloorPercent.ToString("0.0"))
            .Append(" | ").Append(wallCells)
            .Append(" | ").Append(corridorCells)
            .Append(" | ").Append(pathLastRoom).Append(" / ").Append(pathGeometric).Append(" / ").Append(pathBfs)
            .Append(" | ").Append(deadEndCount)
            .Append(" | ").Append(hasVault ? "Ya (" + keyDistance + ")" : "-")
            .Append(" | ").Append(propCount).Append(" (").Append(blockingProps).Append(" / ").Append(rejectedProps).Append(')')
            .Append(" | ").Append(builtObjects)
            .Append(" | ").Append((generateMs + buildMs).ToString("0.0"))
            .Append(" |");
        return builder.ToString();
    }
}
