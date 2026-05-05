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

        // Ограничение глубины: 6 уровней
        private const int MAX_DEPTH = 6;

        public event Action? ScanCompleted;

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
            return Task.Run(() =>
            {
                try
                {
                    _logger.LogInformation("Запуск сканирования...");

                    var root = new FileNode
                    {
                        Name = "Компьютер",
                        Path = "",
                        IsDirectory = true,
                        Level = 0
                    };

                    var drives = DriveInfo.GetDrives();
                    foreach (var drive in drives)
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        if (!drive.IsReady) continue;

                        try
                        {
                            _logger.LogInformation($"Сканирование диска {drive.Name}...");

                            // Сканируем корень диска (уровень 0)
                            var driveNode = ScanFolder(drive.RootDirectory.FullName, 0, cancellationToken);

                            if (driveNode != null)
                            {
                                driveNode.Name = $"{drive.Name} ({drive.DriveFormat})";
                                driveNode.Path = drive.Name;
                                root.Children.Add(driveNode);
                                root.TotalSize += driveNode.TotalSize;
                                root.Size += driveNode.Size;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Ошибка диска {drive.Name}: {ex.Message}");
                        }
                    }

                    _logger.LogInformation($"Сканирование завершено. Найдено: {root.TotalSize / (1024 * 1024 * 1024)} ГБ");

                    lock (_lock)
                    {
                        _lastResult = root;
                    }

                    ScanCompleted?.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"Критическая ошибка: {ex.Message}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Рекурсивный обход папок. БЕЗ лишней асинхронности внутри, чтобы не было StackOverflow.
        /// </summary>
        private FileNode? ScanFolder(string path, int currentDepth, CancellationToken token)
        {
            if (token.IsCancellationRequested) return null;
            if (currentDepth > MAX_DEPTH) return null; // Стоп на 6 уровне

            var node = new FileNode
            {
                Name = new DirectoryInfo(path).Name,
                Path = path,
                IsDirectory = true,
                Level = currentDepth,
                Children = new List<FileNode>()
            };

            try
            {
                var dir = new DirectoryInfo(path);

                // 1. Сначала собираем файлы в ЭТОЙ папке
                foreach (var file in dir.EnumerateFiles())
                {
                    if (token.IsCancellationRequested) break;
                    try
                    {
                        long size = file.Length;
                        node.Children.Add(new FileNode
                        {
                            Name = file.Name,
                            Path = file.FullName,
                            Size = size,
                            TotalSize = size,
                            IsDirectory = false,
                            Level = currentDepth
                        });
                        node.Size += size;
                        node.TotalSize += size;
                    }
                    catch { /* Файл занят или нет доступа - пропускаем */ }
                }

                // 2. Если еще не достигли лимита глубины, идем в подпапки
                if (currentDepth < MAX_DEPTH)
                {
                    foreach (var subDir in dir.EnumerateDirectories())
                    {
                        if (token.IsCancellationRequested) break;

                        // Пропускаем системные ссылки, чтобы не было циклов и StackOverflow
                        if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue;

                        try
                        {
                            var childNode = ScanFolder(subDir.FullName, currentDepth + 1, token);
                            if (childNode != null)
                            {
                                node.Children.Add(childNode);
                                node.TotalSize += childNode.TotalSize;
                            }
                        }
                        catch { /* Папка недоступна - пропускаем */ }
                    }
                }
                else
                {
                    _logger.LogDebug($"Достигнут лимит глубины ({MAX_DEPTH}) для: {path}");
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                _logger.LogWarning($"Ошибка при чтении {path}: {ex.Message}");
            }

            return node;
        }
    }
}