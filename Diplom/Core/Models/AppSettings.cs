namespace Diplom.Core.Models
{
    public class AppSettings
    {
        public int ScanIntervalMinutes { get; set; } = 5;
        public int UiRefreshRateMs { get; set; } = 500; // 2 Гц

        public AppSettings() { }
    }
}