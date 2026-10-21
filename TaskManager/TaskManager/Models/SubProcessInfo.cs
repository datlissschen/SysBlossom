namespace TaskManager.Models
{
    /// <summary>
    /// Represents information about a subprocess, including its process ID, CPU usage, and memory consumption.
    /// </summary>
    public partial class SubProcessInfo
    {
        public int Pid { get; set; }
        public double Cpu { get; set; }
        public double MemoryMB { get; set; }

        // Kept 'Memory' property name so existing XAML bindings (x:Bind Memory) do not break!
        public string Memory => $"{MemoryMB:F0} MB";
        public string CpuDisplay => $"{Cpu:F1}%";
    }
}