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

Write-Host ""Site Notifier Update Installer"" -ForegroundColor Cyan
Write-Host ""============================="" -ForegroundColor Cyan
Write-Host """"

Write-Host ""1. Installing certificate... (Please approve UAC prompts if they appear)"" -ForegroundColor Yellow
$cert1 = Start-Process certutil.exe -ArgumentList ""-addstore -f root `""$cerPath`"""" -Verb RunAs -Wait -PassThru
$cert2 = Start-Process certutil.exe -ArgumentList ""-addstore -f TrustedPeople `""$cerPath`"""" -Verb RunAs -Wait -PassThru

Write-Host ""2. Closing running application instances..."" -ForegroundColor Yellow
Start-Sleep -Seconds 1
Stop-Process -Name Notifier -Force -ErrorAction SilentlyContinue

Write-Host ""3. Installing the updated app package..."" -ForegroundColor Yellow
try {{
    Add-AppxPackage -Path $msixPath -ErrorAction Stop
    Write-Host ""Update completed successfully!"" -ForegroundColor Green
    Start-Sleep -Seconds 2
}} catch {{
    Write-Host """"
    Write-Host ""Installation failed!"" -ForegroundColor Red
    Write-Error $_.Exception.Message
    Write-Host """"
    Read-Host ""Press Enter to close this window...""
}}
";

            File.WriteAllText(scriptPath, psScript);

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false // Show the console window to the user
            };
            System.Diagnostics.Process.Start(startInfo);
        }
    }
}
