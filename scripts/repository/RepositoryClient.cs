using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Authenticated API adapter for repository browsing and integrity-checked package downloads.
/// The UI supplies the signed-in player's short-lived bearer token; this class never stores it.
/// </summary>
public sealed class RepositoryClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient http;
    private readonly Func<CancellationToken, Task<string?>> getAccessToken;

    public RepositoryClient(Uri apiBaseUri, Func<CancellationToken, Task<string?>> tokenProvider, HttpMessageHandler? handler = null)
    {
        if (apiBaseUri is null || !apiBaseUri.IsAbsoluteUri)
            throw new ArgumentException("Repository API base URL must be absolute.", nameof(apiBaseUri));

        getAccessToken = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = apiBaseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? apiBaseUri
            : new Uri(apiBaseUri.AbsoluteUri + "/", UriKind.Absolute);
        http.Timeout = TimeSpan.FromMinutes(3);
    }

    public static Uri GetConfiguredApiBaseUri()
    {
        string configured = Environment.GetEnvironmentVariable("RHYTHIA_REPOSITORY_API") ?? "http://localhost:5080";
        if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("RHYTHIA_REPOSITORY_API must be an absolute HTTP(S) URL.");
        return uri;
    }

    public async Task<RepositoryPage<RepositoryChart>> BrowseAsync(
        string? query = null,
        string? creator = null,
        string? difficulty = null,
        IEnumerable<string>? tags = null,
        string sort = "updated",
        int page = 1,
        int pageSize = 24,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>
        {
            "sort=" + Uri.EscapeDataString(sort),
            "page=" + Math.Max(1, page),
            "pageSize=" + Math.Clamp(pageSize, 1, 100)
        };
        AddParameter(parameters, "q", query);
        AddParameter(parameters, "creator", creator);
        AddParameter(parameters, "difficulty", difficulty);
        if (tags is not null)
        {
            foreach (string tag in tags)
                AddParameter(parameters, "tag", tag);
        }

        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, "v1/charts?" + string.Join("&", parameters), cancellationToken);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RepositoryPage<RepositoryChart>>(JsonOptions, cancellationToken)
            ?? new RepositoryPage<RepositoryChart>();
    }

    public async Task<RepositoryChart?> GetChartAsync(string chartId, CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, $"v1/charts/{Uri.EscapeDataString(chartId)}", cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RepositoryChart>(JsonOptions, cancellationToken);
    }

    public async Task<string> DownloadVerifiedPackageAsync(
        RepositoryChart chart,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (chart is null) throw new ArgumentNullException(nameof(chart));
        if (string.IsNullOrWhiteSpace(chart.Sha256) || chart.Sha256.Length != 64)
            throw new InvalidDataException("Repository response did not include a valid SHA-256 checksum.");

        string relative = $"v1/charts/{Uri.EscapeDataString(chart.ChartId)}/versions/{Uri.EscapeDataString(chart.VersionId)}/download";
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, relative, cancellationToken);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        string? responseHash = response.Headers.TryGetValues("X-Chart-SHA256", out var hashes) ? System.Linq.Enumerable.FirstOrDefault(hashes) : null;
        if (!string.Equals(responseHash, chart.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Download checksum metadata did not match the selected chart version.");

        Directory.CreateDirectory(destinationDirectory);
        string safeName = chart.ChartId + "-" + chart.VersionId + ".zip";
        string finalPath = Path.Combine(destinationDirectory, safeName);
        string tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    total += read;
                    if (total > 536870912)
                        throw new InvalidDataException("Downloaded package exceeds the client size limit.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                string actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actualHash, chart.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded package failed SHA-256 verification.");
            }

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tempPath, finalPath);
            return finalPath;
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Downloads and verifies the repository envelope, then imports its native Rhythia .phxm chart file.
    /// Non-.phxm formats remain downloadable but are not auto-installed by this MVP adapter.
    /// </summary>
    public async Task InstallPhxmPackageAsync(
        RepositoryChart chart,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        string packagePath = await DownloadVerifiedPackageAsync(chart, destinationDirectory, cancellationToken);
        string extractedPath = Path.Combine(destinationDirectory, "install-" + Guid.NewGuid().ToString("N") + ".phxm");
        try
        {
            await ExtractChartFileAsync(packagePath, extractedPath, cancellationToken);
            await MapParser.BulkImport([extractedPath], notify: true);
        }
        finally
        {
            if (File.Exists(extractedPath)) File.Delete(extractedPath);
        }
    }

    private static async Task ExtractChartFileAsync(string packagePath, string destinationPath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Repository package is missing manifest.json.");
        await using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<RepositoryPackageManifest>(
            manifestStream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Repository package manifest is invalid.");

        string chartPath = manifest.ChartDataPath ?? "";
        if (string.IsNullOrWhiteSpace(chartPath) || chartPath.StartsWith("/", StringComparison.Ordinal) ||
            chartPath.Contains('\\') || chartPath.Contains(':') ||
            System.Linq.Enumerable.Any(chartPath.Split('/'), part => part is "" or "." or ".."))
            throw new InvalidDataException("Repository package manifest contains an unsafe chartDataPath.");

        string extension = Path.GetExtension(chartPath);
        if (!string.Equals(extension, ".phxm", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Automatic in-game installation currently supports .phxm packages only.");

        var chartEntry = archive.GetEntry(chartPath)
            ?? throw new InvalidDataException("The chartDataPath entry is missing from the package.");
        if (chartEntry.Length <= 0 || chartEntry.Length > 268435456)
            throw new InvalidDataException("The chart data file is outside the supported size limit.");

        await using var source = chartEntry.Open();
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(destination, 81920, cancellationToken);
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var token = await getAccessToken(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new UnauthorizedAccessException("Sign in is required to browse or download repository charts.");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static void AddParameter(List<string> parameters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parameters.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));
    }

    public void Dispose() => http.Dispose();
}

internal sealed class RepositoryPackageManifest
{
    public string? ChartDataPath { get; set; }
}

public sealed class RepositoryPage<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
}

public sealed class RepositoryChart
{
    public string ChartId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Creator { get; set; } = "";
    public string Description { get; set; } = "";
    public string Difficulty { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public string VersionId { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
