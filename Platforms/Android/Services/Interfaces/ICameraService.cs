// Services/Interfaces/ICameraService.cs
using System.Threading.Tasks;

namespace CameraBurstApp.Services.Interfaces
{
    public interface ICameraService
    {
        Task InitializeAsync(object previewView);
        Task StartPreviewAsync();
        Task StartCaptureAsync(string sessionFolder);
        Task StopCaptureAsync();
        Task ShutdownAsync();
        bool IsCameraAvailable();
    }
}