#nullable enable
using System.Net.Http.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HttpClient = System.Net.Http.HttpClient;

internal sealed record RepositoryOAuthSession(string AccessToken, DateTimeOffset ExpiresAt)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(AccessToken) && DateTimeOffset.UtcNow < ExpiresAt;
}

internal static class RepositoryOAuth
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("RHYTHIA_REPOSITORY_OIDC_AUTHORITY")) &&
        !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("RHYTHIA_REPOSITORY_OIDC_CLIENT_ID")) &&
        !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("RHYTHIA_REPOSITORY_OIDC_SCOPE"));

    public static async Task<RepositoryOAuthSession> SignInAsync(CancellationToken cancellationToken)
    {
        string authority = System.Environment.GetEnvironmentVariable("RHYTHIA_REPOSITORY_OIDC_AUTHORITY")?.TrimEnd('/')
            ?? throw new InvalidOperationException("Set RHYTHIA_REPOSITORY_OIDC_AUTHORITY before signing in.");
        string clientId = System.Environment.GetEnvironmentVariable("RHYTHIA_REPOSITORY_OIDC_CLIENT_ID")
            ?? throw new InvalidOperationException("Set RHYTHIA_REPOSITORY_OIDC_CLIENT_ID before signing in.");
        string scope = System.Environment.GetEnvironmentVariable("RHYTHIA_REPOSITORY_OIDC_SCOPE")
            ?? throw new InvalidOperationException("Set RHYTHIA_REPOSITORY_OIDC_SCOPE to the scope that grants access to the repository API.");
        EnsureSecureEndpoint(new Uri(authority, UriKind.Absolute));

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromMinutes(4));
        string discoveryUrl = authority + "/.well-known/openid-configuration";
        var metadata = await Http.GetFromJsonAsync<OpenIdConfiguration>(discoveryUrl, JsonOptions, requestTimeout.Token)
            ?? throw new InvalidOperationException("The identity provider returned an empty OpenID configuration.");
        if (!Uri.TryCreate(metadata.AuthorizationEndpoint, UriKind.Absolute, out var authorizationEndpoint) ||
            !Uri.TryCreate(metadata.TokenEndpoint, UriKind.Absolute, out var tokenEndpoint))
            throw new InvalidOperationException("The identity provider did not provide valid authorization and token endpoints.");
        EnsureSecureEndpoint(authorizationEndpoint);
        EnsureSecureEndpoint(tokenEndpoint);

        int callbackPort = GetCallbackPort();
        using var listener = new TcpListener(IPAddress.Loopback, callbackPort);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string redirectUri = $"http://127.0.0.1:{port}/callback";
        string state = CreateRandomValue(32);
        string verifier = CreateRandomValue(48);
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizationUri = BuildAuthorizationUri(
            authorizationEndpoint, clientId, redirectUri, scope, state, challenge);

        Error error = OS.ShellOpen(authorizationUri.AbsoluteUri);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Could not open the sign-in page: {error}.");

        string code = await ReceiveAuthorizationCodeAsync(listener, state, requestTimeout.Token);
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = verifier
            })
        };
        using var tokenResponse = await Http.SendAsync(tokenRequest, requestTimeout.Token);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            string response = await tokenResponse.Content.ReadAsStringAsync(requestTimeout.Token);
            throw new HttpRequestException(
                $"Sign-in token exchange failed ({(int)tokenResponse.StatusCode}): {response}");
        }

        var tokens = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, requestTimeout.Token)
            ?? throw new InvalidOperationException("The identity provider returned an empty token response.");
        if (string.IsNullOrWhiteSpace(tokens.AccessToken))
            throw new InvalidOperationException("The identity provider did not return an access token.");
        if (tokens.ExpiresIn <= 0)
            throw new InvalidOperationException("The identity provider returned an invalid access token lifetime.");
        return new RepositoryOAuthSession(tokens.AccessToken, DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, tokens.ExpiresIn)));
    }

    private static int GetCallbackPort()
    {
        string? configuredPort = System.Environment.GetEnvironmentVariable("RHYTHIA_REPOSITORY_OIDC_CALLBACK_PORT");
        if (string.IsNullOrWhiteSpace(configuredPort))
            return 0;
        if (!int.TryParse(configuredPort, out int port) || port is < 1 or > 65535)
            throw new InvalidOperationException("RHYTHIA_REPOSITORY_OIDC_CALLBACK_PORT must be a port from 1 to 65535.");
        return port;
    }

    private static Uri BuildAuthorizationUri(Uri endpoint, string clientId, string redirectUri,
        string scope, string state, string challenge)
    {
        string separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
        string parameters = string.Join("&", new[]
        {
            Pair("response_type", "code"),
            Pair("client_id", clientId),
            Pair("redirect_uri", redirectUri),
            Pair("scope", scope),
            Pair("state", state),
            Pair("code_challenge", challenge),
            Pair("code_challenge_method", "S256")
        });
        return new Uri(endpoint.AbsoluteUri + separator + parameters, UriKind.Absolute);
    }

    private static async Task<string> ReceiveAuthorizationCodeAsync(
        TcpListener listener, string expectedState, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
            var stream = connection.GetStream();
            string? target = await ReadRequestTargetAsync(stream, cancellationToken);
            if (target is null)
                continue;

            var callback = new Uri(new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"), target);
            Dictionary<string, string> query = ParseQuery(callback.Query);
            bool validCallback = callback.AbsolutePath == "/callback" &&
                query.TryGetValue("state", out string? receivedState) &&
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expectedState), Encoding.UTF8.GetBytes(receivedState));
            await RespondToBrowserAsync(stream, validCallback, cancellationToken);
            if (!validCallback)
                continue;
            if (query.TryGetValue("error", out string? error))
            {
                string description = query.TryGetValue("error_description", out string? reason) ? reason : error;
                throw new InvalidOperationException($"Sign-in was not completed: {description}");
            }
            if (query.TryGetValue("code", out string? code) && !string.IsNullOrWhiteSpace(code))
                return code;
            throw new InvalidOperationException("The identity provider callback did not contain an authorization code.");
        }
    }

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        byte[] buffer = new byte[1];
        while (bytes.Count < 8192)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return null;
            bytes.Add(buffer[0]);
            int count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == 13 && bytes[count - 3] == 10 &&
                bytes[count - 2] == 13 && bytes[count - 1] == 10)
                break;
        }

        string requestLine = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n", StringSplitOptions.None)[0];
        string[] fields = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && fields[0] == "GET" ? fields[1] : null;
    }

    private static async Task RespondToBrowserAsync(NetworkStream stream, bool success, CancellationToken cancellationToken)
    {
        string body = success
            ? "<!doctype html><html><body><h2>Sign-in complete</h2><p>You can return to Rhythia.</p></body></html>"
            : "<!doctype html><html><body><h2>Sign-in callback ignored</h2><p>Return to Rhythia and try again.</p></body></html>";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string headers = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            result[Uri.UnescapeDataString(pair[0].Replace('+', ' '))] =
                pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
        }
        return result;
    }

    private static string CreateRandomValue(int bytes)
    {
        return Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(bytes));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Pair(string key, string value) =>
        Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);

    private static void EnsureSecureEndpoint(Uri endpoint)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback))
            throw new InvalidOperationException("Identity provider endpoints must use HTTPS except for localhost development.");
    }

    private sealed class OpenIdConfiguration
    {
        [JsonPropertyName("authorization_endpoint")]
        public string? AuthorizationEndpoint { get; set; }

        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
