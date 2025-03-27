using Microsoft.Maui.Controls;

namespace CameraBurstApp.Controls
{
    public class CameraPreview : View
    {
        // Add any properties you need to control the camera
        public static readonly BindableProperty CameraIdProperty =
            BindableProperty.Create(nameof(CameraId), typeof(string), typeof(CameraPreview), "1");

        public string CameraId
        {
            get { return (string)GetValue(CameraIdProperty); }
            set { SetValue(CameraIdProperty, value); }
        }
    }
}