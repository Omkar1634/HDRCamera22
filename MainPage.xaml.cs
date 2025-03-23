// MainPage.xaml.cs
using CameraBurstApp.Models;
using CameraBurstApp.Services;
using CameraBurstApp.Services.Interfaces;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CameraBurstApp
{
    public partial class MainPage : ContentPage
    {
        private bool _isCapturing = false;
        private ICameraService _cameraService;
        private IFileService _fileService;
        private Dictionary<string, int> _subjectTakeCounts = new Dictionary<string, int>();

        public MainPage()
        {
            InitializeComponent();

            // Initialize services
            _cameraService = DependencyService.Get<ICameraService>();
            _fileService = new FileService();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Request permissions when the page appears
            await CheckAndRequestPermissions();

            // Initialize camera if available
            if (_cameraService.IsCameraAvailable())
            {
                await _cameraService.InitializeAsync(CameraPreviewContainer);
                await _cameraService.StartPreviewAsync();
            }
            else
            {
                await DisplayAlert("Error", "No camera available on this device.", "OK");
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
            var storageStatus = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();

            if (cameraStatus != PermissionStatus.Granted)
            {
                cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
            }

            if (storageStatus != PermissionStatus.Granted)
            {
                storageStatus = await Permissions.RequestAsync<Permissions.StorageWrite>();
            }

            if (cameraStatus != PermissionStatus.Granted || storageStatus != PermissionStatus.Granted)
            {
                await DisplayAlert("Permission Required",
                    "Camera and storage permissions are required for this app to function.", "OK");
            }
        }

        private async void OnCaptureButtonClicked(object sender, EventArgs e)
        {
            // Toggle capturing state
            _isCapturing = !_isCapturing;

            if (_isCapturing)
            {
                // Validate subject info
                string subjectName = SubjectNameEntry.Text?.Trim();
                string subjectEmail = SubjectEmailEntry.Text?.Trim();

                if (string.IsNullOrEmpty(subjectName))
                {
                    await DisplayAlert("Error", "Please enter a subject name.", "OK");
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

                // Start capture
                await _cameraService.StartCaptureAsync(sessionFolder);
                CaptureButton.Text = "Stop Capture";

                // Disable input fields during capture
                SubjectNameEntry.IsEnabled = false;
                SubjectEmailEntry.IsEnabled = false;
            }
            else
            {
                // Stop capture
                await _cameraService.StopCaptureAsync();
                CaptureButton.Text = "Start Capture";

                // Re-enable input fields
                SubjectNameEntry.IsEnabled = true;
                SubjectEmailEntry.IsEnabled = true;
            }
        }
    }
}