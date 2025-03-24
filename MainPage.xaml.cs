using CameraBurstApp.Models;
using CameraBurstApp.Services;
using CameraBurstApp.Services.Interfaces;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui;

namespace CameraBurstApp
{
    public partial class MainPage : ContentPage
    {
        private bool _isCapturing = false;
        private ICameraService _cameraService;
        private IFileService _fileService;
        private Dictionary<string, int> _subjectTakeCounts = new Dictionary<string, int>();

        // Default constructor for XAML previewer and fallback
        public MainPage()
        {
            InitializeComponent();

            // Try to resolve services from the DI container
            if (IPlatformApplication.Current != null)
            {
                _cameraService = IPlatformApplication.Current.Services.GetService<ICameraService>();
                _fileService = IPlatformApplication.Current.Services.GetService<IFileService>();
            }

            System.Diagnostics.Debug.WriteLine($"MainPage created with services: Camera={_cameraService != null}, File={_fileService != null}");
        }

        // Constructor with dependency injection
        public MainPage(ICameraService cameraService, IFileService fileService)
        {
            InitializeComponent();
            _cameraService = cameraService;
            _fileService = fileService;

            System.Diagnostics.Debug.WriteLine("MainPage created with DI services");
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                // Request permissions when the page appears
                await CheckAndRequestPermissions();

                // If services weren't injected, try to get them now
                if (_cameraService == null)
                {
                    System.Diagnostics.Debug.WriteLine("Camera service was null, trying to resolve");
                    _cameraService = IPlatformApplication.Current.Services.GetService<ICameraService>();
                }

                if (_fileService == null)
                {
                    System.Diagnostics.Debug.WriteLine("File service was null, creating new instance");
                    _fileService = new FileService();
                }

                // Initialize camera if available
                if (_cameraService != null && _cameraService.IsCameraAvailable())
                {
                    System.Diagnostics.Debug.WriteLine("Camera is available, initializing...");
                    await _cameraService.InitializeAsync(CameraPreviewContainer);
                    System.Diagnostics.Debug.WriteLine("Starting camera preview...");
                    await _cameraService.StartPreviewAsync();
                    System.Diagnostics.Debug.WriteLine("Camera preview started");
                }
                else
                {
                    string errorReason = _cameraService == null
                        ? "Camera service is null"
                        : "No camera available on this device";

                    System.Diagnostics.Debug.WriteLine(errorReason);
                    await DisplayAlert("Error", errorReason, "OK");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnAppearing: {ex.Message}");
                await DisplayAlert("Error", $"Camera initialization failed: {ex.Message}", "OK");
            }
        }

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();

            // Clean up camera resources
            if (_cameraService != null)
            {
                await _cameraService.ShutdownAsync();
            }
        }

        private async Task CheckAndRequestPermissions()
        {
            var cameraStatus = await Permissions.CheckStatusAsync<Permissions.Camera>();
            var storageReadStatus = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
            var storageWriteStatus = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();

            if (cameraStatus != PermissionStatus.Granted)
            {
                cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
            }

            if (storageReadStatus != PermissionStatus.Granted)
            {
                storageReadStatus = await Permissions.RequestAsync<Permissions.StorageRead>();
            }

            if (storageWriteStatus != PermissionStatus.Granted)
            {
                storageWriteStatus = await Permissions.RequestAsync<Permissions.StorageWrite>();
            }

            if (cameraStatus != PermissionStatus.Granted ||
                storageReadStatus != PermissionStatus.Granted ||
                storageWriteStatus != PermissionStatus.Granted)
            {
                await DisplayAlert("Permission Required",
                    "Camera and storage permissions are required for this app to function.", "OK");
            }
        }
        private async void SubjectNameEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.NewTextValue) && _cameraService != null && _cameraService.IsCameraAvailable())
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Initializing camera in response to user input");
                    await _cameraService.InitializeAsync(CameraPreviewContainer);
                    await _cameraService.StartPreviewAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error initializing camera: {ex.Message}");
                }
            }
        }
        private async void OnCaptureButtonClicked(object sender, EventArgs e)
        {
            // Toggle capturing state
            _isCapturing = !_isCapturing;

            if (_isCapturing)
            {
                // Validate subject info
                string subjectName =   SubjectNameEntry.Text?.Trim();
                string subjectEmail = SubjectEmailEntry.Text?.Trim();

                if (string.IsNullOrEmpty(subjectName))
                {
                    await DisplayAlert("Error", "Please enter a subject name.", "OK");
                    _isCapturing = false;
                    return;
                }

                // Check if camera is initialized
                if (_cameraService == null)
                {
                    System.Diagnostics.Debug.WriteLine("Camera service is null, can't start capture");
                    await DisplayAlert("Error", "Camera not initialized", "OK");
                    _isCapturing = false;
                    return;
                }

                // Get take number for this subject
                if (!_subjectTakeCounts.ContainsKey(subjectName))
                {
                    _subjectTakeCounts[subjectName] = 1;
                }
                else
                {
                    _subjectTakeCounts[subjectName]++;
                }

                // Create metadata
                var metadata = new SubjectMetadata
                {
                    Name = subjectName,
                    Email = subjectEmail,
                    TakeNumber = _subjectTakeCounts[subjectName],
                    Timestamp = DateTime.Now
                };

                // Create session folder
                string sessionFolder = await _fileService.CreateSessionFolder(metadata);
                System.Diagnostics.Debug.WriteLine($"Created session folder: {sessionFolder}");

                try
                {
                    // Start capture
                    await _cameraService.StartCaptureAsync(sessionFolder);
                    CaptureButton.Text = "Stop Capture";

                    // Disable input fields during capture
                    SubjectNameEntry.IsEnabled = false;
                    SubjectEmailEntry.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error starting capture: {ex.Message}");
                    await DisplayAlert("Error", $"Failed to start capture: {ex.Message}", "OK");
                    _isCapturing = false;
                }
            }
            else
            {
                // Stop capture
                if (_cameraService != null)
                {
                    await _cameraService.StopCaptureAsync();
                }
                CaptureButton.Text = "Start Capture";

                // Re-enable input fields
                SubjectNameEntry.IsEnabled = true;
                SubjectEmailEntry.IsEnabled = true;
            }
        }

    }
}