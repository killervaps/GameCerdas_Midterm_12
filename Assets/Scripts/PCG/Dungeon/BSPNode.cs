// Satu area persegi di pohon BSP. Area dibagi dua (left dan right) sampai cukup kecil.
// Node tanpa anak adalah Leaf, dan setiap Leaf boleh berisi satu room.
public class BSPNode
{
    public int x;
    public int y;
    public int width;
    public int height;

    public BSPNode left;
    public BSPNode right;

    public DungeonRoom room;

    public BSPNode(int x, int y, int width, int height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    public bool IsLeaf => left == null && right == null;
}
