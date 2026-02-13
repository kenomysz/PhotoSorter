using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Text.Json;

using ExifDirectory = MetadataExtractor.Directory;

namespace PhotoSorter
{
    public static class Utils
    {
        public static bool IsImage(string ext) => new[] { ".jpg", ".jpeg", ".png", ".tiff" }.Contains(ext.ToLower());

        public static string SanitizePath(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        public static async Task<bool> WaitForFileAccess(string filePath, int maxRetries = 10, int delayMs = 500)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        if (fs.Length >= 0) return true;
                }
                catch (IOException) { // wait
                                      }
                await Task.Delay(delayMs);
            }
            return false;
        }

        public static DateTime? GetDateFromExif(IReadOnlyList<ExifDirectory> directories)
        {
            var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfdDirectory != null && subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime date))
                return date;
            return null;
        }

        public static async Task<(string Country, string City)> GetLocationFromGps(GpsDirectory? gps, HttpClient httpClient)
        {
            var coords = TryGetCoordinates(gps);

            if (coords == null) return ("Unknown", "No_GPS");

            await Task.Delay(1100); // nominatim delay

            try
            {
                var lat = coords.Value.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var lon = coords.Value.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={lat}&lon={lon}";

                var json = await httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("address", out var address))
                    return ("Unknown", "API_Error");

                string country = GetJsonValue(address, "country") ?? "Unknown_Country";
                string city = GetJsonValue(address, "city")
                           ?? GetJsonValue(address, "town")
                           ?? GetJsonValue(address, "village")
                           ?? "Remote_Area";

                return (SanitizePath(country), SanitizePath(city));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Exception: {ex.Message}");
                return ("Network_Error", "Offline");
            }
        }
        private static (double Lat, double Lon)? TryGetCoordinates(GpsDirectory? gps)
        {
            if (gps == null) return null;

            var latVals = gps.GetRationalArray(GpsDirectory.TagLatitude);
            var lonVals = gps.GetRationalArray(GpsDirectory.TagLongitude);

            if (latVals == null || lonVals == null || latVals.Length != 3 || lonVals.Length != 3)
                return null;

            double lat = latVals[0].ToDouble() + (latVals[1].ToDouble() / 60.0) + (latVals[2].ToDouble() / 3600.0);
            double lon = lonVals[0].ToDouble() + (lonVals[1].ToDouble() / 60.0) + (lonVals[2].ToDouble() / 3600.0);

            var latRef = gps.GetString(GpsDirectory.TagLatitudeRef) ?? "N";
            var lonRef = gps.GetString(GpsDirectory.TagLongitudeRef) ?? "E";

            if (latRef.Equals("S", StringComparison.OrdinalIgnoreCase)) lat = -lat;
            if (lonRef.Equals("W", StringComparison.OrdinalIgnoreCase)) lon = -lon;

            return (lat, lon);
        }

        private static string? GetJsonValue(JsonElement element, string key)
            => element.TryGetProperty(key, out var val) ? val.GetString() : null;
    }
}