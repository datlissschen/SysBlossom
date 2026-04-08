using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;

namespace TaskManager
{
    public class ProcessService
    {
        /// <summary>
        /// Functions to save the last time where something has been calculated.
        /// </summary>
        private readonly Dictionary<int, (DateTime Time, TimeSpan ProcessorTime)> _history = new();

        /// <summary>
        /// Function to take care of the starting of the counting of the processor.
        /// </summary>
        private readonly int _processorCount = Environment.ProcessorCount;

        /// <summary>
        /// Method to get the tasks and their details. 
        /// It retrieves all running processes, formats their memory usage, and checks if they are responding.
        /// </summary>
        /// <returns></returns>
        public List<ProcessItem> GetProcesses()
        {
            var processList = new List<ProcessItem>();
            var rawList = new List<dynamic>();
            var allProcesses = Process.GetProcesses();
            var currentTime = DateTime.UtcNow;

            foreach (var p in allProcesses)
            {
                try
                {
                    var currentProcessorTime = p.TotalProcessorTime;
                    double cpuPercent = 0;

                    if (_history.TryGetValue(p.Id, out var lastValues))
                    {
                        // count difference
                        var timeDiff = currentTime - lastValues.Time;
                        var cpuDiff = currentProcessorTime - lastValues.ProcessorTime;

                        // calculate CPU real time 
                        cpuPercent = (cpuDiff.TotalMilliseconds / timeDiff.TotalMilliseconds) * 100 / _processorCount;
                    }

                    // save values
                    _history[p.Id] = (currentTime, currentProcessorTime);
                    string? fullPath = null;
                    try
                    {
                        fullPath = p.MainModule?.FileName;
                    }
                    catch { /* Access Denied for path */ }

                    rawList.Add(new
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        Cpu = Math.Clamp(cpuPercent, 0, 1000), 
                        MemoryRaw = p.WorkingSet64, 
                        IsResponding = p.Responding,
                        Path = p.MainModule?.FileName
                    });
                }
                catch
                {
                    // Access denied
                    continue;
                }
            }
            var activeIds = allProcesses.Select(p => p.Id).ToHashSet();
            var ghostIds = _history.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (var id in ghostIds) _history.Remove(id);

            var groupedList = rawList
            .GroupBy(p => p.Name)
            .Select(group => new ProcessItem
            {
                Name = group.Key,
                // create a list of subprocesses for each process group
                SubProcesses = group.Select(x => new SubProcessInfo
                {
                    Pid = (int)x.Id,
                    Cpu = (double)x.Cpu,
                    Memory = $"{(long)x.MemoryRaw / 1024 / 1024} MB"
                }).ToList(),

                CpuUsage = group.Sum(x => (double)x.Cpu),
                MemoryUsage = $"{(group.Sum(x => (long)x.MemoryRaw) / 1024 / 1024)} MB",
                Status = group.Any(x => (bool)x.IsResponding) ? "Active" : "Hung",
                ImagePath = group.FirstOrDefault(x => x.Path != null)?.Path
            })
            .OrderByDescending(x => x.CpuUsage)
            .ToList();

            return groupedList;
        }

        /// <summary>
        /// Method to kill the task via the given id.
        /// </summary>
        /// <param name="id"></param>
        public bool KillProcesses(List<int> pids)
        {
            try
            {
                foreach (var pid in pids)
                {
                    var p = Process.GetProcessById(pid);
                    p.Kill();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to kill process: {ex.Message}");
                return false;
            }
        }

        private ImageSource? GetIcon(string fileName)
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(fileName)!)
                {
                    // Convert System.Drawing.Icon to a WinUI compatible ImageSource
                    return null; 
                }
            }
            catch { return null; }
        }

    }
}