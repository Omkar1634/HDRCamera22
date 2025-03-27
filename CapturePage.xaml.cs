using Microsoft.Maui.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using CameraBurstApp.Services.Interfaces;
using CameraBurstApp.Models;

namespace CameraBurstApp
{
    public partial class CapturePage : ContentPage
    {
        private int shotCount = 0;
        private string subjectName;
        private int takeNumber;
        private string sessionFolder;

        // Services
        private readonly ICameraService cameraService;
        private readonly IFileService fileService;

        public CapturePage(string subjectName, int takeNumber)
        {
            InitializeComponent();

            // Store the subject information
            this.subjectName = subjectName;
            this.takeNumber = takeNumber;

            // Update the UI with subject info
            SubjectNameLabel.Text = subjectName;
            TakeNumberLabel.Text = $"Take {takeNumber}";

            // Get services from DI
            try
            {
                cameraService = DependencyService.Get<ICameraService>();
                if (cameraService == null)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: Failed to resolve ICameraService");
                }

                fileService = DependencyService.Get<IFileService>();
                if (fileService == null)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: Failed to resolve IFileService");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR resolving services: {ex.Message}\n{ex.StackTrace}");
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                // Check and request permissions
                bool permissionsGranted = await CheckAndRequestPermissions();

                if (permissionsGranted)
                {
                    // Get camera service - check if it's null
                    if (cameraService == null)
                    {
                        await DisplayAlert("Error", "Camera service is not available", "OK");
                        await Navigation.PopAsync();
                        return;
                    }

                    // Check if camera is available
                    if (cameraService.IsCameraAvailable())
                    {
                        // Create the session folder with metadata
                        var metadata = new SubjectMetadata
                        {
                            Name = subjectName,
                            Email = string.Empty, // This would be populated from MainPage
                            TakeNumber = takeNumber,
                            SessionDate = DateTime.Now
                        };

                        sessionFolder = await fileService.CreateSessionFolder(metadata);

                        // Make sure CameraView is not null
                        if (CameraView == null)
                        {
                            await DisplayAlert("Error", "Camera preview is not available", "OK");
                            await Navigation.PopAsync();
                            return;
                        }

                        // Initialize camera with our preview view container
                        try
                        {
                            cameraService.OpenCamera();

                            // Add logging to debug view container issues
                            System.Diagnostics.Debug.WriteLine($"CameraView type: {CameraView?.GetType().FullName ?? "null"}");
                            System.Diagnostics.Debug.WriteLine($"CameraView handler: {CameraView?.Handler?.GetType().FullName ?? "null"}");

                            // Make sure the handler is created before we pass the view to the camera service
                            if (CameraView.Handler == null)
                            {
                                System.Diagnostics.Debug.WriteLine("WARNING: CameraView Handler is null, waiting for handler to be created");
                                // Handler might not be created yet, we need to give it time
                                await Task.Delay(500); // Longer delay to ensure handler creation
                            }

                            // Get platform view from the Handler
                            var platformView = CameraView?.Handler?.PlatformView;
                            if (platformView == null)
                            {
                                System.Diagnostics.Debug.WriteLine("ERROR: Platform view is null after waiting");
                                await DisplayAlert("Error", "Could not access camera preview view", "OK");
                                await Navigation.PopAsync();
                                return;
                            }

                            // Pass the platform view to InitializeAsync
                            System.Diagnostics.Debug.WriteLine($"Platform view type: {platformView.GetType().FullName}");
                            await cameraService.InitializeAsync(platformView);

                            // Start the preview
                            await cameraService.StartPreviewAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Camera initialization error: {ex.Message}\n{ex.StackTrace}");
                            await DisplayAlert("Camera Error", $"Failed to initialize camera: {ex.Message}", "OK");
                            await Navigation.PopAsync();
                        }
                    }
                    else
                    {
                        await DisplayAlert("Camera Not Available",
                                         "Could not access the device camera.",
                                         "OK");
                        await Navigation.PopAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in OnAppearing: {ex.Message}\n{ex.StackTrace}");
                await DisplayAlert("Error", $"Failed to initialize camera: {ex.Message}", "OK");
                await Navigation.PopAsync();
            }
        }

        private async Task<bool> CheckAndRequestPermissions()
        {
            try
            {
                var cameraStatus = await Permissions.CheckStatusAsync<Permissions.Camera>();
                var storageStatus = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();

                System.Diagnostics.Debug.WriteLine($"Initial permission status - Camera: {cameraStatus}, Storage: {storageStatus}");

                if (cameraStatus != PermissionStatus.Granted)
                {
                    cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
                    System.Diagnostics.Debug.WriteLine($"Camera permission request result: {cameraStatus}");
                }

                if (storageStatus != PermissionStatus.Granted)
                {
                    storageStatus = await Permissions.RequestAsync<Permissions.StorageWrite>();
                    System.Diagnostics.Debug.WriteLine($"Storage permission request result: {storageStatus}");
                }

                // Additional permissions that might be needed on Android
#if ANDROID
                var mediaStatus = await Permissions.CheckStatusAsync<Permissions.Media>();
                if (mediaStatus != PermissionStatus.Granted)
                {
                    mediaStatus = await Permissions.RequestAsync<Permissions.Media>();
                    System.Diagnostics.Debug.WriteLine($"Media permission request result: {mediaStatus}");
                }
#endif

                bool allPermissionsGranted =
                    cameraStatus == PermissionStatus.Granted &&
                    storageStatus == PermissionStatus.Granted;

                if (!allPermissionsGranted)
                {
                    await DisplayAlert("Permission Denied",
                        "Camera and storage permissions are required for this app to function.",
                        "OK");
                    await Navigation.PopAsync();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Permission request error: {ex.Message}\n{ex.StackTrace}");
                await DisplayAlert("Permission Error",
                    "An error occurred while requesting permissions. Please ensure camera and storage permissions are enabled in your device settings.",
                    "OK");
                await Navigation.PopAsync();
                return false;
            }
        }

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();

            // Stop and clean up camera
            try
            {
                await cameraService.StopCaptureAsync();
                await cameraService.ShutdownAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping camera: {ex.Message}");
            }
        }

        private async void OnShutterButtonClicked(object sender, EventArgs e)
        {
            try
            {
                // Start capturing to our session folder
                await cameraService.StartCaptureAsync(sessionFolder);

                // Increment shot count
                shotCount++;
                ShotCountLabel.Text = $"{shotCount} shots captured";

                // Enable the Finish button after at least one shot
                if (shotCount > 0)
                {
                    //FinishButton.BorderDashArray = null;
                    FinishButton.BorderColor = null;
                    FinishButton.BackgroundColor = Colors.LightBlue;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Capture Error", $"Failed to capture photo: {ex.Message}", "OK");
            }
        }

        private async void OnCancelButtonClicked(object sender, EventArgs e)
        {
            // Confirm before cancelling if shots were taken
            if (shotCount > 0)
            {
                bool confirm = await DisplayAlert("Cancel Capture",
                                                "Are you sure you want to cancel? All captured photos will be lost.",
                                                "Yes", "No");
                if (!confirm)
                    return;

                // Delete capture folder
                try
                {
                    if (!string.IsNullOrEmpty(sessionFolder) && Directory.Exists(sessionFolder))
                    {
                        Directory.Delete(sessionFolder, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting folder: {ex.Message}");
                }
            }

            // Stop capture and navigate back to main page
            await cameraService.StopCaptureAsync();
            await Navigation.PopAsync();
        }

        private async void OnSettingsButtonClicked(object sender, EventArgs e)
        {
            // Camera settings dialog
            string[] options = new string[] { "Flash On", "Flash Off", "Flash Auto" };
            string action = await DisplayActionSheet("Camera Settings", "Cancel", null, options);

            // Apply selected setting
            if (!string.IsNullOrEmpty(action) && action != "Cancel")
            {
                await DisplayAlert("Setting Applied", $"{action} selected", "OK");
                // Note: The provided ICameraService doesn't have settings methods
                // If needed, you could add them to the interface
            }
        }

        private async void OnFinishButtonClicked(object sender, EventArgs e)
        {
            if (shotCount > 0)
            {
                // Stop capture
                await cameraService.StopCaptureAsync();

                // Navigate back to main page
                await Navigation.PopAsync();

                // In a real app you might use MessagingCenter or event to notify MainPage
                MessagingCenter.Send(this, "CaptureCompleted", new
                {
                    SubjectName = subjectName,
                    TakeNumber = takeNumber,
                    PhotoCount = shotCount,
                    FolderPath = sessionFolder
                });
            }
            else
            {
                await DisplayAlert("No Captures", "Please take at least one photo before finishing", "OK");
            }
        }
    }
}