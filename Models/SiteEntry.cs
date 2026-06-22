using System;
using System.Text.Json.Serialization;

namespace Notifier.Models
{
    public enum DiffMode
    {
        FullPage,
        CssSelector,
        DomDiff,
        Both
    }

    public class SiteEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DiffMode Mode { get; set; } = DiffMode.FullPage;
        public string Selector { get; set; } = string.Empty;
        public string LastContentHash { get; set; } = string.Empty;
        public string LastContent { get; set; } = string.Empty;
        public DateTime? LastChecked { get; set; }

        // Per-Site Custom Check Intervals
        public bool UseCustomInterval { get; set; } = false;
        public int CustomIntervalMinutes { get; set; } = 60;
        public DateTime? NextCheck { get; set; }

        // Advanced HTTP options
        public string HttpMethod { get; set; } = "GET";
        public string CustomHeaders { get; set; } = string.Empty;
        public string CustomCookies { get; set; } = string.Empty;
        public string RequestBody { get; set; } = string.Empty;

        // Helper properties for XAML data binding
        [JsonIgnore]
        public string DisplayMode => Mode == DiffMode.FullPage 
            ? "Text content" 
            : (Mode == DiffMode.DomDiff ? "DOM / HTML" : (Mode == DiffMode.Both ? "Both (Text & HTML)" : $"Selector: {Selector}"));

        [JsonIgnore]
        public string DisplayLastChecked => LastChecked.HasValue 
            ? $"Checked: {LastChecked.Value:g}" 
            : "Checked: Never";

        [JsonIgnore]
        public string DisplayInterval => UseCustomInterval
            ? (CustomIntervalMinutes >= 60 && CustomIntervalMinutes % 60 == 0 
                ? $"Interval: {CustomIntervalMinutes / 60}h" 
                : $"Interval: {CustomIntervalMinutes}m")
            : "Interval: Global";

        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility SelectorVisibility => 
            (Mode == DiffMode.CssSelector && !string.IsNullOrEmpty(Selector)) 
                ? Microsoft.UI.Xaml.Visibility.Visible 
                : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
