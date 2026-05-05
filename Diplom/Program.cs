using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Diplom.Core.Interfaces;
using Diplom.Services;
using Diplom.UI;
using Diplom.Core.Models;
using System.Windows.Forms;

namespace Diplom
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Инициализация визуальных стилей Windows
            ApplicationConfiguration.Initialize();

            try
            {
                // Создаем простую коллекцию сервисов (DI)
                var services = new ServiceCollection();

                // 1. Регистрация настроек (в памяти)
                var appSettings = new AppSettings
                {
                    ScanIntervalMinutes = 5,
                    UiRefreshRateMs = 500
                };
                services.AddSingleton(appSettings);

                // 2. Регистрация сервисов мониторинга
                services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
                services.AddSingleton<ISystemMetricsService, SystemMetricsService>();
                services.AddSingleton<IFileScannerService, FileScannerService>();

                // 3. Регистрация главной формы
                services.AddSingleton<MainForm>();

                // 4. Логгер (консольный)
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

                // Сборка провайдера
                var serviceProvider = services.BuildServiceProvider();

                services.AddSingleton<INetworkMonitorService, NetworkMonitorService>();

                // Запуск формы
                var form = serviceProvider.GetRequiredService<MainForm>();
                Application.Run(form);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Критическая ошибка при запуске:\n{ex.Message}\n\nДетали:\n{ex.StackTrace}",
                    "Ошибка запуска Diplom",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}