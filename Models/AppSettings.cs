namespace Notifier.Models
{
    public class AppSettings
    {
        public int CheckIntervalMinutes { get; set; } = 60;
        public int MaxJitterSeconds { get; set; } = 30;
        public bool RunAtStartup { get; set; } = false;
        public bool IsMonitoringEnabled { get; set; } = true;
    }
}
