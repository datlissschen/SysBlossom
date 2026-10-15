namespace TaskManager.Models
{
    /// <summary>
    /// Repräsentiert Informationen über einen einzelnen Unterprozess (PID, CPU-Last und Arbeitsspeicher).
    /// </summary>
    public class SubProcessInfo
    {
        public int Pid { get; init; }
        public double Cpu { get; init; }
        public double MemoryMB { get; init; }

        public string CpuDisplay => $"{Cpu:F1}%";
        public string MemoryDisplay => $"{MemoryMB:F1} MB";
    }
}