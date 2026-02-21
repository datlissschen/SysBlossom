using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class ProcessService
{
	/// <summary>
	/// Method to get the tasks and their details. 
	/// It retrieves all running processes, formats their memory usage, and checks if they are responding.
	/// </summary>
	/// <returns></returns>
	public List<ProcessItem> GetProcesses()
	{
		return Process.GetProcesses()
			.Select(p => new ProcessItem
			{
				Id = p.Id,
				Name = p.ProcessName,
				MemoryUsage = $"{p.WorkingSet64 / 1024 / 1024} MB",
				Status = p.Responding ? "Running" : "Not Responding"
			})
			.OrderBy(p => p.Name)
			.ToList();
	}

	/// <summary>
	/// Method to kill the task via the given id.
	/// </summary>
	/// <param name="id"></param>
	public void KillProcess(int id)
	{
		try { Process.GetProcessById(id).Kill(); }
		catch (Exception) { /* Handle access denied */ }
	}
}