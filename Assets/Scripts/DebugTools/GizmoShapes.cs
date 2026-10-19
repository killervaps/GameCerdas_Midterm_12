using UnityEngine;

// Bentuk gizmo yang dipakai beberapa sistem AI. Dipanggil dari OnDrawGizmos, warna diatur pemanggil.
public static class GizmoShapes
{
    // Lingkaran datar di bidang XZ.
    public static void Circle(Vector3 center, float radius, int segments = 40)
    {
        Vector3 previous = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
