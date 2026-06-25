using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using NutriForge.Application.Abstractions;
using NutriForge.Application.Recipes;

namespace NutriForge.Infrastructure.RecipeImport;

/// <summary>
/// Fetches recipe-page HTML over a typed HttpClient whose connect callback resolves the host and
/// refuses any non-public address (<see cref="PrivateNetworkGuard"/>) — the SSRF gate. Every redirect
/// hop re-enters the callback, so a public→internal redirect (or DNS rebind) is rejected too. Caps
/// size + time, accepts only HTML, and degrades to null on any fault.
/// </summary>
public sealed partial class RecipeUrlFetcher(HttpClient http, ILogger<RecipeUrlFetcher> logger) : IRecipeUrlFetcher
{
    private const int MaxBytes = 3_000_000; // ~3 MB

    public async Task<FetchedPage?> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var media = resp.Content.Headers.ContentType?.MediaType;
            if (media is not null
                && !media.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !media.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                return null; // not a web page
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var html = await ReadCappedAsync(stream, ct).ConfigureAwait(false);
            var final = resp.RequestMessage?.RequestUri?.ToString() ?? uri.ToString();
            return new FetchedPage(html, final);
        }
#pragma warning disable CA1031 // outbound fetch is best-effort — never throw onto the request path
        catch (Exception ex)
        {
            LogFetchFailed(logger, uri, ex);
            return null;
        }
#pragma warning restore CA1031
    }

    private static async Task<string> ReadCappedAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while (ms.Length < MaxBytes && (read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>
    /// The SSRF connect gate: resolve the host, drop every blocked address, and connect only to a
    /// surviving public one. Used as the typed client's <c>SocketsHttpHandler.ConnectCallback</c>.
    /// </summary>
    public static async ValueTask<Stream> ConnectGuardedAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        var resolved = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        var allowed = resolved.Where(ip => !PrivateNetworkGuard.IsBlocked(ip)).ToArray();
        if (allowed.Length == 0)
        {
            throw new IOException($"Refusing to connect to a non-public address for host '{host}'.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recipe URL fetch failed for {Uri}")]
    private static partial void LogFetchFailed(ILogger logger, Uri uri, Exception ex);
}
