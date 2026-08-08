using System.Net;

namespace TaskSphere.Tests.GitHub;

/// <summary>
/// Records every request and replays a queued response per call, so tests can assert on what
/// was actually sent — headers, host, credential — rather than only on the return value.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> AuthorizationHeaders { get; } = new();

    public int CallCount => Requests.Count;

    public FakeHttpMessageHandler Enqueue(HttpStatusCode status, string body = "", Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
            configure?.Invoke(response);
            return response;
        });

        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        AuthorizationHeaders.Add(request.Headers.Authorization?.Parameter);

        if (_responses.Count == 0)
            throw new InvalidOperationException($"No queued response for {request.Method} {request.RequestUri}");

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
