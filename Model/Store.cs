using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClubEQ.Model;

public class Settings
{
    public string Input = "";   // device names, matched on start (indices change when devices come and go)
    public string Output = "";
    public string Preset = "Garage";
    public string CatcarBepInEx = ""; // the game's BepInEx folder, for "Import from CATCAR"
    public double Width = 1280, Height = 800;
    public bool Maximized;
}

// %APPDATA%\ClubEQ (or CLUBEQ_HOME): settings.json and Presets\<name>.json.
public static class Store
{
    public static readonly string Home = Environment.GetEnvironmentVariable("CLUBEQ_HOME") is { Length: > 0 } h ? h
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClubEQ");
    static string PresetDir => Path.Combine(Home, "Presets");

    public static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true, WriteIndented = true, Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson<T>(T x) => JsonSerializer.Serialize(x, Json);
    public static T FromJson<T>(string s) => JsonSerializer.Deserialize<T>(s, Json);

    public static Settings LoadSettings()
    {
        try { return FromJson<Settings>(File.ReadAllText(Path.Combine(Home, "settings.json"))) ?? new(); }
        catch { return new(); }
    }

    public static void SaveSettings(Settings s) => Write(Path.Combine(Home, "settings.json"), ToJson(s));

    // Saved presets, plus any built-in one that has no saved copy.
    public static List<Preset> LoadPresets()
    {
        var list = new List<Preset>();
        if (Directory.Exists(PresetDir))
            foreach (var f in Directory.GetFiles(PresetDir, "*.json").OrderBy(f => f))
                try { if (FromJson<Preset>(File.ReadAllText(f)) is { } p) list.Add(p); } catch { /* a broken file is skipped, not deleted */ }
        foreach (var b in Builtin.All())
            if (!list.Any(p => p.Name == b.Name)) list.Add(b);
        return list;
    }

    public static void SavePreset(Preset p) => Write(Path.Combine(PresetDir, FileName(p.Name)), ToJson(p));

    public static void DeletePreset(string name)
    {
        var f = Path.Combine(PresetDir, FileName(name));
        if (File.Exists(f)) File.Delete(f);
    }

    static string FileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name + ".json";
    }

    // Write to a temp file and swap it in, so a crash mid-write can't leave half a file.
    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", text);
        File.Move(path + ".tmp", path, true);
    }
}
