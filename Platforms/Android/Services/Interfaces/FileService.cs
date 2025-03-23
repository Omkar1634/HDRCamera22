// Services/FileService.cs
using CameraBurstApp.Models;
using CameraBurstApp.Services.Interfaces;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace CameraBurstApp.Services
{
    public class FileService : IFileService
    {
        public async Task<string> CreateSessionFolder(SubjectMetadata metadata)
        {
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CameraBurstApp");

            // Create the base directory if it doesn't exist
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            // Create a subject folder if it doesn't exist
            string subjectFolder = Path.Combine(baseDir, SanitizeFileName(metadata.Name));
            if (!Directory.Exists(subjectFolder))
            {
                Directory.CreateDirectory(subjectFolder);
            }

            // Create a unique session folder with timestamp and take number
            string sessionFolder = Path.Combine(
                subjectFolder,
                $"Take{metadata.TakeNumber}_{DateTime.Now:yyyyMMdd_HHmmss}");

            if (!Directory.Exists(sessionFolder))
            {
                Directory.CreateDirectory(sessionFolder);
            }

            // Save metadata file
            await SaveMetadata(sessionFolder, metadata);

            return sessionFolder;
        }

        public async Task SaveMetadata(string folderPath, SubjectMetadata metadata)
        {
            string metadataPath = Path.Combine(folderPath, "metadata.json");

            using (FileStream fs = File.Create(metadataPath))
            {
                await JsonSerializer.SerializeAsync(fs, metadata, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
        }

        private string SanitizeFileName(string fileName)
        {
            // Remove any invalid characters from the filename
            string invalidChars = new string(Path.GetInvalidFileNameChars());
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }

            return fileName;
        }
    }
}