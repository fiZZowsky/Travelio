using System.Globalization;
using Microsoft.JSInterop;
using Travelio.Application;
using Travelio.Application.Localization;

namespace Travelio.UI.Services;

public sealed class LanguageService(ILocalStore store, HttpClient http, IJSRuntime js)
{
    public string Language { get; private set; } = "pl";
    public async Task InitializeAsync()
    {
        Language = await store.GetAsync<string>("travelio.language") == "en" ? "en" : "pl";
        Apply();
        await js.InvokeVoidAsync("travelio.language.set", Language);
    }
    public async Task SetAsync(string language)
    {
        if (language is not ("pl" or "en")) return;
        await store.SetAsync("travelio.language", language);
        Language = language;
        Apply();
        await js.InvokeVoidAsync("travelio.language.set", Language);
    }
    public void Apply()
    {
        var culture = CultureInfo.GetCultureInfo(Language == "en" ? "en-GB" : "pl-PL");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        http.DefaultRequestHeaders.AcceptLanguage.Clear();
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(Language);
    }
}
