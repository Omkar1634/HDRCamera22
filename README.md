```

using Android.App;
using Android.OS;
using Android.Support.V4.App;
using Android.Support.V4.Content;
using Android.Hardware.Camera2;
using Android.Views;
using Java.Util.Concurrent;
using Android.Widget;
using Android.Content.PM;

[Activity(Label = "CameraActivity", MainLauncher = true)]
public class CameraActivity : Activity
{
    private const int RequestCameraPermission = 1;
    private CameraManager _cameraManager;
    private CameraDevice _cameraDevice;
    private CameraCaptureSession _captureSession;
    private CameraDevice.StateCallback _cameraCallback;
    private TextureView _textureView;

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        _textureView = FindViewById<TextureView>(Resource.Id.textureView);
        _textureView.SurfaceTextureListener = new SurfaceTextureListener(this);

        // Initialize CameraManager
        _cameraManager = (CameraManager)GetSystemService(CameraService);

        // Check if the app has CAMERA permission
        if (ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.Camera) != Permission.Granted)
        {
            // Request CAMERA permission at runtime
            ActivityCompat.RequestPermissions(this, new string[] { Android.Manifest.Permission.Camera }, RequestCameraPermission);
        }
        else
        {
            // Permissions are granted, proceed to open the camera
            OpenCamera();
        }
    }

    // Handle permission result
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        if (requestCode == RequestCameraPermission)
        {
            if (grantResults[0] == Permission.Granted)
            {
                // Permission granted, proceed to open the camera
                OpenCamera();
            }
            else
            {
                Toast.MakeText(this, "Camera permission is required", ToastLength.Short).Show();
            }
        }
    }

    // Function to open the camera
    private void OpenCamera()
    {
        try
        {
            string cameraId = _cameraManager.GetCameraIdList()[0]; // Use the first camera (you can change this logic)

            _cameraCallback = new CameraStateCallback(this);

            // Create an executor to manage the callback execution (we can use the main thread executor)
            IExecutor executor = Executors.NewSingleThreadExecutor();

            _cameraManager.OpenCamera(cameraId, executor, _cameraCallback);
        }
        catch (CameraAccessException e)
        {
            Android.Util.Log.Error("CameraAccess", "Failed to open camera: " + e.Message);
        }
    }

    // CameraStateCallback to handle camera state changes
    public class CameraStateCallback : CameraDevice.StateCallback
    {
        private readonly CameraActivity _activity;

        public CameraStateCallback(CameraActivity activity)
        {
            _activity = activity;
        }

        public override void OnOpened(CameraDevice camera)
        {
            base.OnOpened(camera);

            _activity._cameraDevice = camera;

            // Start the camera preview when the camera is opened
            _activity.StartPreview();
        }

        public override void OnDisconnected(CameraDevice camera)
        {
            base.OnDisconnected(camera);
            Android.Util.Log.Info("Camera", "Camera disconnected.");
        }

        public override void OnError(CameraDevice camera, CameraError error)
        {
            base.OnError(camera, error);
            Android.Util.Log.Error("Camera", $"Camera error: {error}");
        }
    }

    // Start the camera preview
    private void StartPreview()
    {
        try
        {
            if (_cameraDevice == null || !_textureView.IsAvailable)
                return;

            // Create the Surface for the preview
            SurfaceTexture surfaceTexture = _textureView.SurfaceTexture;
            surfaceTexture.SetDefaultBufferSize(1920, 1080); // Set the desired resolution
            Surface previewSurface = new Surface(surfaceTexture);

            // Create a capture request for the preview
            CaptureRequest.Builder captureRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
            captureRequestBuilder.AddTarget(previewSurface);

            // Create a capture session for the preview
            _cameraDevice.CreateCaptureSession(new List<Surface> { previewSurface }, new CameraCaptureSessionStateCallback(this), null);
        }
        catch (CameraAccessException e)
        {
            Android.Util.Log.Error("CameraPreview", "Failed to start preview: " + e.Message);
        }
    }

    // CameraCaptureSession.StateCallback to manage the capture session
    public class CameraCaptureSessionStateCallback : CameraCaptureSession.StateCallback
    {
        private readonly CameraActivity _activity;

        public CameraCaptureSessionStateCallback(CameraActivity activity)
        {
            _activity = activity;
        }

        public override void OnConfigured(CameraCaptureSession session)
        {
            base.OnConfigured(session);

            // Start the preview once the session is configured
            _activity._captureSession = session;

            try
            {
                // Set the repeating request for continuous preview
                _activity._captureSession.SetRepeatingRequest(_activity._cameraDevice.CreateCaptureRequest(CameraTemplate.Preview).Build(), null, null);
            }
            catch (CameraAccessException e)
            {
                Android.Util.Log.Error("CameraPreview", "Failed to start preview: " + e.Message);
            }
        }

        public override void OnConfigureFailed(CameraCaptureSession session)
        {
            base.OnConfigureFailed(session);
            Android.Util.Log.Error("CameraPreview", "Failed to configure the capture session.");
        }
    }

    // SurfaceTextureListener to handle TextureView's surface changes
    public class SurfaceTextureListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
    {
        private readonly CameraActivity _activity;

        public SurfaceTextureListener(CameraActivity activity)
        {
            _activity = activity;
        }

        public void OnSurfaceCreated(ISurfaceTexture surface, int width, int height) { }

        public void OnSurfaceChanged(ISurfaceTexture surface, int format, int width, int height) { }

        public void OnSurfaceDestroyed(ISurfaceTexture surface) { }

        public void OnSurfaceUpdated(ISurfaceTexture surface) { }
    }
}
