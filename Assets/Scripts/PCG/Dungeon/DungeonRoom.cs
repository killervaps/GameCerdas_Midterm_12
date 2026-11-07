using UnityEngine;

// Satu room persegi di grid dungeon. x, y = pojok kiri bawah (dalam cell).
[System.Serializable]
public class DungeonRoom
{
    public int x;
    public int y;
    public int width;
    public int height;

    public DungeonRoom(int x, int y, int width, int height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    // Titik tengah room. Dipakai corridor, Start, Goal, dan penempatan musuh atau item.
    public Vector2Int Center => new Vector2Int(x + width / 2, y + height / 2);

    public int Area => width * height;

    public bool Contains(Vector2Int cell)
    {
        return cell.x >= x && cell.x < x + width && cell.y >= y && cell.y < y + height;
    }
}
