namespace TaskManager
{
    /// <summary>
    /// class represents a single row in your task manager.
    /// <\summary>
    public class ProcessItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double CpuUsage { get; set; }
        public string CpuUsageDisplay => $"{CpuUsage:F1}%";
        public string MemoryUsage { get; set; } = string.Empty; // Formatted string (e.g., "150 MB")
        public string Status { get; set; } = "Active";

    }
}