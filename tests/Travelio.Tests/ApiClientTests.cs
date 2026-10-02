using System.Net;
using System.Net.Http.Json;
using Travelio.UI.Services;

namespace Travelio.Tests;

public sealed class ApiClientTests
{
    [Fact]
    public async Task Expired_csrf_is_refreshed_once_without_retrying_other_failures()
    {
        var handler = new CsrfHandler();
        var client = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") });
        await client.RegisterAsync("test@example.test", "TravelioTest2026!");
        Assert.Equal(2, handler.TokenRequests);
        Assert.Equal(2, handler.Writes);
        handler.ServerFailure = true;
        await Assert.ThrowsAsync<ApiException>(() => client.RegisterAsync("test@example.test", "TravelioTest2026!"));
        Assert.Equal(3, handler.Writes);
    }

    private sealed class CsrfHandler : HttpMessageHandler
    {
        public int TokenRequests, Writes;
        public bool ServerFailure;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { token = "token-" + ++TokenRequests }) });
            Writes++;
            var response = new HttpResponseMessage(ServerFailure ? HttpStatusCode.ServiceUnavailable : Writes == 1 ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            { Content = JsonContent.Create(new { message = "Test" }) };
            if (Writes == 1) response.Headers.Add("X-Travelio-Csrf-Expired", "true");
            Assert.Equal("token-" + TokenRequests, request.Headers.GetValues("X-Travelio-CSRF").Single());
            return Task.FromResult(response);
        }
    }
}
