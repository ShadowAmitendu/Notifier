using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Notifier.Models;

namespace Notifier.Services
{
    public static class LogManager
    {
        private static readonly string AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SiteNotifier"
        );
        private static readonly string LogPath = Path.Combine(AppDir, "history_logs.json");
        private const int MaxLogEntries = 200;

        private static readonly object _lock = new object();

        public static List<CheckLogEntry> LoadLogs()
        {
            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(AppDir))
                    {
                        Directory.CreateDirectory(AppDir);
                    }

                    if (File.Exists(LogPath))
                    {
                        string json = File.ReadAllText(LogPath);
                        var logs = JsonSerializer.Deserialize<List<CheckLogEntry>>(json);
                        if (logs != null)
                        {
                            return logs;
                        }
                    }
                }
                catch
                {
                    // Fallback to empty list
                }
                return new List<CheckLogEntry>();
            }
        }

        public static void AddLog(string siteId, string siteName, string url, string status, string details)
        {
            lock (_lock)
            {
                try
                {
                    var logs = LoadLogs();
                    var newEntry = new CheckLogEntry
                    {
                        SiteId = siteId,
                        SiteName = siteName,
                        Url = url,
                        Status = status,
                        Details = details,
                        Timestamp = DateTime.Now
                    };

                    logs.Insert(0, newEntry); // Prepend so latest shows first

                    // Cap size
                    if (logs.Count > MaxLogEntries)
                    {
                        logs = logs.GetRange(0, MaxLogEntries);
                    }

                    if (!Directory.Exists(AppDir))
                    {
                        Directory.CreateDirectory(AppDir);
                    }

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(logs, options);
                    File.WriteAllText(LogPath, json);
                }
                catch
                {
                    // Ignore failures to avoid crashing background operations
                }
            }
        }

        public static void ClearLogs()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(LogPath))
                    {
                        File.Delete(LogPath);
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }
    }
}
