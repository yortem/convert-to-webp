using Newtonsoft.Json;
using System;
using System.IO;

namespace ConvertToWebP
{
    public class AppSettings
    {
        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public int Quality { get; set; } = 75;
        public bool ResizeEnabled { get; set; } = false;
        public int MaxWidth { get; set; } = 1600;
        public bool AddPrefix { get; set; } = false;
        public bool UseCustomOutput { get; set; } = false;
        public string CustomOutputPath { get; set; } = "";
        public bool StripMetadata { get; set; } = true;

        public static AppSettings Load()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
                catch
                {
                    // If load fails, return defaults
                    return new AppSettings();
                }
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Handle save error silently or log if needed
            }
        }
    }
}
