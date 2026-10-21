using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using TaskManager.Models;

namespace TaskManager.Services
{
    public class ProcessService
    {
        private readonly Dictionary<int, (DateTime Time, TimeSpan ProcessorTime)> _history = new();
        private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);
        private readonly int _currentProcessId = Environment.ProcessId;

        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit",
            "services", "lsass", "svchost", "winlogon", "dwm", "fontdrvhost", "Memory Compression"
        };

        private readonly record struct RawProcessData(
            int Id,
            string Name,
            double Cpu,
            long MemoryBytes,
            bool IsResponding,
            string? Path);

        public List<ProcessItem> GetProcesses()
        {
            var rawList = new List<RawProcessData>();
            var activeIds = new HashSet<int>();
            Process[] allProcesses = Process.GetProcesses();
            DateTime currentTime = DateTime.UtcNow;

            foreach (Process p in allProcesses)
            {
                try
                {
                    int pid = p.Id;
                    activeIds.Add(pid);

                    string processName = p.ProcessName;
                    long workingSet = p.WorkingSet64;
                    bool isResponding = true;

                    try
                    {
                        isResponding = p.Responding;
                    }
                    catch { }

                    double cpuPercent = 0;
                    try
                    {
                        TimeSpan currentProcessorTime = p.TotalProcessorTime;
                        if (_history.TryGetValue(pid, out var lastValues))
                        {
                            double timeDiffMs = (currentTime - lastValues.Time).TotalMilliseconds;
                            double cpuDiffMs = (currentProcessorTime - lastValues.ProcessorTime).TotalMilliseconds;

                            if (timeDiffMs > 0 && cpuDiffMs >= 0)
                            {
                                cpuPercent = (cpuDiffMs / timeDiffMs) * 100.0 / _processorCount;
                            }
                        }
                        _history[pid] = (currentTime, currentProcessorTime);
                    }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }

                    string? fullPath = null;
                    try
                    {
                        fullPath = p.MainModule?.FileName;
                    }
                    catch { }

                    rawList.Add(new RawProcessData(
                        Id: pid,
                        Name: processName,
                        Cpu: Math.Clamp(cpuPercent, 0.0, 100.0),
                        MemoryBytes: workingSet,
                        IsResponding: isResponding,
                        Path: fullPath
                    ));
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            var ghostIds = _history.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (int id in ghostIds)
            {
                _history.Remove(id);
            }

            var groupedList = rawList
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    double totalMemoryMB = group.Sum(x => x.MemoryBytes) / (1024.0 * 1024.0);
                    string? imagePath = group.Select(x => x.Path).FirstOrDefault(path => !string.IsNullOrEmpty(path));

                    return new ProcessItem
                    {
                        Name = group.Key,
                        SubProcesses = group.Select(x => new SubProcessInfo
                        {
                            Pid = x.Id,
                            Cpu = x.Cpu,
                            MemoryMB = x.MemoryBytes / (1024.0 * 1024.0)
                        }).ToList(),
                        CpuUsage = Math.Clamp(group.Sum(x => x.Cpu), 0.0, 100.0),
                        MemoryMB = totalMemoryMB,
                        Status = group.All(x => x.IsResponding) ? "Active" : "Hung",
                        ImagePath = imagePath
                    };
                })
                .OrderByDescending(x => x.CpuUsage)
                .ToList();

            return groupedList;
        }

        public bool IsCriticalProcess(string processName)
        {
            return !string.IsNullOrWhiteSpace(processName) && ProtectedProcesses.Contains(processName);
        }

        public bool KillProcesses(List<int> pids) => KillProcesses(pids, out _);

        public bool KillProcesses(IEnumerable<int> pids, out string? errorMessage)
        {
            errorMessage = null;
            var errors = new List<string>();

            foreach (int pid in pids.Distinct())
            {
                if (pid <= 4 || pid == _currentProcessId)
                {
                    errors.Add($"PID {pid} is protected and cannot be terminated.");
                    continue;
                }

                try
                {
                    using Process p = Process.GetProcessById(pid);

                    if (ProtectedProcesses.Contains(p.ProcessName))
                    {
                        errors.Add($"System process '{p.ProcessName}' (PID {pid}) cannot be terminated for safety reasons.");
                        continue;
                    }

                    p.Kill();
                }
                catch (ArgumentException) { }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    Debug.WriteLine($"Failed to kill PID {pid}: {ex.Message}");
                    errors.Add($"PID {pid}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                errorMessage = string.Join(Environment.NewLine, errors);
                return false;
            }

            return true;
        }
    }
}