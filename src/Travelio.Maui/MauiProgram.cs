using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Travelio.Application;
using Travelio.Maui.Services;
using Travelio.UI.Services;
namespace Travelio.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddTravelio();
        builder.Services.AddSingleton<ILocalStore, FileLocalStore>();
#if ANDROID
        builder.Services.AddSingleton<INotificationService, Platforms.Android.AndroidNotificationService>();
#elif IOS || MACCATALYST
        builder.Services.AddSingleton<INotificationService, AppleNotificationService>();
#else
        builder.Services.AddSingleton<INotificationService, DesktopNotificationService>();
#endif
        builder.Services.AddScoped(_ => new HttpClient(new HttpClientHandler
        {
            UseCookies = true, CookieContainer = new CookieContainer()
        }) { BaseAddress = GetApiBaseUrl(), Timeout = TimeSpan.FromSeconds(30) });
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
    private static Uri GetApiBaseUrl()
    {
#if DEBUG && ANDROID
        return new Uri("http://10.0.2.2:5180/");
#elif DEBUG
        return new Uri("http://localhost:5180/");
#else
        using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").GetAwaiter().GetResult();
        using var settings = JsonDocument.Parse(stream);
        var url = settings.RootElement.GetProperty("ApiBaseUrl").GetString();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("Wskaż adres HTTPS wdrożonego API w Resources/Raw/appsettings.json.");
        return uri;
#endif
    }
}
