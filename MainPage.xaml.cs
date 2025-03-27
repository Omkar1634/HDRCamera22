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
        private int currentTakeCount = 0;

        public MainPage()
        {
            InitializeComponent();
            UpdateTakeCountLabel();
        }

        private void UpdateTakeCountLabel()
        {
            TakeCountLabel.Text = $"Take: {currentTakeCount}";
        }

        private void SubjectNameEntry_TextChanged(object sender, TextChangedEventArgs e)
        {
            // You can add validation or other logic here
        }

        private async void OnCaptureButtonClicked(object sender, EventArgs e)
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(SubjectNameEntry.Text))
            {
                await DisplayAlert("Error", "Please enter a subject name", "OK");
                return;
            }

            // Navigate to the capture page
            await Navigation.PushAsync(new CapturePage(
                SubjectNameEntry.Text,
                currentTakeCount));
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // This would be where you increment the take count when returning from a capture
            // For demonstration, let's assume we increment it
            // In a real app, you would track this per subject and only increment when needed
            currentTakeCount++;
            UpdateTakeCountLabel();
        }

        

        //    private async void OnCaptureButtonClicked(object sender, EventArgs e)
        //    {
        //        // Toggle capturing state
        //        _isCapturing = !_isCapturing;

        //        if (_isCapturing)
        //        {
        //            // Validate subject info
        //            string subjectName =   SubjectNameEntry.Text?.Trim();
        //            string subjectEmail = SubjectEmailEntry.Text?.Trim();

        //            if (string.IsNullOrEmpty(subjectName))
        //            {
        //                await DisplayAlert("Error", "Please enter a subject name.", "OK");
        //                _isCapturing = false;
        //                return;
        //            }

        //            // Check if camera is initialized
        //            if (_cameraService == null)
        //            {
        //                System.Diagnostics.Debug.WriteLine("Camera service is null, can't start capture");
        //                await DisplayAlert("Error", "Camera not initialized", "OK");
        //                _isCapturing = false;
        //                return;
        //            }

        //            // Get take number for this subject
        //            if (!_subjectTakeCounts.ContainsKey(subjectName))
        //            {
        //                _subjectTakeCounts[subjectName] = 1;
        //            }
        //            else
        //            {
        //                _subjectTakeCounts[subjectName]++;
        //            }

        //            // Create metadata
        //            var metadata = new SubjectMetadata
        //            {
        //                Name = subjectName,
        //                Email = subjectEmail,
        //                TakeNumber = _subjectTakeCounts[subjectName],
        //                Timestamp = DateTime.Now
        //            };

        //            // Create session folder
        //            string sessionFolder = await _fileService.CreateSessionFolder(metadata);
        //            System.Diagnostics.Debug.WriteLine($"Created session folder: {sessionFolder}");

        //            try
        //            {
        //                // Start capture
        //                await _cameraService.StartCaptureAsync(sessionFolder);
        //                CaptureButton.Text = "Stop Capture";

        //                // Disable input fields during capture
        //                SubjectNameEntry.IsEnabled = false;
        //                SubjectEmailEntry.IsEnabled = false;
        //            }
        //            catch (Exception ex)
        //            {
        //                System.Diagnostics.Debug.WriteLine($"Error starting capture: {ex.Message}");
        //                await DisplayAlert("Error", $"Failed to start capture: {ex.Message}", "OK");
        //                _isCapturing = false;
        //            }
        //        }
        //        else
        //        {
        //            // Stop capture
        //            if (_cameraService != null)
        //            {
        //                await _cameraService.StopCaptureAsync();
        //            }
        //            CaptureButton.Text = "Start Capture";

        //            // Re-enable input fields
        //            SubjectNameEntry.IsEnabled = true;
        //            SubjectEmailEntry.IsEnabled = true;
        //        }
        //    }

    }
}