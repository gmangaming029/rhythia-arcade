using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public partial class ArcadeService : Node
{
    private const int MaxCreditsPerRequest = 100;
    private const int MaxRequestBytes = 1024;

    private readonly object stateLock = new();
    private readonly HashSet<string> transactions = new(StringComparer.Ordinal);
    private readonly Queue<string> transactionOrder = new();
    private static readonly string StatePath = Path.Combine(Constants.USER_FOLDER, "arcade-state.json");
    private HttpListener listener;
    private CancellationTokenSource cancellation;
    private int credits;
    private bool freePlay;
    private bool persistenceAvailable = true;

    private sealed class PersistedState
    {
        public int Credits { get; set; }
        public bool FreePlay { get; set; }
        public List<string> Transactions { get; set; } = [];
    }

    public static ArcadeService Instance { get; private set; }

    public int Credits
    {
        get
        {
            lock (stateLock)
                return credits;
        }
    }

    public bool FreePlay
    {
        get
        {
            lock (stateLock)
                return freePlay;
        }
    }

    public bool ApiAvailable { get; private set; }
    [Signal]
    public delegate void StateChangedEventHandler(int credits, bool freePlay);

    public override void _Ready()
    {
        Instance = this;
        LoadState();
        StartApi();
    }

    public override void _ExitTree()
    {
        cancellation?.Cancel();
        listener?.Close();
        cancellation?.Dispose();
        listener = null;
    }

    public bool TryConsumeCredit()
    {
        int updatedCredits;
        bool isFreePlay;

        lock (stateLock)
        {
            isFreePlay = freePlay;
            if (!persistenceAvailable)
            {
                _ = ToastNotification.Notify("Arcade credit storage is unavailable. Map start cancelled.", 1);
                return false;
            }

            if (!isFreePlay && credits == 0)
            {
                _ = ToastNotification.Notify(
                    ApiAvailable ? "Insert a credit to start a map." : "Arcade API is offline. Check the client log.",
                    1
                );
                return false;
            }

            if (!isFreePlay)
            {
                credits--;
                if (!saveStateLocked())
                {
                    credits++;
                    _ = ToastNotification.Notify("Unable to save arcade credits. Map start cancelled.", 1);
                    return false;
                }
            }

            updatedCredits = credits;
        }

        EmitSignal(SignalName.StateChanged, updatedCredits, isFreePlay);
        return true;
    }

    private void StartApi()
    {
        try
        {
            Directory.CreateDirectory(Constants.USER_FOLDER);
            string tokenPath = Path.Combine(Constants.USER_FOLDER, "arcade-api.token");
            string token = File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : "";
            if (token.Length < 64)
            {
                token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                File.WriteAllText(tokenPath, token);
            }

            int port = 8765;
            string portSetting = OS.GetEnvironment("RHYTHIA_ARCADE_PORT");
            if (!string.IsNullOrWhiteSpace(portSetting) && (!int.TryParse(portSetting, out port) || port is < 1024 or > 65535))
                throw new InvalidOperationException("RHYTHIA_ARCADE_PORT must be between 1024 and 65535.");

            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            ApiAvailable = true;
            cancellation = new CancellationTokenSource();
            _ = Listen(token, cancellation.Token);
            Logger.Log($"Arcade API listening on http://127.0.0.1:{port}/ (token file: {tokenPath})");
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Logger.Error($"Unable to start the localhost arcade API: {exception.Message}");
            listener?.Close();
            listener = null;
        }
    }

    private async Task Listen(string token, CancellationToken tokenCancellation)
    {
        while (!tokenCancellation.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException) when (tokenCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (tokenCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException exception)
            {
                Logger.Error($"Arcade API listener stopped: {exception.Message}");
                return;
            }

            try
            {
                await HandleRequest(context, token);
            }
            catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException)
            {
                Logger.Log($"Arcade API client disconnected: {exception.Message}", true);
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context, string token)
    {
        HttpListenerRequest request = context.Request;
        string path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
        if (request.HttpMethod == "GET" && path == "/controller")
        {
            await WriteControllerPage(context.Response);
            return;
        }

        if (request.HttpMethod == "GET" && path.Length == 0)
        {
            context.Response.Redirect("/controller");
            context.Response.Close();
            return;
        }

        if (!IsAuthorized(request, token))
        {
            await WriteResponse(context.Response, 401, "{\"error\":\"unauthorized\"}");
            return;
        }

        if (request.HttpMethod == "GET" && path == "/api/v1/status")
        {
            int currentCredits;
            bool currentFreePlay;
            lock (stateLock)
            {
                if (!persistenceAvailable)
                {
                    currentCredits = -1;
                    currentFreePlay = false;
                }
                else
                {
                    currentCredits = credits;
                    currentFreePlay = freePlay;
                }
            }

            if (currentCredits < 0)
            {
                await WriteResponse(context.Response, 503, "{\"error\":\"arcade_state_unavailable\"}");
                return;
            }

            await WriteResponse(
                context.Response,
                200,
                JsonSerializer.Serialize(
                    new
                    {
                        credits = currentCredits,
                        freePlay = currentFreePlay,
                    }
                )
            );
            return;
        }

        if (request.HttpMethod is not ("POST" or "PUT"))
        {
            await WriteResponse(context.Response, 405, "{\"error\":\"method_not_allowed\"}");
            return;
        }

        if (request.ContentLength64 > MaxRequestBytes)
        {
            await WriteResponse(context.Response, 413, "{\"error\":\"request_too_large\"}");
            return;
        }

        try
        {
            string body = await ReadBody(request);
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                await WriteResponse(context.Response, 400, "{\"error\":\"body_must_be_a_json_object\"}");
                return;
            }

            if (request.HttpMethod == "POST" && path == "/api/v1/credits")
            {
                if (!root.TryGetProperty("amount", out JsonElement amountValue) || !amountValue.TryGetInt32(out int amount) || amount is < 1 or > MaxCreditsPerRequest)
                {
                    await WriteResponse(context.Response, 400, "{\"error\":\"amount_must_be_an_integer_between_1_and_100\"}");
                    return;
                }

                string transactionId = null;
                if (root.TryGetProperty("transactionId", out JsonElement transactionValue))
                {
                    if (transactionValue.ValueKind != JsonValueKind.String)
                    {
                        await WriteResponse(context.Response, 400, "{\"error\":\"transactionId_must_be_a_string\"}");
                        return;
                    }

                    transactionId = transactionValue.GetString();
                    if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 128)
                    {
                        await WriteResponse(context.Response, 400, "{\"error\":\"transactionId_must_be_1_to_128_characters\"}");
                        return;
                    }

                }

                if (!TryAddCredits(amount, transactionId, out bool duplicate))
                {
                    await WriteResponse(context.Response, 500, "{\"error\":\"unable_to_persist_credits\"}");
                    return;
                }

                if (duplicate)
                {
                    await WriteResponse(context.Response, 200, "{\"accepted\":true,\"duplicate\":true}");
                    return;
                }

                await WriteResponse(context.Response, 202, "{\"accepted\":true}");
                return;
            }

            if (request.HttpMethod == "PUT" && path == "/api/v1/free-play")
            {
                if (!root.TryGetProperty("enabled", out JsonElement enabledValue) || enabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    await WriteResponse(context.Response, 400, "{\"error\":\"enabled_must_be_a_boolean\"}");
                    return;
                }

                bool enabled = enabledValue.GetBoolean();
                if (!SetFreePlay(enabled))
                {
                    await WriteResponse(context.Response, 500, "{\"error\":\"unable_to_persist_arcade_state\"}");
                    return;
                }

                await WriteResponse(context.Response, 202, "{\"accepted\":true}");
                return;
            }

            await WriteResponse(context.Response, 404, "{\"error\":\"not_found\"}");
        }
        catch (JsonException)
        {
            await WriteResponse(context.Response, 400, "{\"error\":\"invalid_json\"}");
        }
        catch (InvalidDataException)
        {
            await WriteResponse(context.Response, 413, "{\"error\":\"request_too_large\"}");
        }
        catch (IOException exception)
        {
            Logger.Error($"Arcade API request failed: {exception.Message}");
            context.Response.Abort();
        }
    }

    private static bool IsAuthorized(HttpListenerRequest request, string token)
    {
        string authorization = request.Headers["Authorization"] ?? "";
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        byte[] supplied = Encoding.UTF8.GetBytes(authorization[prefix.Length..]);
        byte[] expected = Encoding.UTF8.GetBytes(token);
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static async Task<string> ReadBody(HttpListenerRequest request)
    {
        using MemoryStream body = new();
        byte[] buffer = new byte[256];
        int read;
        while ((read = await request.InputStream.ReadAsync(buffer)) > 0)
        {
            if (body.Length + read > MaxRequestBytes)
                throw new InvalidDataException();
            body.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static async Task WriteResponse(HttpListenerResponse response, int statusCode, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-store";
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task WriteControllerPage(HttpListenerResponse response)
    {
        using Stream page = typeof(ArcadeService).Assembly.GetManifestResourceStream("Rhythia.arcade-controller.html")
            ?? throw new InvalidOperationException("The arcade controller page resource is missing.");
        using MemoryStream content = new();
        await page.CopyToAsync(content);
        byte[] bytes = content.ToArray();

        response.StatusCode = 200;
        response.ContentType = "text/html";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; form-action 'none'";
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public void AddCredits(int amount)
    {
        if (!TryAddCredits(amount, null, out _))
            _ = ToastNotification.Notify("Unable to save arcade credits; the reward was not added.", 1);
    }

    private bool TryAddCredits(int amount, string transactionId, out bool duplicate)
    {
        int updatedCredits;
        bool currentFreePlay;
        lock (stateLock)
        {
            duplicate = transactionId != null && transactions.Contains(transactionId);
            if (duplicate)
                return true;

            int previousCredits = credits;
            string[] previousTransactions = [.. transactionOrder];
            credits = (int)Math.Min(int.MaxValue, (long)credits + amount);
            if (transactionId != null)
            {
                transactions.Add(transactionId);
                transactionOrder.Enqueue(transactionId);
                if (transactionOrder.Count > 4096)
                    transactions.Remove(transactionOrder.Dequeue());
            }

            if (!saveStateLocked())
            {
                credits = previousCredits;
                transactions.Clear();
                transactionOrder.Clear();
                foreach (string previousTransaction in previousTransactions)
                {
                    transactions.Add(previousTransaction);
                    transactionOrder.Enqueue(previousTransaction);
                }
                duplicate = false;
                return false;
            }

            updatedCredits = credits;
            currentFreePlay = freePlay;
        }

        Callable
            .From(() => EmitSignal(SignalName.StateChanged, updatedCredits, currentFreePlay))
            .CallDeferred();
        return true;
    }

    public bool SetFreePlay(bool enabled)
    {
        int currentCredits;
        lock (stateLock)
        {
            bool previousFreePlay = freePlay;
            freePlay = enabled;
            if (!saveStateLocked())
            {
                freePlay = previousFreePlay;
                return false;
            }
            currentCredits = credits;
        }

        Callable.From(() => EmitSignal(SignalName.StateChanged, currentCredits, enabled)).CallDeferred();
        return true;
    }

    private void LoadState()
    {
        if (!File.Exists(StatePath))
            return;

        try
        {
            PersistedState saved = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(StatePath));
            if (saved == null || saved.Credits < 0 || saved.Transactions == null)
                throw new InvalidDataException("Arcade state contains invalid values.");

            credits = saved.Credits;
            freePlay = saved.FreePlay;
            foreach (string transaction in saved.Transactions.TakeLast(4096))
            {
                if (!string.IsNullOrWhiteSpace(transaction))
                {
                    transactions.Add(transaction);
                    transactionOrder.Enqueue(transaction);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            persistenceAvailable = false;
            Logger.Error($"Unable to load saved arcade state; credit changes are disabled to protect the existing balance: {exception.Message}");
        }
    }

    private bool saveStateLocked()
    {
        if (!persistenceAvailable)
        {
            Logger.Error("Arcade state persistence is unavailable; refusing to change the saved balance.");
            return false;
        }

        string temporaryPath = StatePath + ".tmp";
        PersistedState state = new()
        {
            Credits = credits,
            FreePlay = freePlay,
            Transactions = [.. transactionOrder],
        };

        try
        {
            Directory.CreateDirectory(Constants.USER_FOLDER);
            byte[] content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state));
            using (FileStream stream = new(temporaryPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(true);
            }

            File.Move(temporaryPath, StatePath, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Error($"Unable to persist arcade state: {exception.Message}");
            return false;
        }
    }

}
