using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Notifier.Services
{
    public class ReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<ReleaseAsset> Assets { get; set; } = new();
    }

    public class ReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }

    public static class UpdateManager
    {
        public static async Task<ReleaseInfo?> GetLatestReleaseAsync()
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Notifier-App-Updater");

            try
            {
                var response = await client.GetAsync("https://api.github.com/repos/ShadowAmitendu/Notifier/releases/latest");
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<ReleaseInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        public static async Task DownloadFileWithProgressAsync(
            string url, 
            string destinationPath, 
            IProgress<double> progress, 
            CancellationToken cancellationToken)
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            var totalRead = 0L;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                totalRead += read;

                if (totalBytes != -1)
                {
                    var percentage = ((double)totalRead / totalBytes) * 100.0;
                    progress.Report(percentage);
                }
            }
        }

        public static void TriggerUpdateInstallation(string cerPath, string msixPath)
        {
            string tempDir = Path.GetDirectoryName(msixPath) ?? Path.GetTempPath();
            string scriptPath = Path.Combine(tempDir, "install_update.ps1");

            string psScript = $@"
$cerPath = '{cerPath}'
$msixPath = '{msixPath}'

# 1. Import Certificate (triggers UAC)
Start-Process powershell -ArgumentList ""-NoProfile -ExecutionPolicy Bypass -Command `""Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\Root; Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople'`"""" -Verb RunAs -Wait

# 2. Wait, Close App, and Install MSIX
Start-Sleep -Seconds 1
Stop-Process -Name Notifier -Force -ErrorAction SilentlyContinue
Add-AppxPackage -Path $msixPath
";

            File.WriteAllText(scriptPath, psScript);

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(startInfo);
        }
    }
}
