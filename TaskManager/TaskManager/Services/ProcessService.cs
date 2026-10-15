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

        /// <summary>
        /// Liste kritischer Windows-Systemprozesse, die niemals beendet werden dürfen (Schutz vor BSOD/Systemabsturz).
        /// </summary>
        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit",
            "services", "lsass", "svchost", "winlogon", "dwm", "fontdrvhost", "Memory Compression"
        };

        /// <summary>
        /// Leichtgewichtiges Struct als Ersatz für 'dynamic', um Boxing und DLR-Overhead zu vermeiden.
        /// </summary>
        private readonly record struct RawProcessData(
            int Id,
            string Name,
            double Cpu,
            long MemoryBytes,
            bool IsResponding,
            string? Path);

        /// <summary>
        /// Liest alle laufenden Prozesse aus, berechnet die CPU-Auslastung und gibt alle OS-Handles sauber frei.
        /// </summary>
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
                    catch
                    {
                        // Zugriff auf Responding kann bei manchen Systemprozessen fehlschlagen
                    }

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
                    catch (Win32Exception) { /* Zugriff verweigert (geschützter Prozess) */ }
                    catch (InvalidOperationException) { /* Prozess wurde bereits beendet */ }

                    string? fullPath = null;
                    try
                    {
                        fullPath = p.MainModule?.FileName;
                    }
                    catch
                    {
                        // Zugriff auf MainModule (z. B. 32/64-Bit-Barriere oder Systemprozess) nicht erlaubt
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
                    // Prozess ist während der Abfrage beendet worden oder vollständig geschützt
                }
                finally
                {
                    // WICHTIG: Gibt das native Betriebssystem-Handle sofort frei!
                    p.Dispose();
                }
            }

            // Verwaiste Prozess-IDs aus der Historie bereinigen (verhindert Memory Leak)
            var ghostIds = _history.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (int id in ghostIds)
            {
                _history.Remove(id);
            }

            // Gruppierung nach Prozessname
            var groupedList = rawList
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    double totalMemoryMB = group.Sum(x => x.MemoryBytes) / (1024.0 * 1024.0);
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
                        ImagePath = group.FirstOrDefault(x => !string.IsNullOrEmpty(x.Path)).Path
                    };
                })
                .OrderByDescending(x => x.CpuUsage)
                .ToList();

            return groupedList;
        }

        /// <summary>
        /// Prüft, ob eine Prozessgruppe kritische Systemprozesse enthält.
        /// </summary>
        public bool IsCriticalProcess(string processName)
        {
            return !string.IsNullOrWhiteSpace(processName) && ProtectedProcesses.Contains(processName);
        }

        /// <summary>
        /// Beendet die angegebenen Prozess-IDs unter Einhaltung von Sicherheitsstandards.
        /// </summary>
        public bool KillProcesses(IEnumerable<int> pids, out string? errorMessage)
        {
            errorMessage = null;
            var errors = new List<string>();

            foreach (int pid in pids.Distinct())
            {
                if (pid <= 4 || pid == _currentProcessId)
                {
                    errors.Add($"PID {pid} ist geschützt und darf nicht beendet werden.");
                    continue;
                }

                try
                {
                    using Process p = Process.GetProcessById(pid);

                    if (ProtectedProcesses.Contains(p.ProcessName))
                    {
                        errors.Add($"Systemprozess '{p.ProcessName}' (PID {pid}) darf aus Sicherheitsgründen nicht beendet werden.");
                        continue;
                    }

                    p.Kill();
                }
                catch (ArgumentException)
                {
                    // Prozess existiert bereits nicht mehr -> kein Fehler
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    Debug.WriteLine($"Fehler beim Beenden von PID {pid}: {ex.Message}");
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