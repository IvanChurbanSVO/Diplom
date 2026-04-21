namespace Diplom.Core.Models
{
    public class ProcessSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double CpuUsage { get; set; }
        public long MemoryPrivateBytes { get; set; }
        public long IoReadBytes { get; set; }
        public long IoWriteBytes { get; set; }
        public DateTime LastUpdated { get; set; }

        public int MemoryMB => (int)(MemoryPrivateBytes / (1024 * 1024));

        // Временное поле для отслеживания позиции в сетке
        public int DisplayIndex { get; set; } = -1;
    }
}