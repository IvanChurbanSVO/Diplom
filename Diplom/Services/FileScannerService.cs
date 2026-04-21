using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Diplom.Core.Interfaces;
using Diplom.Core.Models;

namespace Diplom.Services
{
    public class FileScannerService : IFileScannerService
    {
        private readonly ILogger<FileScannerService> _logger;
        private FileNode? _lastResult;
        private readonly object _lock = new();

        public event Action? ScanCompleted;
        public event Action<string>? StatusUpdated; // Событие для статуса

        public FileScannerService(ILogger<FileScannerService> logger)
        {
            _logger = logger;
        }

        public FileNode? GetLastResult()
        {
            lock (_lock) { return _lastResult; }
        }

        public Task StartScanAsync(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                try
                {
                    StatusUpdated?.Invoke("Начало сканирования всех дисков...");
                    _logger.LogInformation("=== НАЧАЛО ПОЛНОГО СКАНИРОВАНИЯ ===");

                    var root = new FileNode
                    {
                        Name = "Все диски",
                        Path = "",
                        IsDirectory = true,
                        Size = 0
                    };

                    var drives = DriveInfo.GetDrives();
                    long totalScannedSize = 0;
                    int totalFilesCount = 0;

                    foreach (var drive in drives)
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        if (!drive.IsReady) continue;

                        StatusUpdated?.Invoke($"Сканирование диска {drive.Name}...");
                        _logger.LogInformation($"-> Диск {drive.Name} ({drive.DriveFormat})");

                        try
                        {
                            var driveNode = await ScanDirectoryAsync(drive.RootDirectory.FullName, cancellationToken);

                            if (driveNode.TotalSize > 0)
                            {
                                driveNode.Name = $"{drive.Name} ({FormatSize(driveNode.TotalSize)})";
                                root.Children.Add(driveNode);
                                totalScannedSize += driveNode.TotalSize;
                                totalFilesCount += CountFiles(driveNode);

                                _logger.LogInformation($"   Диск {drive.Name} завершен. Файлов: {CountFiles(driveNode)}, Размер: {FormatSize(driveNode.TotalSize)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Ошибка диска {drive.Name}");
                        }
                    }

                    root.Size = totalScannedSize;
                    root.TotalSize = totalScannedSize;

                    lock (_lock)
                    {
                        _lastResult = root;
                    }

                    StatusUpdated?.Invoke($"Готово! Найдено {totalFilesCount:N0} файлов на {FormatSize(totalScannedSize)}");
                    _logger.LogInformation($"=== СКАНИРОВАНИЕ ЗАВЕРШЕНО. Всего: {FormatSize(totalScannedSize)} ===");

                    ScanCompleted?.Invoke();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Сканирование отменено.");
                    StatusUpdated?.Invoke("Отменено пользователем.");
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Критическая ошибка сканера.");
                    StatusUpdated?.Invoke($"Ошибка: {ex.Message}");
                }
            }, cancellationToken);
        }

        private int CountFiles(FileNode node)
        {
            int count = node.IsDirectory ? 0 : 1;
            foreach (var child in node.Children) count += CountFiles(child);
            return count;
        }

        private async Task<FileNode> ScanDirectoryAsync(string path, CancellationToken cancellationToken)
        {
            var node = new FileNode
            {
                Path = path,
                Name = new DirectoryInfo(path).Name,
                IsDirectory = true,
                Size = 0,
                TotalSize = 0
            };

            try
            {
                var dirInfo = new DirectoryInfo(path);

                // 1. Сначала файлы в этой папке
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        var fNode = new FileNode
                        {
                            Path = file.FullName,
                            Name = file.Name,
                            Size = file.Length,
                            TotalSize = file.Length,
                            IsDirectory = false
                        };
                        node.Children.Add(fNode);
                        node.Size += file.Length;
                    }
                    catch { /* Игнорируем недоступные файлы */ }
                }

                // 2. Потом папки (рекурсия)
                foreach (var dir in dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        // Асинхронная рекурсия с небольшой задержкой, чтобы UI успевал дышать
                        var child = await ScanDirectoryAsync(dir.FullName, cancellationToken);
                        node.Children.Add(child);
                        node.Size += child.TotalSize;
                    }
                    catch { /* Игнорируем недоступные папки */ }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            node.TotalSize = node.Size;
            return node;
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}