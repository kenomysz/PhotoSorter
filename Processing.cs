using System.Collections.Concurrent;
using System.IO.Compression;
using System.Formats.Tar;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace PhotoSorter
{
    public class FileProcessor
    {
        private readonly string _sourceFolder;
        private readonly string _outputFolder;
        private readonly BlockingCollection<string> _fileQueue;
        private readonly HttpClient _httpClient;

        public FileProcessor(string sourceFolder, string outputFolder)
        {
            _sourceFolder = sourceFolder;
            _outputFolder = outputFolder;
            _fileQueue = new BlockingCollection<string>();

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhotoSorterApp/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public void AddToQueue(string filePath)
        {
            _fileQueue.Add(filePath);
        }

        public async Task RunAsync()
        {
            foreach (var filePath in _fileQueue.GetConsumingEnumerable())
            {
                try
                {
                    if (System.IO.Directory.Exists(filePath))
                    {
                        Console.WriteLine($"[Folder] Scanning content: {filePath}");
                        var filesInside = System.IO.Directory.GetFiles(filePath, "*.*", SearchOption.AllDirectories);
                        if (filesInside.Length == 0)
                        {
                            RecursiveDeleteEmptyFolders(filePath);
                        }
                        else
                        {
                            foreach (var file in filesInside)
                            {
                                _fileQueue.Add(file);
                            }
                        }
                        continue;
                    }

                    if (!File.Exists(filePath)) continue;

                    if (!await Utils.WaitForFileAccess(filePath))
                    {
                        Console.WriteLine($"Skipped, file locked: {Path.GetFileName(filePath)}");
                        continue;
                    }

                    string ext = Path.GetExtension(filePath).ToLower();
                    bool fileProcessed = false;

                    if (ext == ".zip")
                    {
                        await ProcessZipArchive(filePath);
                        File.Delete(filePath);
                        Console.WriteLine($"[Deleted] Archive: {filePath}");
                        fileProcessed = true;
                    }
                    else if (ext == ".tar")
                    {
                        await ProcessTarArchive(filePath);
                        File.Delete(filePath);
                        Console.WriteLine($"[Deleted] Archive: {filePath}");
                        fileProcessed = true;
                    }
                    else if (Utils.IsImage(ext))
                    {
                        await ProcessSingleImage(filePath, filePath);
                        fileProcessed = true;
                    }
                    if (fileProcessed)
                    {
                        string? parentDir = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            RecursiveDeleteEmptyFolders(parentDir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] {filePath}: {ex.Message}");
                }
            }
        }

        private void RecursiveDeleteEmptyFolders(string path)
        {
            try
            {
                string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                string normalizedSource = Path.GetFullPath(_sourceFolder).TrimEnd(Path.DirectorySeparatorChar);

                if (normalizedPath.Length <= normalizedSource.Length) return;

                if (System.IO.Directory.Exists(path))
                {
                    if (!System.IO.Directory.EnumerateFileSystemEntries(path).Any())
                    {
                        System.IO.Directory.Delete(path);
                        Console.WriteLine($"[Cleanup] Removed empty folder: {path}");

                        var parent = System.IO.Directory.GetParent(path);
                        if (parent != null)
                        {
                            RecursiveDeleteEmptyFolders(parent.FullName);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"[Cleanup] Soft skip for {path}: Folder busy or access denied.");
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cleanup ERROR] Unexpected error for {path}: {ex.Message}");
            }
        }

        private async Task ProcessZipArchive(string zipPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) || entry.Length == 0) continue;

                    if (Utils.IsImage(Path.GetExtension(entry.Name).ToLower()))
                    {
                        string tempFile = Path.GetTempFileName();
                        entry.ExtractToFile(tempFile, true);
                        await ProcessSingleImage(tempFile, entry.Name, isTemp: true);
                    }
                }
                Console.WriteLine($"[ZIP] Extracted and sorted contents of: {Path.GetFileName(zipPath)}");
            }
            catch (Exception ex) { Console.WriteLine($"[ZIP Error] {ex.Message}"); }
        }

        private async Task ProcessTarArchive(string tarPath)
        {
            try
            {
                using var stream = File.OpenRead(tarPath);
                using var reader = new TarReader(stream);

                while (reader.GetNextEntry() is TarEntry entry)
                {
                    if (entry.EntryType == TarEntryType.RegularFile && Utils.IsImage(Path.GetExtension(entry.Name).ToLower()))
                    {
                        string tempFile = Path.GetTempFileName();
                        entry.ExtractToFile(tempFile, true);
                        await ProcessSingleImage(tempFile, entry.Name, isTemp: true);
                    }
                }
                Console.WriteLine($"[TAR] Extracted and sorted contents of: {Path.GetFileName(tarPath)}");
            }
            catch (Exception ex) { Console.WriteLine($"[TAR Error] {ex.Message}"); }
        }

        private async Task ProcessSingleImage(string realPath, string originalName, bool isTemp = false)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(realPath);
                DateTime date = Utils.GetDateFromExif(directories) ?? File.GetCreationTime(realPath);

                var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
                var location = await Utils.GetLocationFromGps(gpsDir, _httpClient);

                string datePath = Path.Combine(date.Year.ToString(), date.Month.ToString("00"), date.Day.ToString("00"));
                string locationPath = Path.Combine(location.Country, location.City);
                string targetDir = Path.Combine(_outputFolder, datePath, locationPath);

                System.IO.Directory.CreateDirectory(targetDir);

                string fileName = Path.GetFileName(originalName);
                string targetPath = Path.Combine(targetDir, fileName);

                int counter = 1;
                while (File.Exists(targetPath))
                {
                    string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    targetPath = Path.Combine(targetDir, $"{nameNoExt}({counter++}){ext}");
                }

                if (isTemp)
                {
                    File.Move(realPath, targetPath);
                    Console.WriteLine($"[Moving] {originalName} -> {targetPath}");
                }
                else
                {
                    File.Move(realPath, targetPath);
                    Console.WriteLine($"[Moving] Extracting from archive: {originalName} -> {targetPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[File Error] {originalName}: {ex.Message}");
            }
            finally
            {
                if (isTemp && File.Exists(realPath)) File.Delete(realPath);
            }
        }
    }
}