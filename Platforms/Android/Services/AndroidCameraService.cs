using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Java.IO;
using Java.Lang;
using Java.Util.Concurrent;
using Microsoft.Maui.Platform;
using System;
using System.Threading.Tasks;
using IHandler = Android.OS.Handler;
using HandlerThread = Android.OS.HandlerThread;
using Size = Android.Util.Size;
using Java.Nio;
using CameraBurstApp.Services.Interfaces;
using RectF = global::Android.Graphics.RectF;
using Matrix = global::Android.Graphics.Matrix;
using CameraBurstApp.Platforms.Android.Services.AndroidCameraService;
using static CameraBurstApp.Platforms.Android.Services.AndroidCameraService.AndroidCameraService.BracketedCaptureListener;

namespace CameraBurstApp.Platforms.Android.Services.AndroidCameraService
{
    public class AndroidCameraService : ICameraService
    {
        private CameraDevice cameraDevice;
        private CameraCaptureSession previewSession;
        private CaptureRequest.Builder previewRequestBuilder;
        private TextureView textureView;
        private Size imageDimension;
        private ImageReader imageReader;
        private IHandler backgroundHandler;
        private HandlerThread backgroundThread;
        private TaskCompletionSource<bool> captureTaskSource;
        private string currentSessionFolder;
        private int photoCounter = 0;
        // Implementation of ICameraService interface methods
        public async Task InitializeAsync(object previewView)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"InitializeAsync called with view type: {previewView?.GetType().FullName ?? "null"}");

                // Start background thread first to ensure handler is available
                StartBackgroundThread();

                // Handle direct TextureView
                if (previewView is TextureView directTextureView)
                {
                    textureView = directTextureView;
                    System.Diagnostics.Debug.WriteLine("Preview view is a TextureView directly");
                    // Set up the surface listener
                    textureView.SurfaceTextureListener = new UnifiedSurfaceTextureListener(this);
                    return;
                }

                // Handle different types of view containers
                ViewGroup viewContainer = null;

                if (previewView is ViewGroup androidViewGroup)
                {
                    viewContainer = androidViewGroup;
                    System.Diagnostics.Debug.WriteLine("Preview view is already an Android ViewGroup");
                }
                else if (previewView is Microsoft.Maui.Controls.Layout mauilayout)
                {
                    var handler = mauilayout.Handler;
                    if (handler?.PlatformView is ViewGroup platformView)
                    {
                        viewContainer = platformView;
                        System.Diagnostics.Debug.WriteLine("Successfully got platform view from MAUI Layout");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to get platform view from MAUI Layout");
                    }
                }
                else if (previewView is Microsoft.Maui.Controls.View mauiView)
                {
                    var handler = mauiView.Handler;
                    if (handler?.PlatformView is ViewGroup platformView)
                    {
                        viewContainer = platformView;
                        System.Diagnostics.Debug.WriteLine("Successfully got platform view from MAUI View");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to get platform view from MAUI View");
                    }
                }

                if (viewContainer == null)
                {
                    throw new ArgumentException("Preview view must be a ViewGroup or a MAUI view with a ViewGroup platform view", nameof(previewView));
                }

                // Create and add TextureView to the container
                textureView = new TextureView(Platform.CurrentActivity);

                // Set LayoutParams to ensure it fills the container properly
                textureView.LayoutParameters = new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent);

                // Set visibility and draw attributes
                textureView.Visibility = ViewStates.Visible;

                // Set the hardware acceleration
                Platform.CurrentActivity?.RunOnUiThread(() => {
                    textureView.SetLayerType(LayerType.Hardware, null);
                });

                // Set up the surface listener
                textureView.SurfaceTextureListener = new UnifiedSurfaceTextureListener(this);

                // Clear any existing views and add our TextureView
                viewContainer.RemoveAllViews();
                viewContainer.AddView(textureView);
                // No camera ID setup - will use the hardcoded value in OpenCameraAsync
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in InitializeAsync: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        private class UnifiedSurfaceTextureListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
        {
            private readonly AndroidCameraService service;
            private bool surfaceAvailable = false;

            public UnifiedSurfaceTextureListener(AndroidCameraService service)
            {
                this.service = service;
            }

            public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
            {
                if (surfaceAvailable) return;
                surfaceAvailable = true;

                System.Diagnostics.Debug.WriteLine($"[UnifiedSurfaceTextureListener] Surface available: {width}x{height}");

                service.backgroundHandler?.Post(async () =>
                {
                    try
                    {
                        await service.OpenCameraAsync(); // Calls CreateCameraPreview internally
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UnifiedSurfaceTextureListener] Error opening camera: {ex.Message}");
                    }
                });
            }

            public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
            {
                System.Diagnostics.Debug.WriteLine("[UnifiedSurfaceTextureListener] Surface destroyed");
                surfaceAvailable = false;

                service.CloseCamera();
                service.StopBackgroundThread();
                return true;
            }

            public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
            {
                System.Diagnostics.Debug.WriteLine($"[UnifiedSurfaceTextureListener] Surface size changed: {width}x{height}");
                // Optional: recreate preview or adjust texture transform
            }

            public void OnSurfaceTextureUpdated(SurfaceTexture surface)
            {
                // Optional: you can monitor live frame updates here
            }
        }

        public async Task StartPreviewAsync()
        {
            // Open camera if it's not already open and if surface is available
            if (cameraDevice == null && textureView.IsAvailable)
            {
                await OpenCameraAsync();
            }
            else if (cameraDevice == null)
            {
                System.Diagnostics.Debug.WriteLine("Cannot start preview: TextureView is not available yet");
            }
        }

        public async Task StartCaptureAsync(string sessionFolder)
        {
            if (cameraDevice == null || string.IsNullOrEmpty(sessionFolder))
            {
                System.Diagnostics.Debug.WriteLine($"Cannot capture: cameraDevice={cameraDevice != null}, folder={!string.IsNullOrEmpty(sessionFolder)}");
                return;
            }

            currentSessionFolder = sessionFolder;

            // Create the filename for this capture
            string fileName = $"photo_{DateTime.Now:yyyyMMddHHmmss}_{photoCounter++}.jpg";
            string filePath = System.IO.Path.Combine(sessionFolder, fileName);

            System.Diagnostics.Debug.WriteLine($"Taking photo: {filePath}");

            // Take the photo
            await TakePhotoAsync(filePath);
        }

        public async Task StopCaptureAsync()
        {
            // Reset counter
            photoCounter = 0;
            currentSessionFolder = null;

            // Make this awaitable for the interface implementation
            await Task.CompletedTask;
        }

        public async Task ShutdownAsync()
        {
            // Clean up camera resources
            CloseCamera();
            StopBackgroundThread();

            // Make this awaitable for the interface implementation
            await Task.CompletedTask;
        }

        public bool IsCameraAvailable()
        {
            try
            {
                CameraManager manager = (CameraManager)Platform.CurrentActivity.GetSystemService(Context.CameraService);
                string[] cameraIds = manager.GetCameraIdList();

                // Log available cameras
                System.Diagnostics.Debug.WriteLine($"Available cameras: {string.Join(", ", cameraIds)}");

                return cameraIds.Length > 0;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking camera availability: {ex.Message}");
                return false;
            }
        }

        public void OpenCamera()
        {
            // This method is here to fulfill the interface requirement
            // Actual camera opening is done in OpenCameraAsync
        }

        private async Task<bool> TakePhotoAsync(string filePath)
        {
            if (cameraDevice == null)
            {
                System.Diagnostics.Debug.WriteLine("Cannot take photo: camera not initialized");
                return false;
            }

            captureTaskSource = new TaskCompletionSource<bool>();

            try
            {
                string cameraId = "1";
                // Get characteristics to check exposure compensation support
                CameraManager manager = (CameraManager)Platform.CurrentActivity.GetSystemService(Context.CameraService);
                CameraCharacteristics characteristics = manager.GetCameraCharacteristics(cameraId);

                global::Android.Util.Range compensationRange = GetExposureCompensationRange(characteristics);
                Rational compensationStep = GetExposureCompensationStep(characteristics);

                System.Diagnostics.Debug.WriteLine($"Raw compensation range: Lower={compensationRange.Lower}, Upper={compensationRange.Upper}, Step={compensationStep}");

                // Calculate the maximum range we can use
                int minValue = ((Java.Lang.Integer)compensationRange.Lower).IntValue();
                int maxValue = ((Java.Lang.Integer)compensationRange.Upper).IntValue();

                

                // Setup the image available listener
                BracketedCaptureListener captureListener = new BracketedCaptureListener(this, 2); // Expecting 2 images
                MultipleImageAvailableListener readerListener = new MultipleImageAvailableListener(filePath, this, 2); // 2 images
                imageReader.SetOnImageAvailableListener(readerListener, backgroundHandler);

                // Create 2 capture requests: min EV and max EV
                List<CaptureRequest> captureRequests = new List<CaptureRequest>();

                // Create 2 capture builders with very different exposures
                for (int i = 0; i < 2; i++)
                {
                    CaptureRequest.Builder captureBuilder = cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
                    captureBuilder.AddTarget(imageReader.Surface);

                    // Set focus and exposure modes
                    captureBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousPicture);
                    captureBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);

                    // First image: use minimum EV compensation, Second image: use maximum EV compensation
                    int compensation = (i == 0) ? minValue : maxValue;
                    captureBuilder.Set(CaptureRequest.ControlAeExposureCompensation, compensation);

                    // Lock the exposure to prevent the device from adjusting it automatically
                    captureBuilder.Set(CaptureRequest.ControlAeLock, true);

                    System.Diagnostics.Debug.WriteLine($"Setting exposure compensation to: {compensation}");

                    // Set orientation
                    int rotation = (int)Platform.CurrentActivity.WindowManager.DefaultDisplay.Rotation;
                    captureBuilder.Set(CaptureRequest.JpegOrientation, GetOrientation(rotation));

                    captureRequests.Add(captureBuilder.Build());
                }

                // Stop preview before capturing
                if (previewSession != null)
                {
                    try
                    {
                        previewSession.StopRepeating();

                        // Capture the burst
                        previewSession.CaptureBurst(captureRequests, captureListener, backgroundHandler);

                        return await captureTaskSource.Task;
                    }
                    catch (CameraAccessException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Camera access exception during burst capture: {ex.Message}");
                        captureTaskSource.TrySetResult(false);
                        return false;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Cannot capture: preview session is null");
                    captureTaskSource.TrySetResult(false);
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error taking photo: {ex.Message}\n{ex.StackTrace}");
                captureTaskSource.TrySetResult(false);
                return false;
            }
        }

        public class BracketedCaptureListener : CameraCaptureSession.CaptureCallback
        {
            private readonly AndroidCameraService service;
            private readonly int expectedImages;
            private int capturedImages = 0;

            public BracketedCaptureListener(AndroidCameraService service, int expectedImages)
            {
                this.service = service;
                this.expectedImages = expectedImages;
            }

            public override void OnCaptureCompleted(CameraCaptureSession session, CaptureRequest request, TotalCaptureResult result)
            {
                base.OnCaptureCompleted(session, request, result);

                capturedImages++;
                System.Diagnostics.Debug.WriteLine($"Image {capturedImages}/{expectedImages} captured successfully");

                // If all images in the burst have been captured, restart preview
                if (capturedImages >= expectedImages)
                {
                    // Restart the preview
                    try
                    {
                        service.CreateCameraPreview();
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error restarting preview: {ex.Message}");
                    }
                }
            }

            public override void OnCaptureFailed(CameraCaptureSession session, CaptureRequest request, CaptureFailure failure)
            {
                base.OnCaptureFailed(session, request, failure);
                System.Diagnostics.Debug.WriteLine($"Image capture failed: reason={failure.Reason}");

                // Notify the service of failure
                service.NotifyCaptureComplete(false);

                // Restart the preview
                try
                {
                    service.CreateCameraPreview();
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error restarting preview: {ex.Message}");
                }
            }
        }

        public class MultipleImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
            {
                private readonly string baseFilePath;
                private readonly AndroidCameraService service;
                private readonly int expectedImages;
                private int savedImages = 0;

                public MultipleImageAvailableListener(string baseFilePath, AndroidCameraService service, int expectedImages)
                {
                    this.baseFilePath = baseFilePath;
                    this.service = service;
                    this.expectedImages = expectedImages;
                }

                public void OnImageAvailable(ImageReader reader)
                {
                    global::Android.Media.Image image = null;
                    try
                    {
                        image = reader.AcquireLatestImage();

                        if (image == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Acquired image is null");

                            // If this is the last expected image and it's null, notify completion
                            if (savedImages >= expectedImages - 1)
                            {
                                service.NotifyCaptureComplete(savedImages > 0);
                            }
                            return;
                        }

                        // Determine the exposure suffix
                        string exposureSuffix = (savedImages == 0) ? "_low_exp" : "_high_exp";

                        // Create unique filename for each exposure
                        string filePath = baseFilePath.Replace(".jpg", $"{exposureSuffix}.jpg");

                        ByteBuffer buffer = image.GetPlanes()[0].Buffer;
                        byte[] bytes = new byte[buffer.Capacity()];
                        buffer.Get(bytes);

                        SaveImageToFile(bytes, filePath);
                        System.Diagnostics.Debug.WriteLine($"Image {savedImages + 1}/{expectedImages} saved to {filePath}");

                        savedImages++;

                        // Notify completion when all expected images are saved
                        if (savedImages >= expectedImages)
                        {
                            service.NotifyCaptureComplete(true);
                        }
                    }
                    catch (System.Exception e)
                    {
                        System.Diagnostics.Debug.WriteLine($"Image capture exception: {e.Message}\n{e.StackTrace}");

                        // Notify completion with success=true if at least one image was saved
                        if (savedImages >= expectedImages - 1)
                        {
                            service.NotifyCaptureComplete(savedImages > 0);
                        }
                    }
                    finally
                    {
                        if (image != null)
                        {
                            image.Close();
                        }
                    }
                }

                private void SaveImageToFile(byte[] bytes, string filePath)
                {
                    Java.IO.File file = new Java.IO.File(filePath);
                    try
                    {
                        // Make sure parent directories exist
                        file.ParentFile?.Mkdirs();

                        FileOutputStream output = new FileOutputStream(file);
                        output.Write(bytes);
                        output.Close();
                        System.Diagnostics.Debug.WriteLine($"Successfully wrote {bytes.Length} bytes to file");
                    }
                    catch (System.IO.IOException e)
                    {
                        System.Diagnostics.Debug.WriteLine($"File I/O exception: {e.Message}\n{e.StackTrace}");
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error saving image: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }

        

        private global::Android.Util.Range GetExposureCompensationRange(CameraCharacteristics characteristics)
        {
            return (global::Android.Util.Range)characteristics.Get(CameraCharacteristics.ControlAeCompensationRange);
        }

        private Rational GetExposureCompensationStep(CameraCharacteristics characteristics)
        {
            return (Rational)characteristics.Get(CameraCharacteristics.ControlAeCompensationStep);
        }

        public void NotifyCaptureComplete(bool success)
        {
            captureTaskSource?.TrySetResult(success);
        }

        // Camera ID selection method removed to use hardcoded value

        private int GetOrientation(int rotation)
        {
            // Convert from display rotation to JPEG orientation
            int jpegRotation = 0;
            switch (rotation)
            {
                case (int)SurfaceOrientation.Rotation0:
                    jpegRotation = 90;
                    break;
                case (int)SurfaceOrientation.Rotation90:
                    jpegRotation = 0;
                    break;
                case (int)SurfaceOrientation.Rotation180:
                    jpegRotation = 270;
                    break;
                case (int)SurfaceOrientation.Rotation270:
                    jpegRotation = 180;
                    break;
            }
            return jpegRotation;
        }

        private async Task OpenCameraAsync()
        {
            CameraManager manager = (CameraManager)Platform.CurrentActivity.GetSystemService(Context.CameraService);

            try
            {
                string cameraId = "1"; // Use front camera (hardcoded as in original code)
                System.Diagnostics.Debug.WriteLine($"Opening camera with ID: {cameraId}");

                CameraCharacteristics characteristics = manager.GetCameraCharacteristics(cameraId);
                StreamConfigurationMap map = (StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap);

                if (map == null)
                {
                    throw new System.Exception("Cannot get camera output sizes");
                }

                // Get optimal preview size
                Size[] outputSizes = map.GetOutputSizes(Java.Lang.Class.FromType(typeof(SurfaceTexture)));
                imageDimension = GetOptimalPreviewSize(outputSizes, 1920, 1080);
                System.Diagnostics.Debug.WriteLine($"Selected preview size: {imageDimension.Width}x{imageDimension.Height}");

                // Set up image reader with a suitable size for still capture
                if (imageReader == null)
                {
                    imageReader = ImageReader.NewInstance(1920, 1080, ImageFormatType.Jpeg, 2);
                }

                // Open the camera - use TaskCompletionSource to make this awaitable
                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                StateCallback stateCallback = new StateCallback(this, tcs);

                // Make sure we have a background handler
                if (backgroundHandler == null)
                {
                    StartBackgroundThread();
                }

                manager.OpenCamera(cameraId, stateCallback, backgroundHandler);
                await tcs.Task;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening camera: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        private Size GetOptimalPreviewSize(Size[] sizes, int targetWidth, int targetHeight)
        {
            if (sizes == null || sizes.Length == 0)
                return new Size(targetWidth, targetHeight);

            Size optimalSize = null;
            double minDiff = double.MaxValue;

            // Try to find a size close to the target aspect ratio
            double targetRatio = (double)targetWidth / targetHeight;

            foreach (var size in sizes)
            {
                // Skip sizes that are too large
                if (size.Width > 1920 || size.Height > 1080)
                    continue;

                double ratio = (double)size.Width / size.Height;
                double diff = System.Math.Abs(ratio - targetRatio);

                if (diff < minDiff)
                {
                    optimalSize = size;
                    minDiff = diff;
                }
            }

            // If no good match was found, just return the first acceptable size
            return optimalSize ?? sizes[0];
        }

        private void UpdatePreview()
        {
            if (cameraDevice == null)
            {
                System.Diagnostics.Debug.WriteLine("Cannot update preview: camera device is null");
                return;
            }

            try
            {
                // Set transformation matrix to adjust for orientation
                SetTextureTransform();

                // Use a more reliable control mode
                previewRequestBuilder.Set(CaptureRequest.ControlMode, (int)ControlMode.Auto);
                previewRequestBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousPicture);

                // Do multiple attempts with different settings if needed
                try
                {
                    System.Diagnostics.Debug.WriteLine("Setting repeating request with Auto mode");
                    previewSession.SetRepeatingRequest(previewRequestBuilder.Build(), null, backgroundHandler);
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"First attempt failed: {ex.Message}");

                    try
                    {
                        // Try with a simpler configuration
                        previewRequestBuilder.Set(CaptureRequest.ControlMode, (int)ControlMode.Auto);
                        previewRequestBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.Auto);
                        previewRequestBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);

                        System.Diagnostics.Debug.WriteLine("Setting repeating request with simpler configuration");
                        previewSession.SetRepeatingRequest(previewRequestBuilder.Build(), null, backgroundHandler);
                    }
                    catch (System.Exception innerEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Second attempt failed: {innerEx.Message}");

                        try
                        {
                            // Last resort, try a single capture
                            System.Diagnostics.Debug.WriteLine("Trying single capture as last resort");
                            previewSession.Capture(previewRequestBuilder.Build(), null, backgroundHandler);
                        }
                        catch (System.Exception finalEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Final capture attempt failed: {finalEx.Message}");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating preview: {ex.Message}");
            }
        }

        private void SetTextureTransform()
        {
            if (textureView == null || !textureView.IsAvailable)
                return;

            SurfaceTexture texture = textureView.SurfaceTexture;
            if (texture == null)
                return;

            int rotation = (int)Platform.CurrentActivity.WindowManager.DefaultDisplay.Rotation;
            Matrix matrix = new Matrix();

            // Get the viewable area
            global::Android.Graphics.RectF viewRect = new global::Android.Graphics.RectF(0, 0, textureView.Width, textureView.Height);

            // Get the preview size
            float centerX = viewRect.CenterX();
            float centerY = viewRect.CenterY();

            // Force portrait orientation regardless of device rotation
            // Rotate 90 degrees for portrait orientation
            matrix.PostRotate(0, centerX, centerY);

            // Apply scale if needed to fill the view properly
            float bufferWidth = imageDimension.Width;
            float bufferHeight = imageDimension.Height;
            float viewWidth = viewRect.Width();
            float viewHeight = viewRect.Height();

            // Calculate scaling factors to ensure the rotated image fills the view
            float scaleX = viewHeight / bufferWidth;
            float scaleY = viewWidth / bufferHeight;

            // Use the larger scale to ensure the image fills the view
            float scale = System.Math.Max(scaleX, scaleY);
            matrix.PostScale(scale, scale, centerX, centerY);

            // Apply transformation
            Platform.CurrentActivity.RunOnUiThread(() => {
                textureView.SetTransform(matrix);
            });
        }
        private void CloseCamera()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Closing camera resources");

                if (previewSession != null)
                {
                    previewSession.Close();
                    previewSession = null;
                }

                if (cameraDevice != null)
                {
                    cameraDevice.Close();
                    cameraDevice = null;
                }

                if (imageReader != null)
                {
                    imageReader.Close();
                    imageReader = null;
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error closing camera: {ex.Message}");
            }
        }

        private void StartBackgroundThread()
        {
            // Only start if not already running
            if (backgroundThread == null)
            {
                System.Diagnostics.Debug.WriteLine("Starting camera background thread");
                backgroundThread = new HandlerThread("Camera2Background");
                backgroundThread.Start();
                backgroundHandler = new IHandler(backgroundThread.Looper);
            }
        }

        private void StopBackgroundThread()
        {
            if (backgroundThread != null)
            {
                System.Diagnostics.Debug.WriteLine("Stopping camera background thread");
                try
                {
                    backgroundThread.QuitSafely();
                    backgroundThread.Join();
                    backgroundThread = null;
                    backgroundHandler = null;
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping background thread: {ex.Message}");
                }
            }
        }

        public void CreateCameraPreview()
        {
            try
            {
                if (textureView == null || !textureView.IsAvailable)
                {
                    System.Diagnostics.Debug.WriteLine("Cannot create preview: TextureView not available");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("Creating camera preview");
                SurfaceTexture texture = textureView.SurfaceTexture;

                if (texture == null)
                {
                    System.Diagnostics.Debug.WriteLine("SurfaceTexture is null");
                    return;
                }

                // Adjust buffer size to match the device's display aspect ratio
                int rotation = (int)Platform.CurrentActivity.WindowManager.DefaultDisplay.Rotation;
                int width, height;

                // Use the device's display size to determine the optimal preview size
                if (rotation == (int)SurfaceOrientation.Rotation0 || rotation == (int)SurfaceOrientation.Rotation180)
                {
                    // Portrait orientation
                    width = imageDimension.Height;
                    height = imageDimension.Width;
                }
                else
                {
                    // Landscape orientation
                    width = imageDimension.Width;
                    height = imageDimension.Height;
                }

                System.Diagnostics.Debug.WriteLine($"Setting buffer size: {width}x{height}");
                texture.SetDefaultBufferSize(width, height);

                // Create a surface for camera preview
                Surface previewSurface = new Surface(texture);

                // Create a request for preview
                previewRequestBuilder = cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                previewRequestBuilder.AddTarget(previewSurface);

                // Add auto-focus if available
                previewRequestBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousPicture);

                // Add auto-exposure if available
                previewRequestBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);

                // First try with just the preview surface
                System.Diagnostics.Debug.WriteLine("Creating capture session");
                CameraCaptureSessionCallback stateCallback = new CameraCaptureSessionCallback(this);

                try
                {
                    if (imageReader == null || imageReader.Surface == null)
                    {
                        // Create just with preview surface
                        System.Diagnostics.Debug.WriteLine("Creating session with preview surface only");
                        var surfaces = new System.Collections.Generic.List<Surface> { previewSurface };
                        cameraDevice.CreateCaptureSession(surfaces, stateCallback, backgroundHandler);
                    }
                    else
                    {
                        // Create with both surfaces
                        System.Diagnostics.Debug.WriteLine("Creating session with preview and image reader surfaces");
                        var surfaces = new System.Collections.Generic.List<Surface> { previewSurface, imageReader.Surface };
                        cameraDevice.CreateCaptureSession(surfaces, stateCallback, backgroundHandler);
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error creating capture session: {ex.Message}");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating camera preview: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Nested classes for callbacks

        private class StateCallback : CameraDevice.StateCallback
        {
            private readonly AndroidCameraService service;
            private readonly TaskCompletionSource<bool> tcs;

            public StateCallback(AndroidCameraService service, TaskCompletionSource<bool> tcs)
            {
                this.service = service;
                this.tcs = tcs;
            }

            public override void OnOpened(CameraDevice camera)
            {
                System.Diagnostics.Debug.WriteLine("Camera device opened successfully");
                service.cameraDevice = camera;
                service.CreateCameraPreview();
                tcs.TrySetResult(true);
            }

            public override void OnDisconnected(CameraDevice camera)
            {
                System.Diagnostics.Debug.WriteLine("Camera device disconnected");
                camera.Close();
                service.cameraDevice = null;
                tcs.TrySetResult(false);
            }

            public override void OnError(CameraDevice camera, CameraError error)
            {
                System.Diagnostics.Debug.WriteLine($"Camera device error: {error}");
                camera.Close();
                service.cameraDevice = null;
                tcs.TrySetException(new System.Exception($"Camera Error: {error}"));
            }
        }
        

        

        private class CameraCaptureSessionCallback : CameraCaptureSession.StateCallback
        {
            private readonly AndroidCameraService service;

            public CameraCaptureSessionCallback(AndroidCameraService service)
            {
                this.service = service;
            }

            public override void OnConfigured(CameraCaptureSession session)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Camera capture session configured");
                    service.previewSession = session;
                    service.UpdatePreview();
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in OnConfigured: {ex.Message}");
                }
            }

            public override void OnConfigureFailed(CameraCaptureSession session)
            {
                System.Diagnostics.Debug.WriteLine("Camera configuration failed!");
                Platform.CurrentActivity?.RunOnUiThread(() => {
                    // Show a toast on the UI thread
                    Toast.MakeText(Platform.CurrentActivity, "Failed to configure camera preview", ToastLength.Short).Show();
                });
            }

            public override void OnReady(CameraCaptureSession session)
            {
                base.OnReady(session);
                System.Diagnostics.Debug.WriteLine("CameraCaptureSession is ready");
            }

            public override void OnClosed(CameraCaptureSession session)
            {
                base.OnClosed(session);
                System.Diagnostics.Debug.WriteLine("CameraCaptureSession is closed");
            }
        }

    // Change the accessibility of CameraCaptureListener to public
    public class CameraCaptureListener : CameraCaptureSession.CaptureCallback
    {
        private readonly AndroidCameraService service;

        public CameraCaptureListener(AndroidCameraService service)
        {
            this.service = service;
        }

        public override void OnCaptureCompleted(CameraCaptureSession session, CaptureRequest request, TotalCaptureResult result)
        {
            base.OnCaptureCompleted(session, request, result);
            System.Diagnostics.Debug.WriteLine("Image captured successfully");

            // Restart the preview
            try
            {
                service.CreateCameraPreview();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restarting preview: {ex.Message}");
            }
        }

        public override void OnCaptureFailed(CameraCaptureSession session, CaptureRequest request, CaptureFailure failure)
        {
            base.OnCaptureFailed(session, request, failure);
            System.Diagnostics.Debug.WriteLine($"Image capture failed: reason={failure.Reason}");

            // Notify the service
            service.NotifyCaptureComplete(false);

            // Restart the preview
            try
            {
                service.CreateCameraPreview();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restarting preview: {ex.Message}");
            }
        }
    }

    // Change the accessibility of ImageAvailableListener to public
    public class ImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        private readonly string filePath;
        private readonly AndroidCameraService service;

        public ImageAvailableListener(string filePath, AndroidCameraService service)
        {
            this.filePath = filePath;
            this.service = service;
        }

        public void OnImageAvailable(ImageReader reader)
        {
            global::Android.Media.Image image = null;
            try
            {
                System.Diagnostics.Debug.WriteLine("Image available from camera");
                image = reader.AcquireLatestImage();

                if (image == null)
                {
                    System.Diagnostics.Debug.WriteLine("Acquired image is null");
                    service.NotifyCaptureComplete(false);
                    return;
                }

                ByteBuffer buffer = image.GetPlanes()[0].Buffer;
                byte[] bytes = new byte[buffer.Capacity()];
                buffer.Get(bytes);

                SaveImageToFile(bytes);
                System.Diagnostics.Debug.WriteLine($"Image saved to {filePath}");
                service.NotifyCaptureComplete(true);
            }
            catch (System.Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Image capture exception: {e.Message}\n{e.StackTrace}");
                service.NotifyCaptureComplete(false);
            }
            finally
            {
                if (image != null)
                {
                    image.Close();
                }
            }
        }

        private void SaveImageToFile(byte[] bytes)
        {
            Java.IO.File file = new Java.IO.File(filePath);
            try
            {
                // Make sure parent directories exist
                file.ParentFile?.Mkdirs();

                FileOutputStream output = new FileOutputStream(file);
                output.Write(bytes);
                output.Close();
                System.Diagnostics.Debug.WriteLine($"Successfully wrote {bytes.Length} bytes to file");
            }
            catch (System.IO.IOException e)
            {
                System.Diagnostics.Debug.WriteLine($"File I/O exception: {e.Message}\n{e.StackTrace}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving image: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
    }
}




//private class SurfaceTextureListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
//{
//    private readonly AndroidCameraService service;
//    private bool surfaceAvailable = false;

//    public SurfaceTextureListener(AndroidCameraService service)
//    {
//        this.service = service;
//    }

//    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
//    {
//        if (surfaceAvailable)
//        {
//            System.Diagnostics.Debug.WriteLine("Surface already available, ignoring duplicate callback");
//            return;
//        }

//        surfaceAvailable = true;
//        System.Diagnostics.Debug.WriteLine($"Surface available: width={width}, height={height}");

//        // Set hardware acceleration
//        Platform.CurrentActivity?.RunOnUiThread(() =>
//        {
//            if (service.textureView != null)
//            {
//                service.textureView.SetLayerType(LayerType.Hardware, null);
//            }
//        });

//        // Use the background handler which has a looper for initializing camera
//        if (service.backgroundHandler != null)
//        {
//            service.backgroundHandler.Post(async () =>
//            {
//                try
//                {
//                    // Only open if we don't have a camera device yet
//                    if (service.cameraDevice == null)
//                    {
//                        await service.OpenCameraAsync();
//                    }
//                }
//                catch (System.Exception ex)
//                {
//                    System.Diagnostics.Debug.WriteLine($"Error opening camera: {ex.Message}");
//                    surfaceAvailable = false; // Reset flag to allow retry
//                }
//            });
//        }
//        else
//        {
//            System.Diagnostics.Debug.WriteLine("Background handler is null, can't open camera");
//        }
//    }

//    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
//    {
//        surfaceAvailable = false;
//        System.Diagnostics.Debug.WriteLine("Surface destroyed");
//        return true;
//    }

//    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
//    {
//        System.Diagnostics.Debug.WriteLine($"Surface size changed: width={width}, height={height}");

//        // Recreate the camera preview when the surface size changes
//        if (service.cameraDevice != null)
//        {
//            service.backgroundHandler?.Post(() => {
//                try
//                {
//                    service.CloseCamera();
//                    service.OpenCameraAsync().Wait();
//                }
//                catch (System.Exception ex)
//                {
//                    System.Diagnostics.Debug.WriteLine($"Error recreating camera preview: {ex.Message}");
//                }
//            });
//        }
//    }

//    public void OnSurfaceTextureUpdated(SurfaceTexture surface)
//    {
//        // This gets called frequently, no need to log
//    }
//}