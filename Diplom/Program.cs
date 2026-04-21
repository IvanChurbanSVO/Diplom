using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        static async Task Main()
        {
            // Включаем визуальные стили Windows
            ApplicationConfiguration.Initialize();

            try
            {
                // Создаем хост приложения (DI Container)
                using var host = Host.CreateDefaultBuilder()
                    .ConfigureServices((context, services) =>
                    {
                        // 1. Настройки
                        var appSettings = new AppSettings(
                            scanIntervalMinutes: 5,
                            uiRefreshRateMs: 500, // 2 раза в секунду
                            imageRanges: new List<ImageRangeConfig>()
                        );
                        services.AddSingleton(appSettings);

                        // 2. Сервисы мониторинга
                        services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
                        services.AddSingleton<ISystemMetricsService, SystemMetricsService>();

                        // 3. Главная форма
                        services.AddSingleton<MainForm>();

                        // 4. Логгер
                        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

                        services.AddSingleton<IFileScannerService, FileScannerService>();

                    })
                    .Build();

                // Получаем логгер
                var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("Startup");
                logger.LogInformation("Приложение запущено. Инициализация...");

                // Получаем форму через DI
                var form = host.Services.GetRequiredService<MainForm>();
                logger.LogInformation("Форма создана. Запуск цикла приложения...");

                // ЗАПУСК ФОРМЫ
                // Важно: Application.Run блокирует поток до закрытия формы
                Application.Run(form);

                logger.LogInformation("Приложение завершено.");
            }
            catch (Exception ex)
            {
                // Если ошибка произошла ДО запуска формы
                MessageBox.Show(
                    $"Критическая ошибка при запуске:\n{ex.Message}\n\nДетали:\n{ex.StackTrace}",
                    "Ошибка запуска Diplom",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                // Пробуем запустить форму в аварийном режиме (без сервисов), чтобы увидеть интерфейс
                try
                {
                    var emptyForm = new Form() { Text = "Ошибка запуска", Width = 400, Height = 300 };
                    var lbl = new Label() { Text = $"Не удалось загрузить приложение:\n{ex.Message}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
                    emptyForm.Controls.Add(lbl);
                    Application.Run(emptyForm);
                }
                catch { }
            }
        }
    }
}