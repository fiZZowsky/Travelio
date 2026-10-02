using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Travelio.UI;
using Travelio.UI.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
var configuredUrl = builder.Configuration["ApiBaseUrl"];
var apiBaseUrl = string.IsNullOrWhiteSpace(configuredUrl) ? builder.HostEnvironment.BaseAddress : configuredUrl;
if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBase) || apiBase.Scheme is not ("http" or "https"))
    throw new InvalidOperationException("ApiBaseUrl musi być adresem HTTP lub HTTPS.");
builder.Services.AddScoped(_ => new HttpClient(new BrowserCredentialsHandler()) { BaseAddress = apiBase, Timeout = TimeSpan.FromSeconds(30) });
builder.Services.AddTravelio();
await builder.Build().RunAsync();

