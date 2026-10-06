using Microsoft.Extensions.DependencyInjection;
using Travelio.Application;
using Travelio.Application.Services;

namespace Travelio.UI.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTravelio(this IServiceCollection services)
    {
        services.AddSingleton<IDestinationCatalog, DemoDestinationCatalog>();
        services.AddSingleton<IRecommendationService, RecommendationService>();
        services.AddSingleton<IItineraryPlanner, ItineraryPlanner>();
        services.AddSingleton<IBudgetService, BudgetService>();
        services.AddSingleton<IPreparationService, PreparationService>();
        services.AddScoped<TravelDataClient>();
        services.AddScoped<LanguageService>();
        services.AddScoped<IRegionalAlertProvider, CachedAlertProvider>();
        services.AddSingleton<IScheduleMonitor, ScheduleMonitor>();
        services.AddScoped<ApiClient>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<ReminderCoordinator>();
        services.AddScoped<ILocalStore, BrowserLocalStore>();
        services.AddScoped<INotificationService, BrowserNotificationService>();
        return services;
    }
}

