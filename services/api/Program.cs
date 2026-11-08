using System.IO.Compression;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var repositoryRoot = Path.GetFullPath(
    builder.Configuration["Repository:StorageRoot"] ?? "repository-data",
    builder.Environment.ContentRootPath);
var packagesRoot = Path.Combine(repositoryRoot, "packages");
var stagingRoot = Path.Combine(repositoryRoot, "staging");
Directory.CreateDirectory(packagesRoot);
Directory.CreateDirectory(stagingRoot);

var dbPath = Path.Combine(repositoryRoot, "repository.db");
var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, ForeignKeys = true }.ToString();
var maxPackageBytes = builder.Configuration.GetValue<long>("Repository:MaxPackageBytes", 268435456);
var maxArchiveEntries = builder.Configuration.GetValue<int>("Repository:MaxArchiveEntries", 2048);
var maxExpandedBytes = builder.Configuration.GetValue<long>("Repository:MaxExpandedBytes", 536870912);

builder.Services.AddSingleton(new RepositoryDatabase(connectionString));
builder.Services.AddSingleton(new PackageStore(packagesRoot, stagingRoot, maxPackageBytes, maxArchiveEntries, maxExpandedBytes));
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("api", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.AddFixedWindowLimiter("uploads", limiter =>
    {
        limiter.PermitLimit = 6;
        limiter.Window = TimeSpan.FromMinutes(10);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});

var authority = builder.Configuration["Authentication:Authority"];
var audience = builder.Configuration["Authentication:Audience"];
if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience)))
    throw new InvalidOperationException("Authentication:Authority and Authentication:Audience are required outside Development.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    if (!string.IsNullOrWhiteSpace(authority))
        options.Authority = authority;
    if (!string.IsNullOrWhiteSpace(audience))
        options.Audience = audience;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = !string.IsNullOrWhiteSpace(authority),
        ValidateAudience = !string.IsNullOrWhiteSpace(audience),
        ValidateLifetime = true,
        NameClaimType = "name",
        RoleClaimType = "roles"
    };
});

var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("configured-origins", policy =>
{
    if (origins.Length > 0)
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()
            .WithExposedHeaders("X-Chart-SHA256", "X-Chart-Version", "Content-Disposition");
}));
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("reviewer", policy => policy.RequireRole("reviewer", "maintainer"));
    options.AddPolicy("maintainer", policy => policy.RequireRole("maintainer"));
});

var app = builder.Build();
await app.Services.GetRequiredService<RepositoryDatabase>().InitializeAsync();
app.UseExceptionHandler();
app.UseRouting();
app.UseCors("configured-origins");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var v1 = app.MapGroup("/v1").RequireAuthorization().RequireRateLimiting("api");

v1.MapGet("/health", () => Results.Ok(new { status = "ok", repositoryMode = "local-filesystem" }))
    .AllowAnonymous();

v1.MapGet("/charts", async (string? q, string? title, string? creator, string? difficulty,
    string[]? tag, string? sort, int? page, int? pageSize, RepositoryDatabase db, CancellationToken ct) =>
{
    var result = await db.SearchChartsAsync(q ?? title, creator, difficulty, tag ?? [], sort, page ?? 1, pageSize ?? 24, ct);
    return Results.Ok(result);
});

v1.MapGet("/charts/{chartId}", async (string chartId, RepositoryDatabase db, CancellationToken ct) =>
{
    var chart = await db.GetChartAsync(chartId, ct);
    return chart is null ? Results.NotFound() : Results.Ok(chart);
});

v1.MapGet("/charts/{chartId}/versions/{versionId}", async (string chartId, string versionId, RepositoryDatabase db, CancellationToken ct) =>
{
    var version = await db.GetVersionAsync(chartId, versionId, ct);
    return version is null ? Results.NotFound() : Results.Ok(version);
});

v1.MapGet("/charts/{chartId}/versions/{versionId}/download", async (string chartId, string versionId,
    HttpContext context, RepositoryDatabase db, PackageStore store, CancellationToken ct) =>
{
    var version = await db.GetVersionAsync(chartId, versionId, ct);
    if (version is null) return Results.NotFound();
    var stream = store.OpenPackage(chartId, versionId);
    if (stream is null) return Results.NotFound();
    await db.RecordDownloadAsync(chartId, ct);
    context.Response.Headers["X-Chart-SHA256"] = version.Sha256;
    context.Response.Headers["X-Chart-Version"] = version.VersionId;
    return Results.File(stream, "application/zip", $"{chartId}-{versionId}.zip", enableRangeProcessing: true);
});

v1.MapPost("/uploads", async (HttpContext context, RepositoryDatabase db, PackageStore store, CancellationToken ct) =>
{
    var ownerId = UserId(context.User);
    if (ownerId is null) return Results.Unauthorized();
    var uploadId = Guid.NewGuid().ToString("N");
    var stagePath = store.CreateStage(uploadId);
    await db.CreateUploadAsync(uploadId, ownerId, stagePath, ct);
    return Results.Accepted($"/v1/uploads/{uploadId}", new { uploadId, uploadUrl = $"/v1/uploads/{uploadId}/package" });
}).RequireRateLimiting("uploads");

v1.MapPut("/uploads/{uploadId}/package", async (string uploadId, HttpRequest request, HttpContext context,
    RepositoryDatabase db, PackageStore store, CancellationToken ct) =>
{
    var ownerId = UserId(context.User);
    if (ownerId is null) return Results.Unauthorized();
    var upload = await db.GetUploadAsync(uploadId, ct);
    if (upload is null || upload.OwnerId != ownerId) return Results.NotFound();
    if (upload.Status != "created") return Results.Conflict(new { error = "Upload session is not open." });
    var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = maxPackageBytes;

    string stageFile;
    try
    {
        stageFile = await store.ReceivePackageAsync(upload.StagePath, request.Body, ct);
    }
    catch (InvalidDataException ex)
    {
        await db.FailUploadAsync(uploadId, ex.Message, ct);
        store.DeleteStage(upload.StagePath);
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    var validation = await store.ValidatePackageAsync(stageFile, ct);
    if (!validation.IsValid)
    {
        await db.FailUploadAsync(uploadId, validation.Error!, ct);
        store.DeleteStage(upload.StagePath);
        return Results.BadRequest(new { error = validation.Error });
    }

    var chartId = Guid.NewGuid().ToString("N");
    var versionId = "1";
    var packagePath = store.Publish(stageFile, chartId, versionId);
    var package = validation.Package!;
    await db.PublishChartAsync(chartId, versionId, ownerId, package, packagePath, ct);
    await db.CompleteUploadAsync(uploadId, chartId, ct);
    return Results.Created($"/v1/charts/{chartId}", new { chartId, versionId, package.Sha256, package.SizeBytes });
}).DisableAntiforgery().RequireRateLimiting("uploads");

v1.MapPost("/charts/{chartId}/reports", async (string chartId, ChartReportRequest request,
    HttpContext context, RepositoryDatabase db, CancellationToken ct) =>
{
    var reporterId = UserId(context.User);
    if (reporterId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000)
        return Results.BadRequest(new { error = "A report reason of 1–2000 characters is required." });
    var reportId = await db.ReportChartAsync(chartId, reporterId, request.Reason, ct);
    return reportId is null
        ? Results.NotFound()
        : Results.Accepted("/v1/moderation/reports", new { reportId, status = "open" });
});

v1.MapPost("/moderation/charts/{chartId}/takedown", async (string chartId, ModerationRequest request,
    HttpContext context, RepositoryDatabase db, CancellationToken ct) =>
{
    var moderatorId = UserId(context.User);
    if (moderatorId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Rationale) || request.Rationale.Length > 2000)
        return Results.BadRequest(new { error = "A moderation rationale of 1–2000 characters is required." });
    return await db.TakedownChartAsync(chartId, moderatorId, request.Rationale, ct)
        ? Results.Ok(new { chartId, status = "removed" })
        : Results.NotFound();
}).RequireAuthorization("maintainer");

v1.MapGet("/moderation/reports", async (int? page, int? pageSize, RepositoryDatabase db, CancellationToken ct) =>
    Results.Ok(await db.GetOpenReportsAsync(page ?? 1, pageSize ?? 24, ct)))
    .RequireAuthorization("maintainer");

v1.MapPost("/charts/{chartId}/ranked-nominations", async (string chartId, NominationRequest request,
    HttpContext context, RepositoryDatabase db, CancellationToken ct) =>
{
    var playerId = UserId(context.User);
    if (playerId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Rationale) || request.Rationale.Length > 2000)
        return Results.BadRequest(new { error = "A rationale of 1–2000 characters is required." });
    var nominationId = await db.NominateAsync(chartId, playerId, request.Rationale, ct);
    return nominationId is null ? Results.NotFound() : Results.Created($"/v1/ranked/reviews/{nominationId}", new { nominationId, status = "pending" });
});

v1.MapGet("/ranked/reviews", async (int? page, int? pageSize, RepositoryDatabase db, CancellationToken ct) =>
    Results.Ok(await db.GetReviewsAsync(page ?? 1, pageSize ?? 24, ct)))
    .RequireAuthorization("reviewer");

v1.MapPost("/ranked/reviews/{nominationId}/decision", async (string nominationId, ReviewDecisionRequest request,
    HttpContext context, RepositoryDatabase db, CancellationToken ct) =>
{
    var reviewerId = UserId(context.User);
    if (reviewerId is null) return Results.Unauthorized();
    if (request.Decision is not ("approved" or "rejected") || string.IsNullOrWhiteSpace(request.Rationale))
        return Results.BadRequest(new { error = "Decision must be approved or rejected and include a rationale." });
    var updated = await db.DecideReviewAsync(nominationId, reviewerId, request.Decision, request.Rationale, ct);
    return updated ? Results.Ok(new { nominationId, request.Decision }) : Results.NotFound();
}).RequireAuthorization("reviewer");

v1.MapPost("/ranked/results", () => Results.Problem(
    title: "Ranked scoring is not configured",
    detail: "No scoring algorithm has been approved for this deployment. Results are not accepted until a versioned server-side formula is configured.",
    statusCode: StatusCodes.Status503ServiceUnavailable));

v1.MapGet("/leaderboards/rp", async (int? page, int? pageSize, RepositoryDatabase db, CancellationToken ct) =>
    Results.Ok(await db.GetLeaderboardAsync(page ?? 1, pageSize ?? 50, ct)));

v1.MapGet("/players/{playerId}/rp", async (string playerId, RepositoryDatabase db, CancellationToken ct) =>
    Results.Ok(await db.GetPlayerRpAsync(playerId, ct)));

app.Run();

static string? UserId(ClaimsPrincipal principal) =>
    principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");

public sealed record ChartManifest(string Title, string Creator, string Description, string Difficulty,
    string[] Tags, string FormatVersion, string ChartDataPath, string? CoverPath);
public sealed record ChartItem(string ChartId, string Title, string Creator, string Description, string Difficulty,
    string[] Tags, string VersionId, long SizeBytes, string Sha256, DateTimeOffset UpdatedAt, string RankedState);
public sealed record ChartVersion(string ChartId, string VersionId, long SizeBytes, string Sha256,
    string FormatVersion, string ChartDataPath, DateTimeOffset CreatedAt, string RankedState);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
public sealed record UploadSession(string UploadId, string OwnerId, string StagePath, string Status);
public sealed record NominationRequest(string Rationale);
public sealed record ReviewDecisionRequest(string Decision, string Rationale);
public sealed record ChartReportRequest(string Reason);
public sealed record ModerationRequest(string Rationale);

public sealed class PackageValidation
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public ValidatedPackage? Package { get; init; }
}

public sealed record ValidatedPackage(ChartManifest Manifest, long SizeBytes, string Sha256);

public sealed class PackageStore(string packagesRoot, string stagingRoot, long maxPackageBytes,
    int maxArchiveEntries, long maxExpandedBytes)
{
    public string CreateStage(string uploadId)
    {
        var path = Path.Combine(stagingRoot, uploadId);
        Directory.CreateDirectory(path);
        return path;
    }

    public async Task<string> ReceivePackageAsync(string stagePath, Stream source, CancellationToken ct)
    {
        var destination = Path.Combine(stagePath, "package.zip.part");
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long length = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            length += read;
            if (length > maxPackageBytes) throw new InvalidDataException("Package exceeds the configured size limit.");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return destination;
    }

    public async Task<PackageValidation> ValidatePackageAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count == 0 || archive.Entries.Count > maxArchiveEntries)
                return Invalid("Archive entry count is outside the configured limit.");
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                if (!SafeArchivePath(entry.FullName)) return Invalid("Archive contains an unsafe path.");
                expanded = checked(expanded + entry.Length);
                if (expanded > maxExpandedBytes) return Invalid("Expanded package exceeds the configured size limit.");
            }
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry is null) return Invalid("Package must contain a root manifest.json.");
            await using var manifestStream = manifestEntry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<ChartManifest>(manifestStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Title) ||
                string.IsNullOrWhiteSpace(manifest.Creator) || string.IsNullOrWhiteSpace(manifest.Difficulty) ||
                string.IsNullOrWhiteSpace(manifest.ChartDataPath) || string.IsNullOrWhiteSpace(manifest.FormatVersion))
                return Invalid("Manifest requires title, creator, difficulty, formatVersion, and chartDataPath.");
            if (manifestEntry.Length > 65536 || manifest.Title.Length > 160 || manifest.Creator.Length > 100 ||
                (manifest.Description?.Length ?? 0) > 4000 || (manifest.Tags?.Length ?? 0) > 20 ||
                (manifest.Tags ?? []).Any(tag => tag is null || tag.Length > 40))
                return Invalid("Manifest metadata exceeds one or more field limits.");
            var chartDataEntry = SafeArchivePath(manifest.ChartDataPath) ? archive.GetEntry(manifest.ChartDataPath) : null;
            if (chartDataEntry is null || chartDataEntry.Length <= 0)
                return Invalid("Manifest chartDataPath must name a safe file in the package.");
            await using var packageStream = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(packageStream, ct);
            return new PackageValidation
            {
                IsValid = true,
                Package = new ValidatedPackage(manifest, packageStream.Length, Convert.ToHexString(hash).ToLowerInvariant())
            };
        }
        catch (InvalidDataException ex) { return Invalid(ex.Message); }
        catch (JsonException) { return Invalid("manifest.json is not valid JSON."); }
        catch (Exception ex) when (ex is IOException or OverflowException) { return Invalid("Package could not be safely inspected."); }
    }

    public string Publish(string sourceFile, string chartId, string versionId)
    {
        var directory = Path.Combine(packagesRoot, chartId);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"{versionId}.zip");
        File.Move(sourceFile, destination);
        return destination;
    }

    public FileStream? OpenPackage(string chartId, string versionId)
    {
        if (!Guid.TryParseExact(chartId, "N", out _) || versionId != "1") return null;
        var path = Path.Combine(packagesRoot, chartId, $"{versionId}.zip");
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public void DeleteStage(string stagePath)
    {
        if (Directory.Exists(stagePath)) Directory.Delete(stagePath, recursive: true);
    }

    private static bool SafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':'))
            return false;
        var normalized = path.TrimEnd('/');
        return normalized.Length > 0 && normalized.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
    }

    private static PackageValidation Invalid(string error) => new() { IsValid = false, Error = error };
}

public sealed class RepositoryDatabase(string connectionString)
{
    public async Task InitializeAsync()
    {
        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync();
        await using (var journal = db.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode=WAL;";
            await journal.ExecuteNonQueryAsync();
        }

        await using (var history = db.CreateCommand())
        {
            history.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                  migration_id TEXT PRIMARY KEY,
                  applied_at TEXT NOT NULL
                );
                """;
            await history.ExecuteNonQueryAsync();
        }

        var migrationsPath = Path.Combine(AppContext.BaseDirectory, "Migrations");
        if (!Directory.Exists(migrationsPath))
            throw new DirectoryNotFoundException($"SQL migration directory not found: {migrationsPath}");

        foreach (var migrationPath in Directory.GetFiles(migrationsPath, "*.sql").Order(StringComparer.Ordinal))
        {
            var migrationId = Path.GetFileName(migrationPath);
            await using var check = db.CreateCommand();
            check.CommandText = "SELECT 1 FROM schema_migrations WHERE migration_id = $id";
            check.Parameters.AddWithValue("$id", migrationId);
            if (await check.ExecuteScalarAsync() is not null)
                continue;

            var sql = await File.ReadAllTextAsync(migrationPath);
            await using var transaction = await db.BeginTransactionAsync();
            await using var apply = db.CreateCommand();
            apply.Transaction = (SqliteTransaction)transaction;
            apply.CommandText = sql;
            await apply.ExecuteNonQueryAsync();

            await using var record = db.CreateCommand();
            record.Transaction = (SqliteTransaction)transaction;
            record.CommandText = "INSERT INTO schema_migrations(migration_id, applied_at) VALUES($id, $appliedAt)";
            record.Parameters.AddWithValue("$id", migrationId);
            record.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            await record.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
    }

    public async Task<PagedResult<ChartItem>> SearchChartsAsync(string? query, string? creator, string? difficulty,
        string[] tags, string? sort, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync(ct);
        var where = "status = 'public'";
        if (!string.IsNullOrWhiteSpace(query))
            where += " AND (title LIKE $query OR creator LIKE $query OR EXISTS (SELECT 1 FROM json_each(charts.tags_json) query_tag WHERE query_tag.value LIKE $query))";
        if (!string.IsNullOrWhiteSpace(creator)) where += " AND creator LIKE $creator";
        if (!string.IsNullOrWhiteSpace(difficulty)) where += " AND difficulty = $difficulty";
        for (var index = 0; index < tags.Length; index++)
            where += $" AND EXISTS (SELECT 1 FROM json_each(charts.tags_json) tag WHERE lower(tag.value)=lower($tag{index}))";
        var order = sort == "popular" ? "downloads DESC, updated_at DESC" : "updated_at DESC";
        await using var count = db.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM charts WHERE {where}";
        AddSearchParameters(count, query, creator, difficulty, tags);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        await using var command = db.CreateCommand();
        command.CommandText = $"SELECT chart_id,title,creator,description,difficulty,tags_json,version_id,size_bytes,sha256,updated_at,ranked_state FROM charts WHERE {where} ORDER BY {order} LIMIT $limit OFFSET $offset";
        AddSearchParameters(command, query, creator, difficulty, tags);
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var items = new List<ChartItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new ChartItem(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [],
                reader.GetString(6), reader.GetInt64(7), reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.GetString(10)));
        return new PagedResult<ChartItem>(items, page, pageSize, total);
    }

    public Task<ChartItem?> GetChartAsync(string id, CancellationToken ct) => GetChartInternalAsync(id, ct);
    public async Task<ChartVersion?> GetVersionAsync(string id, string version, CancellationToken ct)
    {
        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT chart_id,version_id,size_bytes,sha256,format_version,chart_data_path,created_at,ranked_state FROM charts WHERE chart_id=$id AND version_id=$version AND status='public'";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$version", version);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new ChartVersion(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.GetString(7))
            : null;
    }

    public async Task CreateUploadAsync(string id, string owner, string stage, CancellationToken ct) =>
        await ExecuteAsync("INSERT INTO uploads(upload_id,owner_id,stage_path,status) VALUES($id,$owner,$stage,'created')",
            ct, ("$id", id), ("$owner", owner), ("$stage", stage));

    public async Task<UploadSession?> GetUploadAsync(string id, CancellationToken ct)
    {
        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT upload_id,owner_id,stage_path,status FROM uploads WHERE upload_id=$id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new UploadSession(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }

    public async Task PublishChartAsync(string id, string version, string owner, ValidatedPackage package,
        string path, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        await ExecuteAsync("""
            INSERT INTO charts(chart_id,owner_id,title,creator,description,difficulty,tags_json,version_id,
              package_path,size_bytes,sha256,format_version,chart_data_path,created_at,updated_at,status,ranked_state)
            VALUES($id,$owner,$title,$creator,$description,$difficulty,$tags,$version,$path,$size,$hash,$format,$data,$now,$now,'public','unranked')
            """, ct, ("$id", id), ("$owner", owner), ("$title", package.Manifest.Title),
            ("$creator", package.Manifest.Creator), ("$description", package.Manifest.Description ?? ""),
            ("$difficulty", package.Manifest.Difficulty), ("$tags", JsonSerializer.Serialize(package.Manifest.Tags ?? [])),
            ("$version", version), ("$path", path), ("$size", package.SizeBytes), ("$hash", package.Sha256),
            ("$format", package.Manifest.FormatVersion), ("$data", package.Manifest.ChartDataPath), ("$now", now));
    }

    public async Task CompleteUploadAsync(string id, string chartId, CancellationToken ct) =>
        await ExecuteAsync("UPDATE uploads SET status='completed',chart_id=$chart WHERE upload_id=$id",
            ct, ("$id", id), ("$chart", chartId));
    public async Task RecordDownloadAsync(string chartId, CancellationToken ct) =>
        await ExecuteAsync("UPDATE charts SET downloads=downloads+1 WHERE chart_id=$id", ct, ("$id", chartId));
    public async Task FailUploadAsync(string id, string error, CancellationToken ct) =>
        await ExecuteAsync("UPDATE uploads SET status='failed',error=$error WHERE upload_id=$id",
            ct, ("$id", id), ("$error", error));

    public async Task<string?> NominateAsync(string chartId, string player, string rationale, CancellationToken ct)
    {
        if (await GetChartAsync(chartId, ct) is null) return null;
        var id = Guid.NewGuid().ToString("N");
        await ExecuteAsync("INSERT INTO nominations(nomination_id,chart_id,player_id,rationale,status,created_at) VALUES($id,$chart,$player,$rationale,'pending',$now)",
            ct, ("$id", id), ("$chart", chartId), ("$player", player), ("$rationale", rationale), ("$now", DateTimeOffset.UtcNow.ToString("O")));
        return id;
    }

    public async Task<string?> ReportChartAsync(string chartId, string reporter, string reason, CancellationToken ct)
    {
        if (await GetChartAsync(chartId, ct) is null) return null;
        var id = Guid.NewGuid().ToString("N");
        await ExecuteAsync("INSERT INTO chart_reports(report_id,chart_id,reporter_id,reason,status,created_at) VALUES($id,$chart,$reporter,$reason,'open',$now)",
            ct, ("$id", id), ("$chart", chartId), ("$reporter", reporter), ("$reason", reason), ("$now", DateTimeOffset.UtcNow.ToString("O")));
        return id;
    }

    public async Task<bool> TakedownChartAsync(string chartId, string moderator, string rationale, CancellationToken ct)
    {
        await using var db = new SqliteConnection(connectionString); await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using var update = db.CreateCommand(); update.Transaction = (SqliteTransaction)tx;
        update.CommandText = "UPDATE charts SET status='removed' WHERE chart_id=$chart AND status='public'";
        update.Parameters.AddWithValue("$chart", chartId);
        if (await update.ExecuteNonQueryAsync(ct) == 0) { await tx.RollbackAsync(ct); return false; }
        await using var audit = db.CreateCommand(); audit.Transaction = (SqliteTransaction)tx;
        audit.CommandText = "INSERT INTO moderation_audit(chart_id,moderator_id,action,rationale,created_at) VALUES($chart,$moderator,'takedown',$rationale,$now)";
        audit.Parameters.AddWithValue("$chart", chartId); audit.Parameters.AddWithValue("$moderator", moderator);
        audit.Parameters.AddWithValue("$rationale", rationale); audit.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await audit.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return true;
    }

    public async Task<PagedResult<object>> GetOpenReportsAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        await using var db = new SqliteConnection(connectionString); await db.OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT report_id,chart_id,reporter_id,reason,created_at FROM chart_reports WHERE status='open' ORDER BY created_at LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", pageSize); command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new { reportId = reader.GetString(0), chartId = reader.GetString(1), reporterId = reader.GetString(2), reason = reader.GetString(3), createdAt = reader.GetString(4) });
        return new PagedResult<object>(items, page, pageSize, items.Count);
    }

    public async Task<PagedResult<object>> GetReviewsAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        await using var db = new SqliteConnection(connectionString); await db.OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT nomination_id,chart_id,player_id,rationale,status,created_at FROM nominations WHERE status='pending' ORDER BY created_at LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", pageSize); command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new { nominationId = reader.GetString(0), chartId = reader.GetString(1), playerId = reader.GetString(2), rationale = reader.GetString(3), status = reader.GetString(4), createdAt = reader.GetString(5) });
        return new PagedResult<object>(items, page, pageSize, items.Count);
    }

    public async Task<bool> DecideReviewAsync(string id, string reviewer, string decision, string rationale, CancellationToken ct)
    {
        await using var db = new SqliteConnection(connectionString); await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using var findChart = db.CreateCommand(); findChart.Transaction = (SqliteTransaction)tx;
        findChart.CommandText = "SELECT chart_id FROM nominations WHERE nomination_id=$id AND status='pending'";
        findChart.Parameters.AddWithValue("$id", id);
        var chartId = await findChart.ExecuteScalarAsync(ct) as string;
        if (chartId is null) { await tx.RollbackAsync(ct); return false; }
        await using var update = db.CreateCommand(); update.Transaction = (SqliteTransaction)tx;
        update.CommandText = "UPDATE nominations SET status=$decision WHERE nomination_id=$id AND status='pending'";
        update.Parameters.AddWithValue("$decision", decision); update.Parameters.AddWithValue("$id", id);
        var changed = await update.ExecuteNonQueryAsync(ct);
        if (changed == 0) { await tx.RollbackAsync(ct); return false; }
        await using var audit = db.CreateCommand(); audit.Transaction = (SqliteTransaction)tx;
        audit.CommandText = "INSERT INTO review_audit(nomination_id,reviewer_id,decision,rationale,created_at) VALUES($id,$reviewer,$decision,$rationale,$now)";
        audit.Parameters.AddWithValue("$id", id); audit.Parameters.AddWithValue("$reviewer", reviewer);
        audit.Parameters.AddWithValue("$decision", decision); audit.Parameters.AddWithValue("$rationale", rationale);
        audit.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await audit.ExecuteNonQueryAsync(ct);
        if (decision == "approved")
        {
            await using var markRanked = db.CreateCommand(); markRanked.Transaction = (SqliteTransaction)tx;
            markRanked.CommandText = "UPDATE charts SET ranked_state='ranked' WHERE chart_id=$chart";
            markRanked.Parameters.AddWithValue("$chart", chartId);
            await markRanked.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct); return true;
    }

    public async Task<PagedResult<object>> GetLeaderboardAsync(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        await using var db = new SqliteConnection(connectionString); await db.OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT player_id,SUM(points) AS rp FROM ranked_results GROUP BY player_id ORDER BY rp DESC LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", pageSize); command.Parameters.AddWithValue("$offset", (page - 1) * pageSize);
        var items = new List<object>(); var rank = (page - 1) * pageSize;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(new { rank = ++rank, playerId = reader.GetString(0), rp = reader.GetDouble(1) });
        return new PagedResult<object>(items, page, pageSize, rank);
    }

    public async Task<object> GetPlayerRpAsync(string player, CancellationToken ct)
    {
        await using var db = new SqliteConnection(connectionString); await db.OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT COALESCE(SUM(points),0) FROM ranked_results WHERE player_id=$player";
        command.Parameters.AddWithValue("$player", player);
        return new { playerId = player, rp = Convert.ToDouble(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) };
    }

    private async Task<ChartItem?> GetChartInternalAsync(string id, CancellationToken ct)
    {
        await using var db = new SqliteConnection(connectionString); await db.OpenAsync(ct);
        await using var command = db.CreateCommand();
        command.CommandText = "SELECT chart_id,title,creator,description,difficulty,tags_json,version_id,size_bytes,sha256,updated_at,ranked_state FROM charts WHERE chart_id=$id AND status='public'";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new ChartItem(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [], reader.GetString(6),
                reader.GetInt64(7), reader.GetString(8), DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.GetString(10))
            : null;
    }

    private static void AddSearchParameters(SqliteCommand command, string? query, string? creator, string? difficulty, string[] tags)
    {
        if (!string.IsNullOrWhiteSpace(query)) command.Parameters.AddWithValue("$query", $"%{query}%");
        if (!string.IsNullOrWhiteSpace(creator)) command.Parameters.AddWithValue("$creator", $"%{creator}%");
        if (!string.IsNullOrWhiteSpace(difficulty)) command.Parameters.AddWithValue("$difficulty", difficulty);
        for (var index = 0; index < tags.Length; index++)
            command.Parameters.AddWithValue($"$tag{index}", tags[index]);
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var db = new SqliteConnection(connectionString); await db.OpenAsync(ct);
        await using var command = db.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
