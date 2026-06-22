namespace Notifier.Models
{
    public class AppSettings
    {
        public int CheckIntervalMinutes { get; set; } = 60;
        public int MaxJitterSeconds { get; set; } = 30;
        public bool RunAtStartup { get; set; } = false;
        public bool IsMonitoringEnabled { get; set; } = true;
        public string AppTheme { get; set; } = "Default";
        public bool PlaySoundOnUpdate { get; set; } = false;
        public string CustomSoundPath { get; set; } = "";
        public bool AutoPruneLogs { get; set; } = false;
        public int PruneLogsDays { get; set; } = 30;
    }
}
