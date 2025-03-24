using CameraBurstApp.Models;
using CameraBurstApp.Services.Interfaces;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
//#if ANDROID
//using AndroidEnvironment = Android.OS.Environment;
//using AndroidApp = Android.App.Application;
//using AndroidMediaScanner = Android.Media.MediaScannerConnection;
//#endif
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

        public async Task<string> SaveImageToGallery(byte[] imageData, string filename)
        {
            try
            {
                // Create a temporary file in our app's cache
                string tempFile = Path.Combine(FileSystem.CacheDirectory, filename);

                // Write the image data to the temp file
                using (FileStream fs = new FileStream(tempFile, FileMode.Create))
                {
                    await fs.WriteAsync(imageData, 0, imageData.Length);
                }

                // Use MAUI's way to get the pictures directory
                string galleryPath = string.Empty;

#if ANDROID
        // For public directory, use this:
        galleryPath = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, 
            Android.OS.Environment.DirectoryPictures);
#else
                galleryPath = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
#endif

                // Create a CameraBurstApp folder in the gallery
                string appGalleryFolder = Path.Combine(galleryPath, "CameraBurstApp");
                if (!Directory.Exists(appGalleryFolder))
                {
                    Directory.CreateDirectory(appGalleryFolder);
                }

                // Final path for the image
                string finalPath = Path.Combine(appGalleryFolder, filename);

                // Copy the file to the gallery folder
                File.Copy(tempFile, finalPath, true);

                // Delete the temp file
                File.Delete(tempFile);

                // Make the file visible in the gallery (Android specific)
#if ANDROID
        Android.Media.MediaScannerConnection.ScanFile(
            Android.App.Application.Context,
            new string[] { finalPath },
            null,
            null);
#endif

                System.Diagnostics.Debug.WriteLine($"Image saved to gallery: {finalPath}");
                return finalPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving to gallery: {ex.Message}");
                return null;
            }
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