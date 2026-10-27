using System;
using UnityEngine;

// Volume pilihan pemain (menu Pengaturan), disimpan di PlayerPrefs supaya tetap ada saat game dibuka lagi.
//   Master  seluruh suara game, lewat AudioListener.volume
//   Music   pengali di atas Music Volume milik MusicManager
//   Sfx     pengali di atas SFX Volume milik SfxManager
// Nilai Inspector MusicManager/SfxManager tetap menjadi campuran dasar, nilai di sini dikalikan di atasnya.
public static class GameAudioSettings
{
    private const string MasterKey = "MathGate.Audio.Master";
    private const string MusicKey = "MathGate.Audio.Music";
    private const string SfxKey = "MathGate.Audio.Sfx";
    private const float DefaultVolume = 1f;

    private static bool loaded;
    private static float master = DefaultVolume;
    private static float music = DefaultVolume;
    private static float sfx = DefaultVolume;

    // Dikirim setiap kali salah satu volume berubah.
    public static event Action Changed;

    public static float Master
    {
        get
        {
            Load();
            return master;
        }
        set
        {
            Load();

            if (Set(ref master, MasterKey, value))
                AudioListener.volume = master;
        }
    }

    public static float Music
    {
        get
        {
            Load();
            return music;
        }
        set
        {
            Load();
            Set(ref music, MusicKey, value);
        }
    }

    public static float Sfx
    {
        get
        {
            Load();
            return sfx;
        }
        set
        {
            Load();
            Set(ref sfx, SfxKey, value);
        }
    }

    public static void ResetToDefault()
    {
        Master = DefaultVolume;
        Music = DefaultVolume;
        Sfx = DefaultVolume;
    }

    // Menulis PlayerPrefs ke disk. Dipanggil saat menu Pengaturan ditutup, bukan setiap slider bergeser.
    public static void Save()
    {
        PlayerPrefs.Save();
    }

    // Volume utama langsung diterapkan saat game mulai, sebelum ada yang membuka menu Pengaturan.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnStart()
    {
        loaded = false;
        Load();
    }

    private static void Load()
    {
        if (loaded)
            return;

        loaded = true;
        master = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, DefaultVolume));
        music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultVolume));
        sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, DefaultVolume));

        AudioListener.volume = master;
    }

    private static bool Set(ref float field, string key, float value)
    {
        value = Mathf.Clamp01(value);

        if (Mathf.Approximately(field, value))
            return false;

        field = value;
        PlayerPrefs.SetFloat(key, value);
        Changed?.Invoke();
        return true;
    }
}
