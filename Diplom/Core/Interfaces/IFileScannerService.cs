using Diplom.Core.Models;

namespace Diplom.Core.Interfaces
{
    public interface IFileScannerService
    {
        /// <summary>
        /// Запуск асинхронного сканирования дисков.
        /// </summary>
        Task StartScanAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Получить последние результаты сканирования (корневой узел дерева).
        /// Возвращает null, если сканирование еще не завершено.
        /// </summary>
        FileNode? GetLastResult();

        /// <summary>
        /// Событие завершения сканирования (можно подписаться для обновления UI).
        /// </summary>
        event Action? ScanCompleted;
    }
}