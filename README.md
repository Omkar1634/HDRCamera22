```
using Android.App;
using Android.OS;
using Android.Support.V4.App;
using Android.Support.V4.Content;
using Android.Hardware.Camera2;
using Java.Util.Concurrent;
using Android.Widget;
using Android.Content.PM;

[Activity(Label = "CameraActivity", MainLauncher = true)]
public class CameraActivity : Activity
{
    private const int RequestCameraPermission = 1;
    private CameraManager _cameraManager;

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

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
            // Permissions are already granted, proceed to open the camera
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

    // Function to get the list of camera IDs
    private void GetCameraList()
    {
        try
        {
            var cameraIdList = _cameraManager.GetCameraIdList();
            foreach (var cameraId in cameraIdList)
            {
                // Log or show the camera ID
                Android.Util.Log.Info("CameraList", $"Camera ID: {cameraId}");
            }
        }
        catch (CameraAccessException e)
        {
            Android.Util.Log.Error("CameraList", "Failed to access camera: " + e.Message);
        }
    }

    // Open the camera
    private void OpenCamera()
    {
        try
        {
            // Get the list of available cameras
            GetCameraList();

            // Pick the first camera ID (just for demonstration purposes)
            string cameraId = _cameraManager.GetCameraIdList()[0];

            // Create a callback for the camera device state
            CameraDevice.StateCallback stateCallback = new CameraStateCallback();

            // Create an executor to manage the callback execution (we can use the main thread executor)
            IExecutor executor = Executors.NewSingleThreadExecutor();

            // Open the camera using the selected cameraId
            _cameraManager.OpenCamera(cameraId, executor, stateCallback);
        }
        catch (CameraAccessException e)
        {
            Android.Util.Log.Error("CameraAccess", "Failed to open camera: " + e.Message);
        }
    }
}

// CameraDevice.StateCallback to handle camera state changes
public class CameraStateCallback : CameraDevice.StateCallback
{
    public override void OnOpened(CameraDevice camera)
    {
        base.OnOpened(camera);
        // Camera opened successfully, you can now use the camera for preview or recording
        Android.Util.Log.Info("Camera", "Camera opened successfully.");
    }

    public override void OnDisconnected(CameraDevice camera)
    {
        base.OnDisconnected(camera);
        // Camera was disconnected
        Android.Util.Log.Info("Camera", "Camera disconnected.");
    }

    public override void OnError(CameraDevice camera, CameraError error)
    {
        base.OnError(camera, error);
        // An error occurred while accessing the camera
        Android.Util.Log.Error("Camera", $"Camera error: {error}");
    }
}

