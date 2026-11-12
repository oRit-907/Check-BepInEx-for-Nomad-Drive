namespace NomadModDownloader.Core;

/// <summary>Downloads (BepInEx, Unity's libraries).</summary>
public static class Net
{
    public const string Site = "https://nomaddrivemods.online/";

    public static async Task<string> DownloadFileAsync(HttpClient http, Uri url, string folder, string? preferredName, IProgress<double>? progress, CancellationToken ct)
    {
        // The Nexus edition downloads one thing only: Unity's libraries from BepInEx's server, after the player said yes.
        if (Edition.Nexus && !string.Equals(url.Host, "unity.bepinex.dev", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("This copy of Check BepInEx doesn't download files."));
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var name = resp.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"')
                   ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                   ?? Uri.UnescapeDataString(Path.GetFileName(url.AbsolutePath));
        if (string.IsNullOrWhiteSpace(name) || !Path.HasExtension(name)) name = (preferredName ?? "download") + ".zip";
        name = Fmt.SafeFileName(name);

        Directory.CreateDirectory(folder);
        var dest = Path.Combine(folder, name);
        var tmp = dest + ".part";
        var total = resp.Content.Headers.ContentLength ?? -1;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buf = new byte[81920];
            long done = 0; int read;
            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        File.Move(tmp, dest, true);
        progress?.Report(1);
        return dest;
    }
}

public static class Fmt
{
    /// <summary>For narrow columns: "4 Nov" / "11-04" this year, otherwise the full date.</summary>
    public static string ShortDate(DateTime d) =>
        d.Year != DateTime.Now.Year ? Date(d, shortYear: true) : L.IsEnglish ? d.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture) : d.ToString("MM-dd");

    public static string Num(double? d) => d?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>"4 Nov 2026" in English; "2026-11-04" in other languages (month names would otherwise stay English).</summary>
    public static string Date(DateTime d, bool shortYear = false) =>
        L.IsEnglish ? d.ToString(shortYear ? "d MMM yy" : "d MMM yyyy") : d.ToString(shortYear ? "yy-MM-dd" : "yyyy-MM-dd");

    public static string Size(long bytes) => bytes switch
    {
        <= 0 => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB"
    };

    public static string Count(long n) => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" : n >= 1000 ? $"{n / 1000.0:0.#}K" : n.ToString();

    public static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }))
            name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return string.IsNullOrEmpty(name) ? "mod" : name;
    }
}
