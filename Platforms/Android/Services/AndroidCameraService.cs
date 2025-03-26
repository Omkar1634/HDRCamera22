using CameraBurstApp.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using global::Android.Content;
using global::Android.Graphics;
using global::Android.Hardware.Camera2;
using global::Android.Hardware.Camera2.Params;
using global::Android.Media;
using global::Android.OS;
using global::Android.Runtime;
using global::Android.Util;
using global::Android.Views;
using global::Android.Widget;
using Java.Lang;
using Java.Nio;
using Java.Util.Concurrent;
using Microsoft.Maui.Platform;
using Exception = System.Exception;
using Size = global::Android.Util.Size;
using Path = System.IO.Path;
using Image = Android.Media.Image;

namespace CameraBurstApp.Platforms.Android.Services
{
    public class AndroidCameraService : ICameraService
    {
        // Camera related members
        private CameraDevice _cameraDevice;
        private CameraCaptureSession _captureSession;
        private CameraManager _cameraManager;
        private string _cameraId;
        private CaptureRequest.Builder _previewRequestBuilder;
        private CaptureRequest _previewRequest;
        private TextureView _textureView;
        private SurfaceTexture _surfaceTexture;
        private Surface _previewSurface;
        private ImageReader _imageReader;
        private HandlerThread _backgroundThread;
        private Handler _backgroundHandler;
        private Java.Util.Concurrent.Semaphore _cameraOpenCloseLock = new Java.Util.Concurrent.Semaphore(1);
        private bool _isCapturing;
        private string _currentSessionFolder;
        private List<CaptureRequest> _captureRequests;

        // Constructor
        public AndroidCameraService()
        {
            _cameraManager = (CameraManager)global::Android.App.Application.Context.GetSystemService(Context.CameraService);
            System.Diagnostics.Debug.WriteLine("AndroidCameraService initialized");
        }

        public async Task InitializeAsync(object previewView)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Initializing camera service...");
                await Task.Run(() =>
                {
                    StartBackgroundThread();

                    // Set up the texture view for the camera preview
                    if (previewView is Grid grid)
                    {
                        System.Diagnostics.Debug.WriteLine("Preview view is a Grid");
                        var nativeView = grid.Handler?.PlatformView;

                        if (nativeView is global::Android.Views.ViewGroup viewGroup)
                        {
                            System.Diagnostics.Debug.WriteLine("Got platform ViewGroup");
                            // Create a new TextureView
                            _textureView = new TextureView(global::Android.App.Application.Context);
                            _textureView.LayoutParameters = new ViewGroup.LayoutParams(
                                ViewGroup.LayoutParams.MatchParent,
                                ViewGroup.LayoutParams.MatchParent);

                            // Clear any existing views and add our texture view
                            viewGroup.RemoveAllViews();
                            viewGroup.AddView(_textureView);

                            // Set the listener
                            _textureView.SurfaceTextureListener = new SurfaceTextureListener(this);
                            System.Diagnostics.Debug.WriteLine("Added TextureView to container");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Native view is not a ViewGroup: {nativeView?.GetType().Name ?? "null"}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Preview view is not a Grid: {previewView?.GetType().Name ?? "null"}");
                    }

                    // Find camera
                    SetUpCameraOutputs();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing camera: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
            }
        }

        private void ConfigureTransform(int viewWidth, int viewHeight)
        {
            if (_textureView == null)
                return;

            var rotation = (int)global::Android.App.Application.Context.GetSystemService(Context.WindowService).JavaCast<IWindowManager>().DefaultDisplay.Rotation;
            Matrix matrix = new Matrix();

            // Set appropriate scaling based on display rotation
            float ratioSurface = (float)viewWidth / viewHeight;

            // Ensure we have valid dimensions to avoid division by zero
            if (viewWidth > 0 && viewHeight > 0)
            {
                matrix.PostScale(1f, 1f, viewWidth / 2f, viewHeight / 2f);
                _textureView.SetTransform(matrix);
                System.Diagnostics.Debug.WriteLine($"Preview transform configured for {viewWidth}x{viewHeight}");
            }
        }

        public async Task StartPreviewAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Starting camera preview...");
                if (_textureView?.SurfaceTexture == null)
                {
                    System.Diagnostics.Debug.WriteLine("TextureView or SurfaceTexture is null, can't start preview");
                    return;
                }

                if (_cameraDevice == null)
                {
                    System.Diagnostics.Debug.WriteLine("Camera device is null, opening camera");
                    await OpenCameraAsync();
                }

                await ConfigureCameraPreviewAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting preview: {ex.Message}");
            }
        }

        public void OpenCamera()
        {
            System.Diagnostics.Debug.WriteLine("OpenCamera called");
            _ = OpenCameraAsync();
        }

        public bool IsCameraAvailable()
        {
            try
            {
                // Check if we have at least one camera
                string[] cameraIds = _cameraManager?.GetCameraIdList();
                bool available = cameraIds != null && cameraIds.Length > 0;
                System.Diagnostics.Debug.WriteLine($"Camera available: {available}, found {cameraIds?.Length ?? 0} cameras");
                return available;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking camera availability: {ex.Message}");
                return false;
            }
        }

        public async Task StartCaptureAsync(string sessionFolder)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Starting capture to folder: {sessionFolder}");
                if (_cameraDevice == null || _captureSession == null)
                {
                    System.Diagnostics.Debug.WriteLine("Camera device or capture session is null, can't start capture");
                    return;
                }

                _isCapturing = true;
                _currentSessionFolder = sessionFolder;

                // Create directory if it doesn't exist
                if (!Directory.Exists(_currentSessionFolder))
                {
                    Directory.CreateDirectory(_currentSessionFolder);
                }

                // Use the combined approach with both auto and manual settings
                _captureRequests = CreateCombinedExposureBurstRequests();
                System.Diagnostics.Debug.WriteLine($"Created {_captureRequests.Count} exposure requests for burst");

                // Start the burst capture
                await Task.Run(() =>
                {
                    try
                    {
                        if (_captureRequests.Count > 0)
                        {
                            _captureSession.StopRepeating();
                            _captureSession.CaptureBurst(_captureRequests, new CaptureCallback(this), _backgroundHandler);
                            // After the burst, we restart the preview
                            _captureSession.SetRepeatingRequest(_previewRequest, null, _backgroundHandler);
                            System.Diagnostics.Debug.WriteLine("Burst capture started with combined exposure settings");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("No capture requests created, aborting capture");
                        }
                    }
                    catch (CameraAccessException e)
                    {
                        System.Diagnostics.Debug.WriteLine($"Camera access exception: {e.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting capture: {ex.Message}");
            }
        }

        private List<CaptureRequest> CreateCombinedExposureBurstRequests()
        {
            List<CaptureRequest> requests = new List<CaptureRequest>();

            try
            {
                System.Diagnostics.Debug.WriteLine("Creating combined exposure burst capture requests");

                if (_imageReader == null || _imageReader.Surface == null)
                {
                    System.Diagnostics.Debug.WriteLine("ImageReader or its Surface is null, cannot create capture requests");
                    return requests;
                }

                // First part: Auto exposure with maximum compensation and torch
                {
                    CaptureRequest.Builder captureBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
                    captureBuilder.AddTarget(_imageReader.Surface);

                    // Use auto exposure with maximum compensation
                    captureBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);
                    captureBuilder.Set(CaptureRequest.ControlAeExposureCompensation, 12); // Maximum compensation +4EV

                    // Turn on the flash torch mode to provide additional lighting
                    captureBuilder.Set(CaptureRequest.FlashMode, (int)FlashMode.Torch);

                    // Other auto settings
                    captureBuilder.Set(CaptureRequest.ControlCaptureIntent, (int)ControlCaptureIntent.ZeroShutterLag);
                    captureBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousPicture);
                    captureBuilder.Set(CaptureRequest.ControlAwbMode, (int)ControlAwbMode.WarmFluorescent);

                    requests.Add(captureBuilder.Build());
                    System.Diagnostics.Debug.WriteLine("Created auto-exposure request with torch mode");
                }

                // Second part: Manual exposure with very high ISO and torch
                {
                    CaptureRequest.Builder captureBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
                    captureBuilder.AddTarget(_imageReader.Surface);

                    // Use manual exposure with high ISO
                    captureBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.Off);
                    captureBuilder.Set(CaptureRequest.SensorExposureTime, 33000000L); // 33ms (try to exceed default)
                    captureBuilder.Set(CaptureRequest.SensorSensitivity, 3200); // Very high ISO

                    // Turn on the flash torch mode to provide additional lighting
                    captureBuilder.Set(CaptureRequest.FlashMode, (int)FlashMode.Torch);

                    // Other settings
                    captureBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousPicture);
                    captureBuilder.Set(CaptureRequest.ControlAwbMode, (int)ControlAwbMode.WarmFluorescent);

                    requests.Add(captureBuilder.Build());
                    System.Diagnostics.Debug.WriteLine("Created manual exposure request with high ISO and torch mode");
                }

                // Third part: Manual exposure with extremely high ISO (light boost)
                {
                    CaptureRequest.Builder captureBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
                    captureBuilder.AddTarget(_imageReader.Surface);

                    // Use manual exposure with maximum ISO
                    captureBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.Off);
                    captureBuilder.Set(CaptureRequest.SensorExposureTime, 66000000L); // 66ms (try for longer exposure)
                    captureBuilder.Set(CaptureRequest.SensorSensitivity, 6400); // Maximum ISO

                    // Turn on the flash torch mode
                    captureBuilder.Set(CaptureRequest.FlashMode, (int)FlashMode.Torch);

                    // Other settings
                    captureBuilder.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousPicture);
                    captureBuilder.Set(CaptureRequest.ControlAwbMode, (int)ControlAwbMode.WarmFluorescent);

                    requests.Add(captureBuilder.Build());
                    System.Diagnostics.Debug.WriteLine("Created extreme exposure request with maximum ISO and torch mode");
                }
            }
            catch (CameraAccessException e)
            {
                System.Diagnostics.Debug.WriteLine($"Camera access exception: {e.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating combined exposure requests: {ex.Message}");
            }

            return requests;
        }

        public async Task StopCaptureAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Stopping capture");
                _isCapturing = false;
                // Stop any ongoing burst captures
                await Task.Run(() =>
                {
                    try
                    {
                        if (_captureSession != null)
                        {
                            _captureSession.StopRepeating();
                            _captureSession.AbortCaptures();
                            // Restart preview
                            _captureSession.SetRepeatingRequest(_previewRequest, null, _backgroundHandler);
                            System.Diagnostics.Debug.WriteLine("Capture stopped and preview restarted");
                        }
                    }
                    catch (CameraAccessException e)
                    {
                        System.Diagnostics.Debug.WriteLine($"Camera access exception: {e.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping capture: {ex.Message}");
            }
        }

        public async Task ShutdownAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Shutting down camera service");
                await Task.Run(() =>
                {
                    CloseCamera();
                    StopBackgroundThread();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error shutting down camera: {ex.Message}");
            }
        }

        private void SetUpCameraOutputs()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Setting up camera outputs");
                string[] cameraIdList = _cameraManager.GetCameraIdList();
                System.Diagnostics.Debug.WriteLine($"Found {cameraIdList.Length} cameras");

                // Find a camera that supports RAW and has the back-facing orientation
                foreach (string id in cameraIdList)
                {
                    CameraCharacteristics characteristics = _cameraManager.GetCameraCharacteristics(id);

                    // Check if this is a back-facing camera
                    var facing = (int)characteristics.Get(CameraCharacteristics.LensFacing);
                    if (facing != (int)LensFacing.Back)
                    {
                        System.Diagnostics.Debug.WriteLine($"Camera {id} is not back-facing, skipping");
                        continue;
                    }

                    // Check if RAW format is supported
                    var capabilities = (int[])characteristics.Get(CameraCharacteristics.RequestAvailableCapabilities);
                    bool supportsRaw = false;
                    foreach (int capability in capabilities)
                    {
                        if (capability == (int)RequestAvailableCapabilities.Raw)
                        {
                            supportsRaw = true;
                            break;
                        }
                    }

                    if (!supportsRaw)
                    {
                        System.Diagnostics.Debug.WriteLine($"Camera {id} does not support RAW, skipping");
                        continue;
                    }

                    // We've found a suitable camera
                    _cameraId = id;
                    System.Diagnostics.Debug.WriteLine($"Selected camera {id} for capture (supports RAW)");
                    return;
                }

                // If we couldn't find a camera that supports RAW, use any back camera
                foreach (string id in cameraIdList)
                {
                    CameraCharacteristics characteristics = _cameraManager.GetCameraCharacteristics(id);
                    var facing = (int)characteristics.Get(CameraCharacteristics.LensFacing);
                    if (facing == (int)LensFacing.Back)
                    {
                        _cameraId = id;
                        System.Diagnostics.Debug.WriteLine($"Selected camera {id} as fallback (back camera)");
                        return;
                    }
                }

                // If all else fails, use the first camera
                if (cameraIdList.Length > 0)
                {
                    _cameraId = cameraIdList[0];
                    System.Diagnostics.Debug.WriteLine($"Selected camera {_cameraId} as last resort (first camera)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No cameras found!");
                }
            }
            catch (CameraAccessException e)
            {
                System.Diagnostics.Debug.WriteLine($"Camera access exception: {e.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting up camera outputs: {ex.Message}");
            }
        }

        private async Task OpenCameraAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Opening camera {_cameraId}");
                if (_cameraManager == null || _cameraId == null)
                {
                    System.Diagnostics.Debug.WriteLine("Camera manager or camera ID is null");
                    return;
                }

                if (!_cameraOpenCloseLock.TryAcquire(2500, TimeUnit.Milliseconds))
                {
                    System.Diagnostics.Debug.WriteLine("Timeout waiting for camera lock");
                    throw new RuntimeException("Time out waiting to lock camera opening.");
                }

                _cameraManager.OpenCamera(_cameraId, new CameraStateCallback(this), _backgroundHandler);
                System.Diagnostics.Debug.WriteLine("Camera open request sent");
            }
            catch (CameraAccessException e)
            {
                System.Diagnostics.Debug.WriteLine($"Camera access exception: {e.Message}");
                _cameraOpenCloseLock.Release();
            }
            catch (InterruptedException e)
            {
                System.Diagnostics.Debug.WriteLine($"Interrupted: {e.Message}");
                _cameraOpenCloseLock.Release();
                throw new RuntimeException("Interrupted while trying to lock camera opening.", e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening camera: {ex.Message}");
                _cameraOpenCloseLock.Release();
            }

            await Task.CompletedTask;
        }

        private async Task ConfigureCameraPreviewAsync()
        {
            System.Diagnostics.Debug.WriteLine("Configuring camera preview");
            if (_cameraDevice == null)
            {
                System.Diagnostics.Debug.WriteLine("Camera device is null, cannot configure preview");
                return;
            }

            if (_textureView == null)
            {
                System.Diagnostics.Debug.WriteLine("TextureView is null, cannot configure preview");
                return;
            }

            if (_surfaceTexture == null)
            {
                System.Diagnostics.Debug.WriteLine("SurfaceTexture is null, cannot configure preview");
                return;
            }

            try
            {
                // Ensure dimensions are valid
                int previewWidth = System.Math.Max(1, _textureView.Width);
                int previewHeight = System.Math.Max(1, _textureView.Height);
                System.Diagnostics.Debug.WriteLine($"TextureView dimensions: {previewWidth}x{previewHeight}");

                // Get camera characteristics
                CameraCharacteristics characteristics = _cameraManager.GetCameraCharacteristics(_cameraId);
                StreamConfigurationMap map = (StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap);

                // Set up ImageReader for RAW capture
                Size[] rawSizes = map.GetOutputSizes((int)ImageFormatType.RawSensor);
                if (rawSizes != null && rawSizes.Length > 0)
                {
                    // Use the largest RAW size
                    Size largest = ChooseBiggestSize(rawSizes);
                    System.Diagnostics.Debug.WriteLine($"Selected RAW size: {largest.Width}x{largest.Height}");

                    _imageReader = ImageReader.NewInstance(largest.Width, largest.Height,ImageFormatType.RawSensor, 5);
                    _imageReader.SetOnImageAvailableListener(new ImageAvailableListener(this), _backgroundHandler);
                    System.Diagnostics.Debug.WriteLine("ImageReader configured for RAW capture");
                }
                else
                {
                    // Fall back to JPEG if RAW is not supported
                    System.Diagnostics.Debug.WriteLine("RAW format not supported, falling back to JPEG");
                    Size[] jpegSizes = map.GetOutputSizes((int)ImageFormatType.Jpeg);
                    if (jpegSizes != null && jpegSizes.Length > 0)
                    {
                        Size largest = ChooseBiggestSize(jpegSizes);
                        System.Diagnostics.Debug.WriteLine($"Selected JPEG size: {largest.Width}x{largest.Height}");
                        _imageReader = ImageReader.NewInstance(largest.Width, largest.Height,ImageFormatType.Jpeg, 5);
                        _imageReader.SetOnImageAvailableListener(new ImageAvailableListener(this), _backgroundHandler);
                        System.Diagnostics.Debug.WriteLine("ImageReader configured for JPEG capture");
                    }
                }

                // Get optimal preview size
                Size[] previewSizes = map.GetOutputSizes(Java.Lang.Class.FromType(typeof(SurfaceTexture)));
                if (previewSizes != null && previewSizes.Length > 0)
                {
                    Size optimalSize = ChooseOptimalSize(previewSizes, previewWidth, previewHeight);
                    System.Diagnostics.Debug.WriteLine($"Selected preview size: {optimalSize.Width}x{optimalSize.Height}");
                    _surfaceTexture.SetDefaultBufferSize(optimalSize.Width, optimalSize.Height);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No preview sizes available, using view dimensions");
                    _surfaceTexture.SetDefaultBufferSize(previewWidth, previewHeight);
                }

                // Create the preview surface
                System.Diagnostics.Debug.WriteLine("Creating preview surface");
                _previewSurface = new Surface(_surfaceTexture);

                // Configure transform
                ConfigureTransform(_textureView.Width, _textureView.Height);


                // Create capture session
                List<Surface> surfaces = new List<Surface> { _previewSurface };
                if (_imageReader != null)
                {
                    surfaces.Add(_imageReader.Surface);
                    System.Diagnostics.Debug.WriteLine("Added image reader surface");
                }

                System.Diagnostics.Debug.WriteLine($"Creating capture session with {surfaces.Count} surfaces");
                _cameraDevice.CreateCaptureSession(surfaces, new CaptureSessionCallback(this), _backgroundHandler);
            }
            catch (CameraAccessException e)
            {
                System.Diagnostics.Debug.WriteLine($"Camera access exception: {e.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error configuring camera preview: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private Size ChooseBiggestSize(Size[] choices)
        {
            Size biggest = choices[0];
            for (int i = 1; i < choices.Length; i++)
            {
                if (choices[i].Width * choices[i].Height > biggest.Width * biggest.Height)
                {
                    biggest = choices[i];
                }
            }
            return biggest;
        }

        private Size ChooseOptimalSize(Size[] choices, int width, int height)
        {
            // First, find a size that matches the aspect ratio of the display
            double targetRatio = (double)width / height;
            Size optimalSize = null;
            double minDiff = double.MaxValue;

            foreach (Size size in choices)
            {
                double ratio = (double)size.Width / size.Height;
                double diff = System.Math.Abs(ratio - targetRatio);
                if (diff < minDiff)
                {
                    optimalSize = size;
                    minDiff = diff;
                }
            }

            // If we couldn't find a good match, just use the largest available size
            if (optimalSize == null)
            {
                return ChooseBiggestSize(choices);
            }

            return optimalSize;
        }

        private List<CaptureRequest> CreateHdrCaptureRequests()
        {
            List<CaptureRequest> requests = new List<CaptureRequest>();

            try
            {
                System.Diagnostics.Debug.WriteLine("Creating HDR capture requests");
                // Create multiple capture requests with different exposure settings for HDR
                long[] exposureTimes = new long[] { 1000000L / 1000, 1000000L / 250, 1000000L / 60 }; // 1ms, 4ms, 16.7ms
                int[] isoValues = new int[] { 800, 400, 200 };

                for (int i = 0; i < exposureTimes.Length; i++)
                {
                    CaptureRequest.Builder captureBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);

                    // Add surfaces for capture
                    captureBuilder.AddTarget(_imageReader.Surface);

                    // Configure manual settings for exposure and ISO
                    if (CaptureRequest.ControlAeMode != null)
                    {
                        captureBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.Off);
                    }
                    if (CaptureRequest.SensorExposureTime != null)
                    {
                        captureBuilder.Set(CaptureRequest.SensorExposureTime, exposureTimes[i]);
                    }
                    if (CaptureRequest.SensorSensitivity != null)
                    {
                        captureBuilder.Set(CaptureRequest.SensorSensitivity, isoValues[i]);
                    }

                    // Add this request to our list
                    requests.Add(captureBuilder.Build());
                    System.Diagnostics.Debug.WriteLine($"Created capture request with exposure {exposureTimes[i]}ns, ISO {isoValues[i]}");
                }
            }
            catch (CameraAccessException e)
            {
                System.Diagnostics.Debug.WriteLine($"Camera access exception: {e.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating HDR requests: {ex.Message}");
            }

            return requests;
        }

        private void StartBackgroundThread()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Starting background thread");
                _backgroundThread = new HandlerThread("CameraBackground");
                _backgroundThread.Start();
                _backgroundHandler = new Handler(_backgroundThread.Looper);
                System.Diagnostics.Debug.WriteLine("Background thread started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting background thread: {ex.Message}");
            }
        }

        private void StopBackgroundThread()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Stopping background thread");
                if (_backgroundThread != null)
                {
                    _backgroundThread.QuitSafely();
                    try
                    {
                        _backgroundThread.Join();
                        _backgroundThread = null;
                        _backgroundHandler = null;
                        System.Diagnostics.Debug.WriteLine("Background thread stopped");
                    }
                    catch (InterruptedException e)
                    {
                        System.Diagnostics.Debug.WriteLine($"Interrupted: {e.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping background thread: {ex.Message}");
            }
        }

        private void CloseCamera()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Closing camera");
                _cameraOpenCloseLock.Acquire();
                try
                {
                    if (_captureSession != null)
                    {
                        _captureSession.Close();
                        _captureSession = null;
                        System.Diagnostics.Debug.WriteLine("Capture session closed");
                    }

                    if (_cameraDevice != null)
                    {
                        _cameraDevice.Close();
                        _cameraDevice = null;
                        System.Diagnostics.Debug.WriteLine("Camera device closed");
                    }

                    if (_imageReader != null)
                    {
                        _imageReader.Close();
                        _imageReader = null;
                        System.Diagnostics.Debug.WriteLine("Image reader closed");
                    }
                }
                finally
                {
                    _cameraOpenCloseLock.Release();
                }
            }
            catch (InterruptedException e)
            {
                System.Diagnostics.Debug.WriteLine($"Interrupted while closing camera: {e.Message}");
                throw new RuntimeException("Interrupted while trying to lock camera closing.", e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error closing camera: {ex.Message}");
                _cameraOpenCloseLock.Release();
            }
        }

        // Helper classes
        public class SurfaceTextureListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
        {
            private readonly AndroidCameraService _cameraService;

            public SurfaceTextureListener(AndroidCameraService cameraService)
            {
                _cameraService = cameraService;
            }

            public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
            {
                System.Diagnostics.Debug.WriteLine($"OnSurfaceTextureAvailable: {width}x{height}");

                // Force layout measurement
                _cameraService._textureView.Post(() => {
                    System.Diagnostics.Debug.WriteLine($"TextureView actual size after post: {_cameraService._textureView.Width}x{_cameraService._textureView.Height}");

                    _cameraService._surfaceTexture = surface;
                    System.Diagnostics.Debug.WriteLine("Surface texture available, starting camera");

                    // Use more realistic dimensions
                    int realWidth = _cameraService._textureView.Width > 0 ? _cameraService._textureView.Width : 1080;
                    int realHeight = _cameraService._textureView.Height > 0 ? _cameraService._textureView.Height : 1920;

                    _cameraService.ConfigureTransform(realWidth, realHeight);
                    _cameraService.OpenCameraAsync().Wait();
                });
            }

            public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
            {
                System.Diagnostics.Debug.WriteLine("OnSurfaceTextureDestroyed");
                return true;
            }

            public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
            {
                System.Diagnostics.Debug.WriteLine($"OnSurfaceTextureSizeChanged: {width}x{height}");
                _cameraService.ConfigureTransform(width, height);
            }

            public void OnSurfaceTextureUpdated(SurfaceTexture surface)
            {
               
            }
        }

        public class CaptureCallback : CameraCaptureSession.CaptureCallback
        {
            private readonly AndroidCameraService _cameraService;
            private int _captureCounter = 0;

            public CaptureCallback(AndroidCameraService cameraService)
            {
                _cameraService = cameraService;
            }

            public override void OnCaptureCompleted(CameraCaptureSession session, CaptureRequest request, TotalCaptureResult result)
            {
                base.OnCaptureCompleted(session, request, result);
                System.Diagnostics.Debug.WriteLine($"Capture completed: {_captureCounter + 1}/{_cameraService._captureRequests?.Count ?? 0}");

                if (_cameraService._isCapturing)
                {
                    // Increment counter to keep track of how many images we've captured in this burst
                    _captureCounter++;

                    // If we've captured all exposures in our HDR sequence, restart the burst if still capturing
                    if (_captureCounter >= _cameraService._captureRequests.Count)
                    {
                        _captureCounter = 0;
                        if (_cameraService._isCapturing)
                        {
                            try
                            {
                                // Continue burst capture
                                session.CaptureBurst(_cameraService._captureRequests, this, _cameraService._backgroundHandler);
                                System.Diagnostics.Debug.WriteLine("Starting new burst capture");
                            }
                            catch (CameraAccessException e)
                            {
                                System.Diagnostics.Debug.WriteLine($"Camera access exception: {e.Message}");
                            }
                        }
                    }
                }
            }

            public override void OnCaptureFailed(CameraCaptureSession session, CaptureRequest request, CaptureFailure failure)
            {
                base.OnCaptureFailed(session, request, failure);
                System.Diagnostics.Debug.WriteLine($"Capture failed: Error {failure.Reason}");
            }
        }

        public class CameraStateCallback : CameraDevice.StateCallback
        {
            private readonly AndroidCameraService _cameraService;

            public CameraStateCallback(AndroidCameraService cameraService)
            {
                _cameraService = cameraService;
            }

            public override void OnOpened(CameraDevice camera)
            {
                System.Diagnostics.Debug.WriteLine($"Camera {camera.Id} opened successfully");
                _cameraService._cameraOpenCloseLock.Release();
                _cameraService._cameraDevice = camera;
                _cameraService.ConfigureCameraPreviewAsync().Wait();
            }

            public override void OnDisconnected(CameraDevice camera)
            {
                System.Diagnostics.Debug.WriteLine($"Camera {camera.Id} disconnected");
                _cameraService._cameraOpenCloseLock.Release();
                camera.Close();
                _cameraService._cameraDevice = null;
            }

            public override void OnError(CameraDevice camera, CameraError error)
            {
                System.Diagnostics.Debug.WriteLine($"Camera {camera.Id} error: {error}");
                _cameraService._cameraOpenCloseLock.Release();
                camera.Close();
                _cameraService._cameraDevice = null;
            }
        }

        public class CaptureSessionCallback : CameraCaptureSession.StateCallback
        {
            private readonly AndroidCameraService _cameraService;

            public CaptureSessionCallback(AndroidCameraService cameraService)
            {
                _cameraService = cameraService;
            }

            public override void OnConfigured(CameraCaptureSession session)
            {
                System.Diagnostics.Debug.WriteLine("Capture session configured successfully");
                if (_cameraService._cameraDevice == null)
                {
                    System.Diagnostics.Debug.WriteLine("Camera device is null, can't set up preview");
                    return;
                }

                _cameraService._captureSession = session;

                try
                {
                    // Create the request builder FIRST
                    _cameraService._previewRequestBuilder = _cameraService._cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                    _cameraService._previewRequestBuilder.AddTarget(_cameraService._previewSurface);

                    // THEN set properties on it
                    if (CaptureRequest.ControlAeMode != null)
                    {
                        _cameraService._previewRequestBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.On);
                    }

                    if (CaptureRequest.ControlAwbMode != null)
                    {
                        _cameraService._previewRequestBuilder.Set(CaptureRequest.ControlAwbMode, 1);
                    }

                    // Start displaying the camera preview
                    _cameraService._previewRequest = _cameraService._previewRequestBuilder.Build();
                    _cameraService._captureSession.SetRepeatingRequest(_cameraService._previewRequest, null, _cameraService._backgroundHandler);
                    System.Diagnostics.Debug.WriteLine("Camera preview started");
                }
                catch (CameraAccessException e)
                {
                    System.Diagnostics.Debug.WriteLine($"Camera access exception: {e.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error setting up preview: {ex.Message}");
                }
            }

            public override void OnConfigureFailed(CameraCaptureSession session)
            {
                System.Diagnostics.Debug.WriteLine("Failed to configure capture session");
            }
        }

        public class ImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
        {
            private readonly AndroidCameraService _cameraService;
            private int _imageCounter = 0;

            public ImageAvailableListener(AndroidCameraService cameraService)
            {
                _cameraService = cameraService;
            }

            public void OnImageAvailable(ImageReader reader)
            {
                System.Diagnostics.Debug.WriteLine("Image available from camera");
                if (!_cameraService._isCapturing || string.IsNullOrEmpty(_cameraService._currentSessionFolder))
                {
                    System.Diagnostics.Debug.WriteLine("Not capturing or session folder not set, ignoring image");
                    return;
                }

                Image image = null;
                try
                {
                    image = reader.AcquireLatestImage();
                    if (image == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Acquired image is null");
                        return;
                    }

                    // Save the RAW image
                    SaveRawImage(image);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"Error handling image: {e.Message}");
                }
                finally
                {
                    if (image != null)
                    {
                        image.Close();
                        System.Diagnostics.Debug.WriteLine("Image closed");
                    }
                }

                
            }
            private void SaveRawImage(Image image)
            {
                try
                {
                    string fileName = $"RAW_IMG_{DateTime.Now:yyyyMMdd_HHmmss}_{_imageCounter++}.dng";

                    // Keep saving to app's private storage for metadata purposes
                    string privatePath = Path.Combine(_cameraService._currentSessionFolder, fileName);

                    ByteBuffer buffer = image.GetPlanes()[0].Buffer;
                    byte[] bytes = new byte[buffer.Remaining()];
                    buffer.Get(bytes);

                    // Save to private storage
                    using (FileStream output = new FileStream(privatePath, FileMode.Create))
                    {
                        output.Write(bytes, 0, bytes.Length);
                    }

                    // Now also save to gallery using DI to get the file service
                    var fileService = IPlatformApplication.Current.Services.GetService<IFileService>();
                    if (fileService != null)
                    {
                        // Use ConfigureAwait(false) to avoid deadlocks
                        Task.Run(async () => {
                            await fileService.SaveImageToGallery(bytes, fileName).ConfigureAwait(false);
                        });
                    }

                    System.Diagnostics.Debug.WriteLine($"Image saved successfully: {bytes.Length} bytes");
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving image: {e.Message}");
                }
            }
        }
    }
}