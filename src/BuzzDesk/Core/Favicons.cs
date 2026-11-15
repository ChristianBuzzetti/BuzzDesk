using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace BuzzDesk;

/// <summary>
/// Fetches a link's page title and icon from the link's own site (nothing else is contacted, and no Windows
/// credentials are sent). Only http(s). Icons are PNG, ICO, GIF or JPEG up to 256 KB; anything else is ignored.
/// </summary>
public static partial class Favicons
{
    static readonly HttpClient http = CreateClient();
    const int MaxPage = 512 * 1024, MaxIcon = 256 * 1024;

    static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseDefaultCredentials = false, AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) BuzzDesk");
        return c;
    }

    public sealed record Result(string? Title, byte[]? Icon);

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex TitleRx();
    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex LinkTagRx();
    [GeneratedRegex(@"\b(rel|href|type|sizes)\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase)] private static partial Regex AttrRx();

    public static async Task<Result> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var page) || page.Scheme is not ("http" or "https")) return new(null, null);
        string? title = null;
        var candidates = new List<Uri>();
        try
        {
            using var resp = await http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.IsSuccessStatusCode && (resp.Content.Headers.ContentType?.MediaType ?? "").Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                string html = await ReadLimitedAsync(resp, MaxPage, ct);
                var baseUri = resp.RequestMessage?.RequestUri ?? page;
                if (TitleRx().Match(html) is { Success: true } t)
                    title = System.Net.WebUtility.HtmlDecode(Regex.Replace(t.Groups[1].Value, @"\s+", " ")).Trim();
                foreach (Match tag in LinkTagRx().Matches(html))
                {
                    var attrs = AttrRx().Matches(tag.Value).ToDictionary(m => m.Groups[1].Value.ToLowerInvariant(), m => m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value);
                    if (!attrs.TryGetValue("rel", out var rel) || !rel.Contains("icon", StringComparison.OrdinalIgnoreCase) || !attrs.TryGetValue("href", out var href)) continue;
                    if (href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || attrs.GetValueOrDefault("type", "").Contains("svg")) continue;   // WPF can't draw SVG
                    if (Uri.TryCreate(baseUri, System.Net.WebUtility.HtmlDecode(href), out var icon)) candidates.Add(icon);
                }
                // Small PNGs first (crisp at 16-32 px), then the rest in page order.
                candidates = candidates.OrderBy(u => u.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException) { }
        candidates.Add(new Uri(page, "/favicon.ico"));
        foreach (var icon in candidates.Distinct().Take(4))
        {
            if (icon.Scheme is not ("http" or "https")) continue;
            try
            {
                using var resp = await http.GetAsync(icon, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > MaxIcon) continue;
                var bytes = await ReadBytesLimitedAsync(resp, MaxIcon, ct);
                if (bytes != null && IsImage(bytes)) return new(title, bytes);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException) { }
        }
        return new(title, null);
    }

    /// <summary>PNG, ICO, GIF or JPEG by their magic numbers.</summary>
    public static bool IsImage(byte[] b) =>
        b.Length > 8 && (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ||
                         b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0 ||
                         b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 ||
                         b[0] == 0xFF && b[1] == 0xD8);

    static async Task<string> ReadLimitedAsync(HttpResponseMessage resp, int max, CancellationToken ct)
    {
        var bytes = await ReadBytesLimitedAsync(resp, max, ct, truncate: true) ?? [];
        var charset = resp.Content.Headers.ContentType?.CharSet;
        Encoding enc;
        try { enc = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { enc = Encoding.UTF8; }
        return enc.GetString(bytes);
    }

    static async Task<byte[]?> ReadBytesLimitedAsync(HttpResponseMessage resp, int max, CancellationToken ct, bool truncate = false)
    {
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while ((n = await s.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > max) { if (truncate) { ms.Write(buffer, 0, (int)(max - ms.Length)); break; } return null; }
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }
}
