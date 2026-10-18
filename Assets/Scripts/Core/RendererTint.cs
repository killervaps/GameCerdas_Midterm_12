using UnityEngine;

// Mewarnai semua renderer di bawah objek ini tanpa membuat material baru (MaterialPropertyBlock).
// Dipakai untuk membedakan role musuh selama semua masih memakai model TripleT.
public class RendererTint : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField] private Color color = Color.white;

    private Renderer[] renderers;
    private MaterialPropertyBlock block;

    public Color Color => color;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        block = new MaterialPropertyBlock();

        Apply();
    }

    public void SetColor(Color newColor)
    {
        color = newColor;
        Apply();
    }

    private void Apply()
    {
        if (renderers == null)
            return;

        foreach (Renderer target in renderers)
        {
            if (target == null || target.sharedMaterial == null)
                continue;

            target.GetPropertyBlock(block);

            // URP Lit memakai _BaseColor, shader lama memakai _Color.
            if (target.sharedMaterial.HasProperty(BaseColorId))
                block.SetColor(BaseColorId, color);

            if (target.sharedMaterial.HasProperty(ColorId))
                block.SetColor(ColorId, color);

            target.SetPropertyBlock(block);
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying && renderers != null)
            Apply();
    }
}
