using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TERMINAL.DeviceHub
{
    public class AppSettings
    {
        public bool IsRs485Enabled { get; set; } = true;
        public List<PortSetting> Rs485Ports { get; set; } = new List<PortSetting>();

        public bool IsUartEnabled { get; set; } = true;
        public List<PortSetting> UartPorts { get; set; } = new List<PortSetting>();

        public bool IsI2CEnabled { get; set; } = true;
        public List<string> I2CDevices { get; set; } = new List<string>();

        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        // Загрузка настроек
        public static AppSettings Load()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch { }
            }
            return new AppSettings();
        }

        // Сохранение настроек
        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                // Можно вывести в лог, если файл занят
                Console.WriteLine($"Ошибка сохранения настроек: {ex.Message}");
            }
        }
    }
}