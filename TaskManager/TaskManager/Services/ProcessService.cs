using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
namespace TaskManager
{
    public class ProcessService
    {
        /// <summary>
        /// Method to get the tasks and their details. 
        /// It retrieves all running processes, formats their memory usage, and checks if they are responding.
        /// </summary>
        /// <returns></returns>
        public List<ProcessItem> GetProcesses()
        {
            var processList = new List<ProcessItem>();
            Process[] allProcesses = Process.GetProcesses();

            foreach (var p in allProcesses)
            {
                try
                {
                    processList.Add(new ProcessItem
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        // WorkingSet64 is the physical memory usage
                        MemoryUsage = $"{(p.WorkingSet64 / 1024 / 1024)} MB",
                        Status = p.Responding ? "Active" : "Hung"
                    });
                }
                catch (Exception)
                {
                    // Skip processes we don't have permission to see
                    continue;
                }
            }
            return processList.OrderBy(x => x.Name).ToList();
        }

        /// <summary>
        /// Method to kill the task via the given id.
        /// </summary>
        /// <param name="id"></param>
        public bool KillProcess(int id)
        {
            try
            {
                var p = Process.GetProcessById(id);
                p.Kill();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to kill process: {ex.Message}");
                return false;
            }
        }

    }
}