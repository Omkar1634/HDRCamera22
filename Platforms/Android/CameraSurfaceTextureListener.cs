using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.OS;
using Android.Views;
using Java.Lang;
using System;
using Handler = Android.OS.Handler;
using Size = Android.Util.Size;

namespace CameraBurstApp.Platforms.Android
{
    public class CameraSurfaceTextureListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
    {
        private readonly Context context;
        private readonly string cameraId;
        private CameraDevice cameraDevice;
        private CameraCaptureSession captureSession;
        private CaptureRequest.Builder previewRequestBuilder;
        private Handler backgroundHandler;
        private HandlerThread backgroundThread;
        private Size previewSize;
        private bool surfaceAvailable = false;

        public CameraSurfaceTextureListener(Context context, string cameraId)
        {
            this.context = context;
            this.cameraId = cameraId;
            StartBackgroundThread();
        }

        public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
        {
            if (surfaceAvailable)
                return;

            surfaceAvailable = true;
            Console.WriteLine($"Surface available: {width}x{height}");

            try
            {
                backgroundHandler.Post(() => {
                    try
                    {
                        OpenCamera(surface, width, height);
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine($"Error opening camera: {ex.Message}");
                    }
                });
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Error in OnSurfaceTextureAvailable: {ex.Message}");
            }
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
        {
            CloseCamera();
            StopBackgroundThread();
            surfaceAvailable = false;
            return true;
        }

        public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
        {
            // Adjust for size changes
            Console.WriteLine($"Surface size changed: {width}x{height}");
        }

        public void OnSurfaceTextureUpdated(SurfaceTexture surface)
        {
            // Called when the content of the surface changes
        }

        private void OpenCamera(SurfaceTexture surface, int width, int height)
        {
            try
            {
                // Get camera manager
                CameraManager manager = (CameraManager)context.GetSystemService(Context.CameraService);

                // Get camera characteristics
                CameraCharacteristics characteristics = manager.GetCameraCharacteristics(cameraId);
                StreamConfigurationMap map = (StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap);

                // Get optimal preview size
                previewSize = GetOptimalSize(map.GetOutputSizes(Java.Lang.Class.FromType(typeof(SurfaceTexture))), width, height);

                // Configure the texture view
                surface.SetDefaultBufferSize(previewSize.Width, previewSize.Height);

                // Open the camera
                StateCallback stateCallback = new StateCallback(this);
                manager.OpenCamera(cameraId, stateCallback, backgroundHandler);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Error opening camera: {ex.Message}");
            }
        }

        private Size GetOptimalSize(Size[] sizes, int width, int height)
        {
            if (sizes == null || sizes.Length == 0)
                return new Size(width, height);

            double targetRatio = (double)width / height;
            Size optimalSize = null;
            double minDiff = double.MaxValue;

            foreach (var size in sizes)
            {
                double ratio = (double)size.Width / size.Height;
                double diff =  System.Math.Abs(ratio - targetRatio);

                if (diff < minDiff)
                {
                    optimalSize = size;
                    minDiff = diff;
                }
            }

            return optimalSize ?? sizes[0];
        }

        private void CreateCameraPreview(SurfaceTexture texture)
        {
            try
            {
                // Set the buffer size
                texture.SetDefaultBufferSize(previewSize.Width, previewSize.Height);

                // Create a surface
                Surface surface = new Surface(texture);

                // Create a capture request
                previewRequestBuilder = cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                previewRequestBuilder.AddTarget(surface);

                // Create a capture session
                SessionStateCallback sessionCallback = new SessionStateCallback(this);

                // Use a List<Surface> instead of Java.Util.ArrayList
                var surfaces = new System.Collections.Generic.List<Surface> { surface };
                cameraDevice.CreateCaptureSession(surfaces, sessionCallback, backgroundHandler);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Error creating preview: {ex.Message}");
            }
        }

        private void UpdatePreview()
        {
            try
            {
                previewRequestBuilder.Set(CaptureRequest.ControlMode, (int)ControlMode.Auto);
                captureSession.SetRepeatingRequest(previewRequestBuilder.Build(), null, backgroundHandler);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Error updating preview: {ex.Message}");
            }
        }

        private void CloseCamera()
        {
            if (captureSession != null)
            {
                captureSession.Close();
                captureSession = null;
            }

            if (cameraDevice != null)
            {
                cameraDevice.Close();
                cameraDevice = null;
            }
        }

        private void StartBackgroundThread()
        {
            backgroundThread = new HandlerThread("CameraBackground");
            backgroundThread.Start();
            backgroundHandler = new Handler(backgroundThread.Looper);
        }

        private void StopBackgroundThread()
        {
            if (backgroundThread != null)
            {
                backgroundThread.QuitSafely();
                try
                {
                    backgroundThread.Join();
                    backgroundThread = null;
                    backgroundHandler = null;
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine($"Error stopping background thread: {ex.Message}");
                }
            }
        }

        // Camera state callback
        private class StateCallback : CameraDevice.StateCallback
        {
            private readonly CameraSurfaceTextureListener parent;
            private readonly SurfaceTexture surfaceTexture;

            public StateCallback(CameraSurfaceTextureListener parent)
            {
                this.parent = parent;
            }

            public override void OnOpened(CameraDevice camera)
            {
                parent.cameraDevice = camera;

                // The TextureView's surface texture is what we need to use
                var textureViews = new System.Collections.Generic.List<TextureView>();
                FindTextureViews(parent.context, textureViews);

                if (textureViews.Count > 0 && textureViews[0].SurfaceTexture != null)
                {
                    parent.CreateCameraPreview(textureViews[0].SurfaceTexture);
                }
                else
                {
                    Console.WriteLine("Could not find texture view or surface texture");
                }
            }

            private void FindTextureViews(Context context, System.Collections.Generic.List<TextureView> textureViews)
            {
                if (context is global::Android.App.Activity activity)
                {
                    var rootView = activity.Window.DecorView.RootView as ViewGroup;
                    if (rootView != null)
                    {
                        FindTextureViewsRecursive(rootView, textureViews);
                    }
                }
            }

            private void FindTextureViewsRecursive(ViewGroup viewGroup, System.Collections.Generic.List<TextureView> textureViews)
            {
                for (int i = 0; i < viewGroup.ChildCount; i++)
                {
                    var child = viewGroup.GetChildAt(i);
                    if (child is TextureView textureView)
                    {
                        textureViews.Add(textureView);
                    }
                    else if (child is ViewGroup childGroup)
                    {
                        FindTextureViewsRecursive(childGroup, textureViews);
                    }
                }
            }

            public override void OnDisconnected(CameraDevice camera)
            {
                camera.Close();
                parent.cameraDevice = null;
            }

            public override void OnError(CameraDevice camera, CameraError error)
            {
                camera.Close();
                parent.cameraDevice = null;
                Console.WriteLine($"Camera error: {error}");
            }
        }

        // Session state callback
        private class SessionStateCallback : CameraCaptureSession.StateCallback
        {
            private readonly CameraSurfaceTextureListener parent;

            public SessionStateCallback(CameraSurfaceTextureListener parent)
            {
                this.parent = parent;
            }

            public override void OnConfigured(CameraCaptureSession session)
            {
                parent.captureSession = session;
                parent.UpdatePreview();
            }

            public override void OnConfigureFailed(CameraCaptureSession session)
            {
                Console.WriteLine("Camera session configuration failed");
            }
        }
    }
}