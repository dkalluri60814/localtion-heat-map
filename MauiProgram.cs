using LocationHeatMap.Services;
using LocationHeatMap.ViewModels;
using LocationHeatMap.Views;
using Microsoft.Extensions.Logging;

namespace LocationHeatMap;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            // Registers the handlers of the Microsoft.Maui.Controls.Maps controls (Map, Pin, Circle ...).
            .UseMauiMaps()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Dependency injection: one database connection and one tracker for the whole app.
        builder.Services.AddSingleton<ILocationRepository, LocationRepository>();
        builder.Services.AddSingleton<ILocationTrackingService, LocationTrackingService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
