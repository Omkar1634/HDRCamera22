using Microsoft.Extensions.Logging;
using CameraBurstApp.Services.Interfaces;
using CameraBurstApp.Services;
using CameraBurstApp.Controls;
#if ANDROID
using CameraBurstApp.Platforms.Android.CameraPreviewHandler;
using CameraBurstApp.Platforms.Android.Services.AndroidCameraService;
#endif

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

            // Register handlers
            builder.ConfigureMauiHandlers(handlers =>
            {
                handlers.AddHandler<CameraBurstApp.Controls.CameraPreview, CameraBurstApp.Platforms.Android.CameraPreviewHandler.CameraPreviewHandler>();
            });

            
            // Register services
            builder.Services.AddSingleton<IFileService, FileService>();

#if ANDROID
            System.Diagnostics.Debug.WriteLine("Registering AndroidCameraService");
            builder.Services.AddSingleton<ICameraService, AndroidCameraService>();
#endif

            // Register pages
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<CapturePage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}