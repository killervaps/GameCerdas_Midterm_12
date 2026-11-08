using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Teks petunjuk yang ditulis langsung di scene (mis. "Tahan (E)" melayang di depan gate) ikut skema kontrol.
// Setiap scene dimuat, semua teks TMP yang memuat "(E)" diikat ke ControlHintLabel:
//   keyboard  "Tahan (E)"     gamepad  "Tahan (RT)" (tombol Interaksi saat ini, berwarna)
public static class WorldHintTexts
{
    private const string KeyboardToken = "(E)";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        BindAll();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindAll();
    }

    private static void BindAll()
    {
        TMP_Text[] texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (TMP_Text text in texts)
        {
            // Teks UI di Canvas sudah diurus layar masing-masing.
            if (text is TextMeshProUGUI || text.GetComponent<ControlHintLabel>() != null)
                continue;

            string template = text.text;

            if (string.IsNullOrEmpty(template) || !template.Contains(KeyboardToken))
                continue;

            ControlHintLabel.Bind(text, () => ControlHints.IsGamepad
                ? template.Replace(KeyboardToken, ControlHints.PadGlyph(PadAction.Interact))
                : template);
        }
    }
}
