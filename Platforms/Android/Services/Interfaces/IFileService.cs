// Services/Interfaces/IFileService.cs
using CameraBurstApp.Models;
using System.Threading.Tasks;

namespace CameraBurstApp.Services.Interfaces
{
    public interface IFileService
    {
        Task<string> CreateSessionFolder(SubjectMetadata metadata);
        Task SaveMetadata(string folderPath, SubjectMetadata metadata);
    }
}