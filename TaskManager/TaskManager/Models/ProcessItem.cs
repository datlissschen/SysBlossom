/// <summary>
/// class represents a single row in your task manager.
/// <\summary>
public class ProcessItem
{
    public int Id { get; set; }
    public string Name { get; set; }
    public double CpuUsage { get; set; }
    public string MemoryUsage { get; set; } // Formatted string (e.g., "150 MB")
    public string Status { get; set; }
}