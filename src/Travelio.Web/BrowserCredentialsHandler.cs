using Microsoft.AspNetCore.Components.WebAssembly.Http;

/// <summary>Browser-only cookie transport; native hosts use their own cookie container.</summary>
internal sealed class BrowserCredentialsHandler : DelegatingHandler
{
    public BrowserCredentialsHandler() : base(new HttpClientHandler()) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
