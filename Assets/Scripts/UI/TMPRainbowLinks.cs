using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Mewarnai huruf di dalam <link="rgb">...</link> dengan pelangi yang berputar (gaya lampu RGB gaming).
// Dipakai untuk nama dan label rarity SIGMA. Bekerja di teks dunia (TextMeshPro) maupun UI (TextMeshProUGUI).
// Teks tanpa link rgb tidak diproses sama sekali.
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class TMPRainbowLinks : MonoBehaviour
{
    private readonly List<Vector2Int> ranges = new List<Vector2Int>();

    private TMP_Text text;
    private string cachedSource;
    private bool hasLink;

    private void Awake()
    {
        text = GetComponent<TMP_Text>();
    }

    private void LateUpdate()
    {
        string source = text.text;

        // Daftar huruf yang diwarnai hanya disusun ulang saat isi teks berubah.
        if (!ReferenceEquals(source, cachedSource))
        {
            cachedSource = source;
            hasLink = !string.IsNullOrEmpty(source) && source.Contains("<link=\"" + GateRarityStyle.RainbowLinkId + "\">");

            if (hasLink)
            {
                text.ForceMeshUpdate();
                CollectRanges();
            }
        }

        if (!hasLink || ranges.Count == 0)
            return;

        // Mesh dibuat ulang TMP saat layout berubah, warna lalu ditimpa lagi di bawah.
        if (text.havePropertiesChanged)
            text.ForceMeshUpdate();

        TMP_TextInfo info = text.textInfo;
        int letter = 0;

        foreach (Vector2Int range in ranges)
        {
            for (int i = range.x; i < range.y && i < info.characterCount; i++)
            {
                TMP_CharacterInfo character = info.characterInfo[i];

                if (!character.isVisible)
                    continue;

                Color32 color = GateRarityStyle.SigmaColor(-GateRarityStyle.LetterOffset(letter++));
                color.a = character.color.a;

                Color32[] colors = info.meshInfo[character.materialReferenceIndex].colors32;
                int v = character.vertexIndex;

                colors[v] = color;
                colors[v + 1] = color;
                colors[v + 2] = color;
                colors[v + 3] = color;
            }
        }

        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private void CollectRanges()
    {
        ranges.Clear();

        TMP_TextInfo info = text.textInfo;

        for (int i = 0; i < info.linkCount; i++)
        {
            TMP_LinkInfo link = info.linkInfo[i];

            if (link.GetLinkID() != GateRarityStyle.RainbowLinkId)
                continue;

            ranges.Add(new Vector2Int(link.linkTextfirstCharacterIndex, link.linkTextfirstCharacterIndex + link.linkTextLength));
        }
    }
}
