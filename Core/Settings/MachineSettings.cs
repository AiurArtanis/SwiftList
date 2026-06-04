using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SwiftList.Core
{
    public class MachineSettings
    {
        public List<string> EnabledLocalDrives { get; set; } = new();

        public static string SettingsPath => Path.Combine(Logger.SharedDataDir, "machine-settings.json");

        public static MachineSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new MachineSettings();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<MachineSettings>(json) ?? new MachineSettings();
            }
            catch (Exception ex)
            {
                Logger.Log($"[MachineSettings] Failed to load settings: {ex.Message}");
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
}
