/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>One request a provider made, as the fake saw it.</summary>
internal sealed record SeenRequest(HttpMethod Method, string Url, string? Authorization, string? Body, string UserAgent);

/// <summary>
/// A store's HTTP service, played by a table of routes: the first route whose text is in the
/// request's address answers. Anything no route knows is a test failure, not a silent 404.
/// </summary>
internal sealed class FakeStore : HttpMessageHandler, IHttpClientFactory
{
    private readonly List<(string Fragment, HttpMethod? Method, Func<SeenRequest, HttpResponseMessage> Answer)> _routes = new();
    private readonly ConcurrentQueue<SeenRequest> _seen = new();

    public IReadOnlyList<SeenRequest> Requests => _seen.ToList();

    public IEnumerable<SeenRequest> RequestsTo(string fragment) => Requests.Where(r => r.Url.Contains(fragment, StringComparison.Ordinal));

    /// <summary>Answers requests whose address contains <paramref name="fragment"/> with a fixed JSON body.</summary>
    public FakeStore Json(string fragment, string json, HttpStatusCode status = HttpStatusCode.OK, HttpMethod? method = null) =>
        Route(fragment, _ => Reply(status, json), method);

    public FakeStore Route(string fragment, Func<SeenRequest, HttpResponseMessage> answer, HttpMethod? method = null)
    {
        _routes.Add((fragment, method, answer));
        return this;
    }

    public static HttpResponseMessage Reply(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var seen = new SeenRequest(
            request.Method,
            request.RequestUri!.ToString(),
            request.Headers.Authorization?.ToString(),
            request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Headers.UserAgent.ToString());
        _seen.Enqueue(seen);

        foreach (var (fragment, method, answer) in _routes)
        {
            if (seen.Url.Contains(fragment, StringComparison.Ordinal) == true && (method == null || method == request.Method))
            {
                return answer(seen);
            }
        }
        throw new InvalidOperationException($"No route for {request.Method} {seen.Url}");
    }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
}

/// <summary>A session store that keeps what it is given in memory, and counts.</summary>
internal sealed class MemorySessionStore : ISessionStore
{
    private readonly Dictionary<string, object> _saved = new();

    public int Saves { get; private set; }

    public int Deletes { get; private set; }

    public T? Load<T>(string path)
        where T : class => _saved.TryGetValue(path, out var session) ? session as T : null;

    public void Save<T>(string path, T session)
    {
        _saved[path] = session!;
        Saves++;
    }

    public void Delete(string path)
    {
        _saved.Remove(path);
        Deletes++;
    }

    public bool Has(string path) => _saved.ContainsKey(path);
}
