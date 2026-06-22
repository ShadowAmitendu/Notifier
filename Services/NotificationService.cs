using System.Windows.Forms;

namespace Notifier.Services
{
    public class NotificationService
    {
        private readonly NotifyIcon _notifyIcon;

        public NotificationService(NotifyIcon notifyIcon)
        {
            _notifyIcon = notifyIcon;
        }

        public void ShowStartup(int siteCount)
        {
            string message = siteCount == 1 
                ? "Monitoring 1 site for updates." 
                : $"Monitoring {siteCount} sites for updates.";
            
            _notifyIcon.ShowBalloonTip(
                3000, 
                "Site Notifier Started", 
                message, 
                ToolTipIcon.Info
            );
        }

        public void ShowUpdate(string siteName, string url)
        {
            try
            {
                string escapedSiteName = System.Security.SecurityElement.Escape(siteName);
                string escapedUrl = System.Security.SecurityElement.Escape(url);

                string toastXml = $@"
<toast launch=""{escapedUrl}"">
  <visual>
    <binding template=""ToastGeneric"">
      <text>Site Updated!</text>
      <text>Changes detected on {escapedSiteName}.</text>
      <text>{escapedUrl}</text>
    </binding>
  </visual>
  <actions>
    <action content=""Visit Site"" arguments=""{escapedUrl}"" activationType=""protocol""/>
    <action content=""Dismiss"" arguments=""dismiss"" activationType=""background""/>
  </actions>
</toast>";

                var xmlDoc = new Windows.Data.Xml.Dom.XmlDocument();
                xmlDoc.LoadXml(toastXml);
                var toast = new Windows.UI.Notifications.ToastNotification(xmlDoc);
                Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to send native toast: {ex.Message}");
                // Fallback to balloon tip with warning (alert) icon
                _notifyIcon.ShowBalloonTip(
                    5000,
                    "Site Updated!",
                    $"Changes detected on {siteName}.\nClick to visit: {url}",
                    ToolTipIcon.Warning
                );
            }
        }

        public void ShowError(string siteName, string error)
        {
            _notifyIcon.ShowBalloonTip(
                5000,
                $"Error checking {siteName}",
                error,
                ToolTipIcon.Warning
            );
        }

        private static Windows.Media.Playback.MediaPlayer? _mediaPlayer;

        public static void PlaySound(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

            try
            {
                if (_mediaPlayer == null)
                {
                    _mediaPlayer = new Windows.Media.Playback.MediaPlayer();
                }
                _mediaPlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new System.Uri(filePath));
                _mediaPlayer.Play();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to play sound: {ex.Message}");
            }
        }
    }
}
