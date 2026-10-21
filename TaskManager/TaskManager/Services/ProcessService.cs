using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TaskManager.Models;

namespace TaskManager.Services
{
    public class ProcessService
    {
        private readonly Dictionary<int, (DateTime Time, TimeSpan ProcessorTime)> _history = [];
        private readonly Dictionary<int, string?> _pathCache = [];
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

        // Fast Win32 API to read process executable path without throwing C# MainModule exceptions
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private static string? TryGetProcessPathFast(int pid)
        {
            if (pid <= 4) return null;
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return null;

            try
            {
                var buffer = new StringBuilder(1024);
                uint size = (uint)buffer.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, buffer, ref size))
                {
                    return buffer.ToString();
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
            return null;
        }

        public List<ProcessItem> GetProcesses()
        {
            List<RawProcessData> rawList = [];
            HashSet<int> activeIds = [];
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

                    // Only check Responding if the process actually has a MainWindowHandle (avoids blocking on background services)
                    bool isResponding = p.MainWindowHandle == IntPtr.Zero || p.Responding;

                    double cpuPercent = 0;
                    if (pid > 4)
                    {
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
                        catch
                        {
                            // Protected system process
                        }
                    }

                    // Cache executable path per PID so we only query the OS once per process lifetime
                    if (!_pathCache.TryGetValue(pid, out string? fullPath))
                    {
                        fullPath = TryGetProcessPathFast(pid);
                        _pathCache[pid] = fullPath;
                    }

                    rawList.Add(new RawProcessData(
                        Id: pid,
                        Name: processName,
                        Cpu: Math.Clamp(cpuPercent, 0.0, 100.0),
                        MemoryBytes: workingSet,
                        IsResponding: isResponding,
                        Path: fullPath
                    ));
                }
                catch
                {
                    // Process exited during enumeration
                }
                finally
                {
                    p.Dispose();
                }
            }

            // Clean up exited processes from caches
            var ghostIds = _history.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (int id in ghostIds)
            {
                _history.Remove(id);
                _pathCache.Remove(id);
            }

            var groupedList = rawList
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    double totalMemoryMB = group.Sum(x => x.MemoryBytes) / (1024.0 * 1024.0);
                    string? imagePath = null;
                    foreach (var item in group)
                    {
                        if (!string.IsNullOrEmpty(item.Path))
                        {
                            imagePath = item.Path;
                            break;
                        }
                    }

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

        public static bool IsCriticalProcess(string processName)
        {
            return !string.IsNullOrWhiteSpace(processName) && ProtectedProcesses.Contains(processName);
        }

        public bool KillProcesses(List<int> pids) => KillProcesses(pids, out _);

        public bool KillProcesses(IEnumerable<int> pids, out string? errorMessage)
        {
            errorMessage = null;
            List<string> errors = [];

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