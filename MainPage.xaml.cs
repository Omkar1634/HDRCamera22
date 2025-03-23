// MainPage.xaml.cs
using Microsoft.Maui.Controls;
using System;

namespace CameraBurstApp
{
    public partial class MainPage : ContentPage
    {
        private bool _isCapturing = false;

        public MainPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Request permissions when the page appears
            await CheckAndRequestPermissions();
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

        private void OnCaptureButtonClicked(object sender, EventArgs e)
        {
            // Toggle capturing state
            _isCapturing = !_isCapturing;

            if (_isCapturing)
            {
                CaptureButton.Text = "Stop Capture";
                // Start capture logic will be implemented later
            }
            else
            {
                CaptureButton.Text = "Start Capture";
                // Stop capture logic will be implemented later
            }
        }
    }
}