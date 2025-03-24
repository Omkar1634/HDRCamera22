using CameraBurstApp.Services;
using CameraBurstApp.Services.Interfaces;
#if ANDROID
using CameraBurstApp.Platforms.Android.Services;
#endif
using Microsoft.Extensions.Logging;

namespace CameraBurstApp
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Register services
            builder.Services.AddSingleton<FileService, FileService>();

#if ANDROID
            builder.Services.AddSingleton<ICameraService, AndroidCameraService>();
#endif

            // Register pages
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<AppShell>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}