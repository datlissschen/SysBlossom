using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskManager.Models
{
    /// <summary>
    /// Represents information about a subprocess, including its process ID, CPU usage, and memory consumption.
    /// </summary>
    public class SubProcessInfo
    {
        public int Pid { get; set; }
        public double Cpu { get; set; }
        public string Memory { get; set; }
        public string CpuDisplay => $"{Cpu:F1}%";
    }
}
