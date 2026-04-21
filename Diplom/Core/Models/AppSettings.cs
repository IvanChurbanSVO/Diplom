namespace Diplom.Core.Models
{
    public class ImageRangeConfig
    {
        public string RangeName { get; set; } = string.Empty;
        public long MinSizeBytes { get; set; }
        public long MaxSizeBytes { get; set; }
        public string? CustomIconPath { get; set; }
    }

    public class AppSettings
    {
        // Конструктор по умолчанию (нужен для Configure<> и сериализации JSON)
        public AppSettings()
        {
            ImageRanges = new List<ImageRangeConfig>();
            ScanIntervalMinutes = 5;
            UiRefreshRateMs = 500;
        }

        // Конструктор с параметрами (нужен для твоего кода в Program.cs)
        public AppSettings(int scanIntervalMinutes, int uiRefreshRateMs, List<ImageRangeConfig> imageRanges)
        {
            ScanIntervalMinutes = scanIntervalMinutes;
            UiRefreshRateMs = uiRefreshRateMs;
            ImageRanges = imageRanges ?? new List<ImageRangeConfig>();
        }

        public int ScanIntervalMinutes { get; set; }
        public int UiRefreshRateMs { get; set; }
        public List<ImageRangeConfig> ImageRanges { get; set; }
    }
}