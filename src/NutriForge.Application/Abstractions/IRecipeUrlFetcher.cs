namespace NutriForge.Application.Abstractions;

/// <summary>A recipe page fetched from the web: its HTML and the final (post-redirect) URL.</summary>
public sealed record FetchedPage(string Html, string FinalUrl);

/// <summary>
/// Fetches a recipe web page's HTML for deterministic JSON-LD parsing. The implementation is
/// SSRF-hardened (rejects loopback / private / link-local / cloud-metadata addresses, caps size + time,
/// HTML only) — pasting a URL must never let the server reach internal infrastructure. Faults degrade
/// to null; nothing throws onto the request path.
/// </summary>
public interface IRecipeUrlFetcher
{
    Task<FetchedPage?> FetchAsync(string url, CancellationToken ct = default);
}
