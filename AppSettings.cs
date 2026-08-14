using Newtonsoft.Json;
using System;
using System.IO;

namespace ConvertToWebP
{
    public class AppSettings
    {
        private static string SettingsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConvertToWebP", "settings.json");

        private static string LegacySettingsPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public int Quality { get; set; } = 75;
        public bool ResizeEnabled { get; set; } = false;
        public int MaxWidth { get; set; } = 1600;
        public bool AddPrefix { get; set; } = false;
        public bool UseCustomOutput { get; set; } = false;
        public string CustomOutputPath { get; set; } = "";
        public bool StripMetadata { get; set; } = true;
        public int CompressionMethod { get; set; } = 4;

        public static AppSettings Load()
        {
            string path = SettingsPath;
            if (!File.Exists(path) && File.Exists(LegacySettingsPath))
            {
                path = LegacySettingsPath;
            }

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                    if (path == LegacySettingsPath)
                    {
                        settings.Save();
                    }
                    return settings;
                }
                catch
                {
                    return new AppSettings();
                }
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
            }
        }
    }
}
