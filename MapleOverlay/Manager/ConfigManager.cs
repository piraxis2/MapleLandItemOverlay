using System;
using System.IO;
using Newtonsoft.Json;

namespace MapleOverlay.Manager
{
    public class AppConfig
    {
        // 기본값 설정
        public int KeyCapture { get; set; } = 0xC0; // ` (VK_OEM_3)
        public int KeyManualSearch { get; set; } = 0xDC; // \ (VK_OEM_5)
        public int KeyExit { get; set; } = 0x79; // F10 (VK_F10)
        public int KeyClosePanel { get; set; } = 0x1B; // ESC (VK_ESCAPE)
    }

    public class ConfigManager
    {
        private const string ConfigFileName = "config.json";
        public AppConfig Config { get; private set; }

        public ConfigManager()
        {
            LoadConfig();
        }

        public void LoadConfig()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    Config = JsonConvert.DeserializeObject<AppConfig>(json);
                }
                else
                {
                    // 파일이 없으면 기본값으로 생성
                    Config = new AppConfig();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config Load Error: {ex.Message}");
                Config = new AppConfig(); // 오류 시 기본값 사용
            }
        }

        public void SaveConfig()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
                string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config Save Error: {ex.Message}");
            }
        }
    }
}