using Microsoft.AspNetCore.Components.WebView;
namespace Travelio.Maui;
public partial class MainPage : ContentPage
{
    public MainPage() => InitializeComponent();
    private void OnUrlLoading(object? sender, UrlLoadingEventArgs args)
    {
        if (args.Url.Host is not "0.0.0.0" and not "0.0.0.1" && args.Url.Scheme is "https" or "http")
            args.UrlLoadingStrategy = UrlLoadingStrategy.OpenExternally;
    }
}
