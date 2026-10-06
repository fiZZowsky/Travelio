using System.Globalization;
using System.Text.Json;

namespace Travelio.Application.Localization;

/// <summary>Embedded resources shared by web, native hosts, exports and server validation.</summary>
public static class L
{
    private static readonly Lazy<Dictionary<string, string>> English = new(() =>
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("Travelio.Application.Localization.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });
    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "pl";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "en" ? "en-GB" : "pl-PL");
    public static string T(string text) => Language == "en" && English.Value.TryGetValue(text, out var translated) ? translated : text;
    public static string F(FormattableString text) => string.Format(Culture, T(text.Format), text.GetArguments());
    public static string Name(string original, IReadOnlyDictionary<string, string>? names)
    {
        if (names is not null && names.TryGetValue(Language, out var localized) && !string.IsNullOrWhiteSpace(localized)) return localized;
        if (names is not null && names.TryGetValue("en", out var english) && !string.IsNullOrWhiteSpace(english)) return english;
        if (names is not null && names.TryGetValue("pl", out var polish) && !string.IsNullOrWhiteSpace(polish)) return polish;
        return T(original);
    }
}
