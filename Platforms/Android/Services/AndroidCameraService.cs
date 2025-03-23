// Platforms/Android/Services/AndroidCameraService.cs
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
using CameraBurstApp.Services.Interfaces;
using Java.Lang;
using Java.Nio;
using Java.Util.Concurrent;
using Microsoft.Maui.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static Android.Hardware.Camera2.CameraCaptureSession;

// Use full namespace for the dependency attribute
[assembly: Microsoft.Maui.Controls.Dependency(typeof(CameraBurstApp.Platforms.Android.Services.AndroidCameraService))]

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
        // Use the fully qualified name to avoid ambiguity
        private Java.Util.Concurrent.Semaphore _cameraOpenCloseLock = new Java.Util.Concurrent.Semaphore(1);
        private bool _isCapturing;
        private string _currentSessionFolder;
        private List<CaptureRequest> _captureRequests;

        // Constructor
        public AndroidCameraService()
        {
            _cameraManager = (CameraManager)global::Android.App.Application.Context.GetSystemService(Context.CameraService);
        }

        public async Task InitializeAsync(object previewView)
        {
            await Task.Run(() =>
            {
                StartBackgroundThread();

                // Set up the texture view for the camera preview
                if (previewView is TextureView textureView)
                {
                    _textureView = textureView;
                    _textureView.SurfaceTextureListener = new SurfaceTextureListener(this);
                }
                else if (previewView is View view)
                {
                    _textureView = new TextureView(view.Context);
                    _textureView.LayoutParameters = new ViewGroup.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent,
                        ViewGroup.LayoutParams.MatchParent);
                    _textureView.SurfaceTextureListener = new SurfaceTextureListener(this);
                    ((ViewGroup)view).AddView(_textureView);
                }
                else
                {
                    throw new ArgumentException("Preview view must be a TextureView or a View that can host a TextureView");
                }

                // Find camera
                SetUpCameraOutputs();
            });
        }

        public async Task StartPreviewAsync()
        {
            if (_textureView?.SurfaceTexture == null)
                return;

            if (_cameraDevice == null)
            {
                await OpenCameraAsync();
            }

            await ConfigureCameraPreviewAsync();
        }

        public bool IsCameraAvailable()
        {
            try
            {
                // Check if we have at least one camera
                string[] cameraIds = _cameraManager.GetCameraIdList();
                return cameraIds != null && cameraIds.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task StartCaptureAsync(string sessionFolder)
        {
            if (_cameraDevice == null || _captureSession == null)
                return;

            _isCapturing = true;
            _currentSessionFolder = sessionFolder;

            // Create directory if it doesn't exist
            if (!Directory.Exists(_currentSessionFolder))
            {
                Directory.CreateDirectory(_currentSessionFolder);
            }

            // Set up HDR capture requests
            _captureRequests = CreateHdrCaptureRequests();

            // Start the burst capture
            await Task.Run(() =>
            {
                try
                {
                    _captureSession.StopRepeating();
                    _captureSession.CaptureBurst(_captureRequests, new CaptureCallback(this), _backgroundHandler);
                    // After the burst, we restart the preview
                    _captureSession.SetRepeatingRequest(_previewRequest, null, _backgroundHandler);
                }
                catch (CameraAccessException e)
                {
                    e.PrintStackTrace();
                }
            });
        }

        public async Task StopCaptureAsync()
        {
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
                    }
                }
                catch (CameraAccessException e)
                {
                    e.PrintStackTrace();
                }
            });
        }

        public async Task ShutdownAsync()
        {
            await Task.Run(() =>
            {
                CloseCamera();
                StopBackgroundThread();
            });
        }

        private void SetUpCameraOutputs()
        {
            try
            {
                string[] cameraIdList = _cameraManager.GetCameraIdList();

                // Find a camera that supports RAW and has the back-facing orientation
                foreach (string id in cameraIdList)
                {
                    CameraCharacteristics characteristics = _cameraManager.GetCameraCharacteristics(id);

                    // Check if this is a back-facing camera
                    var facing = (int)characteristics.Get(CameraCharacteristics.LensFacing);
                    if (facing != (int)LensFacing.Back)
                        continue;

                    // Check if RAW format is supported
                    var capabilities = (int[])characteristics.Get(CameraCharacteristics.RequestAvailableCapabilities);
                    bool supportsRaw = false;
                    foreach (int capability in capabilities)
                    {
                        if (capability == (int)RequestAvailableCapabilities.RawCapability)
                        {
                            supportsRaw = true;
                            break;
                        }
                    }

                    if (!supportsRaw)
                        continue;

                    // We've found a suitable camera
                    _cameraId = id;
                    return;
                }
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }

        private async Task OpenCameraAsync()
        {
            try
            {
                if (_cameraManager == null || _cameraId == null)
                    return;

                if (!_cameraOpenCloseLock.TryAcquire(2500, TimeUnit.Milliseconds))
                {
                    throw new RuntimeException("Time out waiting to lock camera opening.");
                }

                _cameraManager.OpenCamera(_cameraId, new CameraStateCallback(this), _backgroundHandler);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
            catch (InterruptedException e)
            {
                throw new RuntimeException("Interrupted while trying to lock camera opening.", e);
            }

            await Task.CompletedTask;
        }

        private async Task ConfigureCameraPreviewAsync()
        {
            if (_cameraDevice == null || _textureView == null || _surfaceTexture == null)
            {
                return;
            }

            try
            {
                // Set up ImageReader for RAW capture
                CameraCharacteristics characteristics = _cameraManager.GetCameraCharacteristics(_cameraId);
                Size[] rawSizes = ((StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap))
                    .GetOutputSizes((int)ImageFormat.RawSensor);

                if (rawSizes != null && rawSizes.Length > 0)
                {
                    // Use the largest RAW size
                    Size largest = rawSizes[0];
                    for (int i = 1; i < rawSizes.Length; i++)
                    {
                        if (rawSizes[i].Width * rawSizes[i].Height > largest.Width * largest.Height)
                            largest = rawSizes[i];
                    }

                    _imageReader = ImageReader.NewInstance(largest.Width, largest.Height, ImageFormat.RawSensor, 5);
                    _imageReader.SetOnImageAvailableListener(new ImageAvailableListener(this), _backgroundHandler);
                }

                // Configure the texture for the preview
                _surfaceTexture.SetDefaultBufferSize(_textureView.Width, _textureView.Height);
                _previewSurface = new Surface(_surfaceTexture);

                // Create capture session
                List<Surface> surfaces = new List<Surface>
                {
                    _previewSurface
                };

                if (_imageReader != null)
                {
                    surfaces.Add(_imageReader.Surface);
                }

                _cameraDevice.CreateCaptureSession(surfaces, new CaptureSessionCallback(this), _backgroundHandler);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }

            await Task.CompletedTask;
        }

        private List<CaptureRequest> CreateHdrCaptureRequests()
        {
            List<CaptureRequest> requests = new List<CaptureRequest>();

            try
            {
                // Create multiple capture requests with different exposure settings for HDR
                long[] exposureTimes = new long[] { 1000000L / 1000, 1000000L / 250, 1000000L / 60 }; // 1ms, 4ms, 16.7ms
                int[] isoValues = new int[] { 800, 400, 200 };

                for (int i = 0; i < exposureTimes.Length; i++)
                {
                    CaptureRequest.Builder captureBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);

                    // Add surfaces for capture (usually only the ImageReader surface for still capture)
                    captureBuilder.AddTarget(_imageReader.Surface);

                    // Configure manual settings for exposure and ISO
                    captureBuilder.Set(CaptureRequest.ControlAeMode, (int)ControlAEMode.Off);
                    captureBuilder.Set(CaptureRequest.SensorExposureTime, exposureTimes[i]);
                    captureBuilder.Set(CaptureRequest.SensorSensitivity, isoValues[i]);

                    // Add this request to our list
                    requests.Add(captureBuilder.Build());
                }
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }

            return requests;
        }

        private void StartBackgroundThread()
        {
            _backgroundThread = new HandlerThread("CameraBackground");
            _backgroundThread.Start();
            _backgroundHandler = new Handler(_backgroundThread.Looper);
        }

        private void StopBackgroundThread()
        {
            if (_backgroundThread != null)
            {
                _backgroundThread.QuitSafely();
                try
                {
                    _backgroundThread.Join();
                    _backgroundThread = null;
                    _backgroundHandler = null;
                }
                catch (InterruptedException e)
                {
                    e.PrintStackTrace();
                }
            }
        }

        private void CloseCamera()
        {
            try
            {
                _cameraOpenCloseLock.Acquire();
                if (_captureSession != null)
                {
                    _captureSession.Close();
                    _captureSession = null;
                }

                if (_cameraDevice != null)
                {
                    _cameraDevice.Close();
                    _cameraDevice = null;
                }

                if (_imageReader != null)
                {
                    _imageReader.Close();
                    _imageReader = null;
                }
            }
            catch (InterruptedException e)
            {
                throw new RuntimeException("Interrupted while trying to lock camera closing.", e);
            }
            finally
            {
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
                _cameraService._surfaceTexture = surface;
                _cameraService.OpenCameraAsync().Wait();
            }

            public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
            {
                return true;
            }

            public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
            {
                // Configure transform if needed
            }

            public void OnSurfaceTextureUpdated(SurfaceTexture surface)
            {
                // Not needed for this implementation
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
                            }
                            catch (CameraAccessException e)
                            {
                                e.PrintStackTrace();
                            }
                        }
                    }
                }
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
                _cameraService._cameraOpenCloseLock.Release();
                _cameraService._cameraDevice = camera;
                _cameraService.ConfigureCameraPreviewAsync().Wait();
            }

            public override void OnDisconnected(CameraDevice camera)
            {
                _cameraService._cameraOpenCloseLock.Release();
                camera.Close();
                _cameraService._cameraDevice = null;
            }

            public override void OnError(CameraDevice camera, CameraError error)
            {
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
                if (_cameraService._cameraDevice == null)
                    return;

                _cameraService._captureSession = session;

                try
                {
                    // Create a request for camera preview
                    _cameraService._previewRequestBuilder = _cameraService._cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                    _cameraService._previewRequestBuilder.AddTarget(_cameraService._previewSurface);

                    // Start displaying the camera preview
                    _cameraService._previewRequest = _cameraService._previewRequestBuilder.Build();
                    _cameraService._captureSession.SetRepeatingRequest(_cameraService._previewRequest, null, _cameraService._backgroundHandler);
                }
                catch (CameraAccessException e)
                {
                    e.PrintStackTrace();
                }
            }

            public override void OnConfigureFailed(CameraCaptureSession session)
            {
                // Handle configuration failure
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
                if (!_cameraService._isCapturing || string.IsNullOrEmpty(_cameraService._currentSessionFolder))
                    return;

                Image image = null;
                try
                {
                    image = reader.AcquireLatestImage();
                    if (image == null)
                        return;

                    // Save the RAW image
                    SaveRawImage(image);
                }
                catch (Exception e)
                {
                    e.PrintStackTrace();
                }
                finally
                {
                    if (image != null)
                    {
                        image.Close();
                    }
                }
            }

            private void SaveRawImage(Image image)
            {
                string fileName = $"RAW_IMG_{DateTime.Now:yyyyMMdd_HHmmss}_{_imageCounter++}.dng";
                string filePath = Path.Combine(_cameraService._currentSessionFolder, fileName);

                ByteBuffer buffer = image.GetPlanes()[0].Buffer;
                byte[] bytes = new byte[buffer.Remaining()];
                buffer.Get(bytes);

                try
                {
                    using (FileStream output = new FileStream(filePath, FileMode.Create))
                    {
                        output.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (IOException e)
                {
                    e.PrintStackTrace();
                }
            }
        }
    }
}