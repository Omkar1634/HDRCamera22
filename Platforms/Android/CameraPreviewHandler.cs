using Android.Content;
using Android.Views;
using CameraBurstApp.Controls;
using Microsoft.Maui.Handlers;
using Android.Views;
using Android.Graphics;
using CameraBurstApp.Platforms.Android.Services.AndroidCameraService; 

namespace CameraBurstApp.Platforms.Android.CameraPreviewHandler
{
    public class CameraPreviewHandler : ViewHandler<CameraPreview, TextureView>
    {
        private TextureView textureView;

        public static PropertyMapper<CameraPreview, CameraPreviewHandler> PropertyMapper = new PropertyMapper<CameraPreview, CameraPreviewHandler>(ViewHandler.ViewMapper)
        {
            [nameof(CameraPreview.CameraId)] = MapCameraId
        };

        public CameraPreviewHandler() : base(PropertyMapper)
        {
        }

        protected override TextureView CreatePlatformView()
        {
            System.Diagnostics.Debug.WriteLine("CreatePlatformView called");

            // Create TextureView with explicit context
            textureView = new TextureView(Context);

            // Configure layout parameters
            var layoutParams = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent  // You can adjust height as needed
            );
            textureView.LayoutParameters = layoutParams;

            System.Diagnostics.Debug.WriteLine($"TextureView created: {textureView != null}");

            // Set camera surface listener with the camera ID
            textureView.SurfaceTextureListener = new CameraSurfaceTextureListener(
                Context,
                VirtualView?.CameraId ?? "1");

            return textureView;
        }

        private static void MapCameraId(CameraPreviewHandler handler, CameraPreview preview)
        {
            // If the camera ID changes, we need to recreate the surface texture listener
            if (handler.textureView?.SurfaceTextureListener is CameraSurfaceTextureListener oldListener)
            {
                // Handle cleanup if needed
            }

            // Create new listener
            handler.textureView.SurfaceTextureListener = new CameraSurfaceTextureListener(
                handler.Context,
                preview?.CameraId ?? "1");
        }
    }
}