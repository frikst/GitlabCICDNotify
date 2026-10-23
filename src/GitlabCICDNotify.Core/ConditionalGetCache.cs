using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace GitlabCICDNotify.Core;

// Query parameters and credentials must never share validators, even if the
// server emits the same ETag for different representations.
internal sealed class ConditionalGetCache
{
    private const int MaxEntries = 128;
    private const int MaxBodyBytes = 256 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, Entry> _entries = [];

    public async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request,
        Guid accountId, long userId, CancellationToken ct)
    {
        var token = request.Headers.GetValues("PRIVATE-TOKEN").Single();
        var key = new CacheKey(accountId, userId, request.RequestUri!.AbsoluteUri,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
        Entry? cached;
        lock (_gate) _entries.TryGetValue(key, out cached);
        if (cached != null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(cached.ETag));

        var response = await http.SendAsync(request, ct);
        try
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (cached == null)
                    throw new GitLabApiException("GitLab vrátil HTTP 304 bez uloženej odpovede.", response.StatusCode);
                // 304 normally omits pagination headers; retain them unless explicitly replaced.
                var updated = cached with
                {
                    ETag = response.Headers.ETag?.ToString() ?? cached.ETag,
                    NextPage = Values(response, "X-Next-Page") ?? cached.NextPage,
                    Links = Values(response, "Link") ?? cached.Links
                };
                Store(key, response.Headers.CacheControl?.NoStore == true || response.Headers.Vary.Contains("*") ? null : updated);
                response.Dispose();
                return updated.CreateResponse();
            }

            Entry? entry = null;
            if (response.StatusCode == HttpStatusCode.OK && response.Headers.ETag is { } etag &&
                response.Headers.CacheControl?.NoStore != true &&
                !response.Headers.Vary.Contains("*") &&
                response.Content.Headers.ContentLength is not > MaxBodyBytes)
            {
                var body = await response.Content.ReadAsByteArrayAsync(ct);
                if (body.Length <= MaxBodyBytes)
                    entry = new Entry(body, etag.ToString(), response.Content.Headers.ContentType?.ToString(),
                        Values(response, "X-Next-Page"), Values(response, "Link"));
            }
            // Errors and responses without validators invalidate the old representation.
            Store(key, entry);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public void InvalidateAccount(Guid id)
    {
        lock (_gate)
            foreach (var key in _entries.Keys.Where(k => k.AccountId == id).ToArray())
                _entries.Remove(key);
    }

    private void Store(CacheKey key, Entry? entry)
    {
        lock (_gate)
        {
            _entries.Remove(key);
            if (entry == null) return;
            while (_entries.Count >= MaxEntries) _entries.Remove(_entries.Keys.First());
            _entries.Add(key, entry);
        }
    }

    private static string[]? Values(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) ? values.ToArray() : null;

    private sealed record CacheKey(Guid AccountId, long UserId, string Url, string CredentialHash);
    private sealed record Entry(byte[] Body, string ETag, string? ContentType, string[]? NextPage, string[]? Links)
    {
        public HttpResponseMessage CreateResponse()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Body) };
            if (ContentType != null) response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType);
            response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);
            if (NextPage != null) response.Headers.TryAddWithoutValidation("X-Next-Page", NextPage);
            if (Links != null) response.Headers.TryAddWithoutValidation("Link", Links);
            return response;
        }
    }
}
