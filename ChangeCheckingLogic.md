# How Notifier Detects Website Changes

The application checks for updates on monitored websites by comparing content snapshots over time. It performs this check inside the [SiteChecker](file:///e:/Notifier/Services/SiteChecker.cs) service. Instead of comparing raw HTML (which changes frequently with dynamic ads, scripts, timestamps, and formatting), it filters the content to isolate and hash the actual user-facing text.

Here is the step-by-step breakdown of how the difference check works:

---

## Step 1: Fetching the Raw HTML
When a check runs, the app uses a configured `HttpClient` to send a standard HTTP `GET` request to the target site.
- It includes a modern User-Agent header so web servers identify it as a valid browser request rather than a bot.
- It enables automatic decompression (Gzip/Deflate) to download the content as fast as possible.

```csharp
string html = await _httpClient.GetStringAsync(site.Url);
```

---

## Step 2: Parsing the Document Object Model (DOM)
The downloaded HTML is loaded into the **HtmlAgilityPack** parser, which converts the raw text stream into a structured DOM tree. This allows the program to travers elements, select sections, and remove noise.

```csharp
var doc = new HtmlAgilityPack.HtmlDocument();
doc.LoadHtml(html);
```

---

## Step 3: Noise & Metadata Removal
Web pages contain markup and instructions that do not represent visible text content (e.g. tracking scripts, CSS style sheets, and HTML comments). To avoid false alarms, the program strips these elements from the DOM:
- **Scripts**: `<script>` blocks (JavaScript/tracking code).
- **Styles**: `<style>` blocks (CSS styling).
- **Comments**: `<!-- comments -->` (developer comments).

```csharp
// Selects and removes script, style, and comment nodes from the DOM tree
var nodesToRemove = node.SelectNodes(".//script | .//style | .//comment()");
if (nodesToRemove != null)
{
    foreach (var n in nodesToRemove)
    {
        n.Remove();
    }
}
```

---

## Step 4: Text Extraction & Normalization
Once the DOM is clean, the program extracts the raw readable text (`InnerText`) and normalizes it:
1. **HTML Entity Decoding**: Converts HTML-encoded symbols (like `&amp;`, `&quot;`, `&#8217;`) back to their standard plain-text characters (`&`, `"`, `'`).
2. **Whitespace Normalization**: Removes trailing/leading line spaces, replaces multiple empty lines with a single newline, and trims the entire document text.

```csharp
// Normalization splits by lines, trims each line, and skips empty ones
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
```

---

## Step 5: Cryptographic Hashing (SHA-256)
Instead of storing megabytes of raw webpage text on disk inside `config.json`, the app converts the normalized text into a secure **SHA-256 hash** (a unique 64-character hexadecimal fingerprint representing the content).
- If even a single letter, number, or word changes, the resulting SHA-256 hash will be completely different.
- If the page content is exactly the same, the hash remains identical.

```csharp
byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
var sb = new StringBuilder();
foreach (byte b in bytes)
{
    sb.Append(b.ToString("x2"));
}
return sb.ToString();
```

---

## Step 6: Comparison & Notification
Finally, the app compares the computed hash with the previously stored `LastContentHash` for that website inside your configurations:
- **First Check**: If `LastContentHash` is empty (newly added site), the new hash is stored without triggering an update.
- **Subsequent Checks**: If `LastContentHash` has a value, the app checks if `LastContentHash != NewHash`.
- **Change Detected**: If the hashes don't match, `HasChanged` is set to `true`, prompting the app to trigger a Windows toast notification and system tray alert.
- **Save State**: The new content hash is saved back to `config.json` to act as the baseline for the next check.

```csharp
result.HasChanged = !string.IsNullOrEmpty(site.LastContentHash) && site.LastContentHash != hash;
```
