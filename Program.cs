namespace PhotoSorter
{
    class Program
    {
        static string SourceFolder = @"Inbox/";
        static string OutputFolder = @"Sorted/";

        static async Task Main(string[] args)
        {
            Console.WriteLine($"Monitoring: {SourceFolder}");
            Console.WriteLine($"Copying to: {OutputFolder}");

            Directory.CreateDirectory(SourceFolder);
            Directory.CreateDirectory(OutputFolder);

            var processor = new FileProcessor(SourceFolder, OutputFolder);

            using var watcher = new FileSystemWatcher(SourceFolder);
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime;

            watcher.Created += (s, e) => {
                Console.WriteLine($"[DETECTED] {e.Name}");
                processor.AddToQueue(e.FullPath);
            };
            watcher.EnableRaisingEvents = true;

            if (Directory.Exists(SourceFolder))
            {
                var initialEntries = Directory.GetFileSystemEntries(SourceFolder, "*", SearchOption.TopDirectoryOnly);
                foreach (var entry in initialEntries)
                {
                    processor.AddToQueue(entry);
                }
            }
            await processor.RunAsync(); // processing loop (Processing.cs)
        }
    }
}