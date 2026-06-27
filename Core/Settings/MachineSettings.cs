using System.Text.Json;

namespace SwiftList.Core;

public class MachineSettings
{
    public List<string> LocalDrives { get; set; } = new();

    public static string SettingsPath => Path.Combine(Logger.SharedDataDir, "machine-settings.json");

    public static MachineSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new MachineSettings();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<MachineSettings>(json) ?? new MachineSettings();
            settings.LocalDrives = settings.LocalDrives
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return settings;
        }
        catch (Exception ex)
        {
            Logger.Log($"[MachineSettings] Failed to load settings: {ex.Message}", LogLevel.Error);
            return new MachineSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Logger.SharedDataDir);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, options));
    }
}
