using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Notifier.Models;

namespace Notifier.Services
{
    public class CheckResult
    {
        public bool HasChanged { get; set; }
        public string NewHash { get; set; } = string.Empty;
        public string NewContent { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool IsError => !string.IsNullOrEmpty(ErrorMessage);
    }

    public static class SiteChecker
    {
        private static readonly HttpClient _httpClient;

        static SiteChecker()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            _httpClient.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public static async Task<CheckResult> CheckSiteAsync(SiteEntry site)
        {
            var result = new CheckResult();
            try
            {
                string snapshotsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SiteNotifier",
                    "Snapshots"
                );
                if (!Directory.Exists(snapshotsDir))
                {
                    Directory.CreateDirectory(snapshotsDir);
                }
                string snapshotPath = Path.Combine(snapshotsDir, $"{site.Id}.html");

                // Fetch fresh live HTML (no-cache headers are set)
                string html = await _httpClient.GetStringAsync(site.Url);

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);

                string extractedText = string.Empty;

                if (site.Mode == DiffMode.FullPage)
                {
                    RemoveNoise(doc.DocumentNode);
                    extractedText = doc.DocumentNode.InnerText;
                }
                else if (site.Mode == DiffMode.DomDiff)
                {
                    RemoveNoise(doc.DocumentNode);
                    extractedText = doc.DocumentNode.OuterHtml;
                }
                else if (site.Mode == DiffMode.Both)
                {
                    RemoveNoise(doc.DocumentNode);
                    extractedText = doc.DocumentNode.InnerText + "\n---DOM_DIFF---\n" + doc.DocumentNode.OuterHtml;
                }
                else if (site.Mode == DiffMode.CssSelector)
                {
                    string xpath = CssToXPath(site.Selector);
                    var node = doc.DocumentNode.SelectSingleNode(xpath);
                    if (node == null)
                    {
                        result.ErrorMessage = $"Element not found matching selector/XPath: {site.Selector}";
                        return result;
                    }
                    RemoveNoise(node);
                    extractedText = node.InnerText;
                }

                extractedText = CleanWhitespace(extractedText);
                string hash = ComputeHash(extractedText);

                result.NewContent = extractedText;
                result.NewHash = hash;

                bool snapshotExists = File.Exists(snapshotPath);
                if (!snapshotExists)
                {
                    // First time, write snapshot file and mark as not changed
                    await File.WriteAllTextAsync(snapshotPath, html);
                    result.HasChanged = false;
                }
                else
                {
                    // Read old HTML snapshot
                    string oldHtml = await File.ReadAllTextAsync(snapshotPath);
                    var oldDoc = new HtmlAgilityPack.HtmlDocument();
                    oldDoc.LoadHtml(oldHtml);

                    string oldExtractedText = string.Empty;
                    if (site.Mode == DiffMode.FullPage)
                    {
                        RemoveNoise(oldDoc.DocumentNode);
                        oldExtractedText = oldDoc.DocumentNode.InnerText;
                    }
                    else if (site.Mode == DiffMode.DomDiff)
                    {
                        RemoveNoise(oldDoc.DocumentNode);
                        oldExtractedText = oldDoc.DocumentNode.OuterHtml;
                    }
                    else if (site.Mode == DiffMode.Both)
                    {
                        RemoveNoise(oldDoc.DocumentNode);
                        oldExtractedText = oldDoc.DocumentNode.InnerText + "\n---DOM_DIFF---\n" + oldDoc.DocumentNode.OuterHtml;
                    }
                    else if (site.Mode == DiffMode.CssSelector)
                    {
                        string xpath = CssToXPath(site.Selector);
                        var oldNode = oldDoc.DocumentNode.SelectSingleNode(xpath);
                        if (oldNode != null)
                        {
                            RemoveNoise(oldNode);
                            oldExtractedText = oldNode.InnerText;
                        }
                    }

                    oldExtractedText = CleanWhitespace(oldExtractedText);
                    string oldHash = ComputeHash(oldExtractedText);

                    // Compare old and new text
                    result.HasChanged = (oldHash != hash || oldExtractedText != extractedText);

                    if (result.HasChanged)
                    {
                        // Overwrite snapshot file with the updated HTML
                        await File.WriteAllTextAsync(snapshotPath, html);
                    }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static void RemoveNoise(HtmlNode node)
        {
            // Remove scripts, styles, and comments inside this node
            var nodesToRemove = node.SelectNodes(".//script | .//style | .//comment()");
            if (nodesToRemove != null)
            {
                foreach (var n in nodesToRemove)
                {
                    n.Remove();
                }
            }
        }

        private static string CleanWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            
            // Decode HTML entities
            text = HtmlEntity.DeEntitize(text);

            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    sb.AppendLine(trimmed);
                }
            }
            return sb.ToString().Trim();
        }

        private static string ComputeHash(string input)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        public static string CssToXPath(string cssSelector)
        {
            if (string.IsNullOrWhiteSpace(cssSelector)) return "//*";

            cssSelector = cssSelector.Trim();

            // If it already looks like XPath, return it
            if (cssSelector.StartsWith("/") || cssSelector.StartsWith("./") || cssSelector.StartsWith("("))
            {
                return cssSelector;
            }

            // Split by space to support descendant combinators (e.g. "div .content")
            var parts = cssSelector.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var xpathParts = new List<string>();

            foreach (var part in parts)
            {
                string elementXPath;
                if (part.StartsWith("#"))
                {
                    elementXPath = $"*[@id='{part.Substring(1)}']";
                }
                else if (part.StartsWith("."))
                {
                    string className = part.Substring(1);
                    elementXPath = $"*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]";
                }
                else if (part.Contains("."))
                {
                    var dotParts = part.Split('.');
                    string tag = string.IsNullOrEmpty(dotParts[0]) ? "*" : dotParts[0];
                    string className = dotParts[1];
                    elementXPath = $"{tag}[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]";
                }
                else if (part.Contains("#"))
                {
                    var hashParts = part.Split('#');
                    string tag = string.IsNullOrEmpty(hashParts[0]) ? "*" : hashParts[0];
                    string idName = hashParts[1];
                    elementXPath = $"{tag}[@id='{idName}']";
                }
                else
                {
                    elementXPath = part;
                }

                xpathParts.Add(elementXPath);
            }

            return "//" + string.Join("//", xpathParts);
        }
    }
}
