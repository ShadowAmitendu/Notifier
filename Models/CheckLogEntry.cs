using System;

namespace Notifier.Models
{
    public class CheckLogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string SiteId { get; set; } = string.Empty;
        public string SiteName { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Success", "Changed", "Error"
        public string Details { get; set; } = string.Empty; // Description or error details

        // Helper properties for XAML data binding
        public string DisplayTime => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

        public string StatusColor => Status switch
        {
            "Changed" => "#EF4444", // Red-500
            "Error" => "#F59E0B",   // Amber-500
            _ => "#10B981"          // Green-500 (Success)
        };

        public string StatusGlyph => Status switch
        {
            "Changed" => "\uE783", // Warning icon
            "Error" => "\uE7BA",   // Error/Alert info icon
            _ => "\uE73E"          // Checkmark icon
        };
    }
}
