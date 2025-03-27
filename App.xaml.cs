using CameraBurstApp.Platforms.Android;
using CameraBurstApp.Services;
using CameraBurstApp.Services.Interfaces;
using CameraBurstApp.Platforms.Android.Services.AndroidCameraService;
 
namespace CameraBurstApp
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Register services with DependencyService as a fallback
            RegisterServices();

            // Set up navigation
            MainPage = new NavigationPage(new MainPage());
        }

        private void RegisterServices()
        {
            try
            {
                // Register file service
                DependencyService.Register<IFileService, FileService>();

                // Register camera service for Android
#if ANDROID
                System.Diagnostics.Debug.WriteLine("Registering AndroidCameraService with DependencyService");
                DependencyService.Register<ICameraService, AndroidCameraService>();
#else
                System.Diagnostics.Debug.WriteLine("No camera service implementation for this platform");
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error registering services: {ex.Message}");
            }
        }

        protected override void OnStart()
        {
            // Handle when your app starts
        }

        protected override void OnSleep()
        {
            // Handle when your app sleeps
        }

        protected override void OnResume()
        {
            // Handle when your app resumes
        }
    }
}