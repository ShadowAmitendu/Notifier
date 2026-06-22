using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Notifier.Models;

namespace Notifier.Services
{
    public class ConfigData
    {
        public List<SiteEntry> Sites { get; set; } = new();
        public AppSettings Settings { get; set; } = new();
    }

    public static class ConfigManager
    {
        private static readonly string AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SiteNotifier"
        );
        private static readonly string ConfigPath = Path.Combine(AppDir, "config.json");

        public static ConfigData Load()
        {
            try
            {
                if (!Directory.Exists(AppDir))
                {
                    Directory.CreateDirectory(AppDir);
                }

                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var data = JsonSerializer.Deserialize<ConfigData>(json);
                    if (data != null)
                    {
                        return data;
                    }
                }
            }
            catch
            {
                // Fallback to default
            }

            return new ConfigData();
        }

        public static void Save(ConfigData data)
        {
            try
            {
                if (!Directory.Exists(AppDir))
                {
                    Directory.CreateDirectory(AppDir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
