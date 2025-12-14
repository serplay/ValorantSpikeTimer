using System.Text.Json;
using System.IO;

namespace ValorantSpikeTimer
{
    public class Config
    {
        public int LeftPixelX { get; set; }
        public int LeftPixelY { get; set; }
        public int CenterPixelX { get; set; }
        public int CenterPixelY { get; set; }
        public int RightPixelX { get; set; }
        public int RightPixelY { get; set; }

        // RGB thresholds calculated from sampled pixels
        public int RedMin { get; set; } = 120;
        public int GreenMax { get; set; } = 30;
        public int BlueMax { get; set; } = 30;

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ValorantSpikeTimer",
            "config.json"
        );

        public static Config? Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<Config>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VSTimer] Error loading config: {ex.Message}");
            }
            return null;
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(ConfigPath)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                
                System.Diagnostics.Debug.WriteLine($"[VSTimer] Config saved to {ConfigPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VSTimer] Error saving config: {ex.Message}");
            }
        }

        public bool IsValid()
        {
            return LeftPixelX > 0 && LeftPixelY > 0 && 
                   CenterPixelX > 0 && CenterPixelY > 0 &&
                   RightPixelX > 0 && RightPixelY > 0;
        }

        public static string GetConfigPath() => ConfigPath;
    }
}
