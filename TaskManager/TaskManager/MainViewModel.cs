using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TaskManager.Models;
using TaskManager.Services;
using TaskManager.Helper;

namespace TaskManager
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ProcessService _service = new();
        public ObservableCollection<ProcessItem> Processes { get; } = new();

        // Data for view
        private double _totalCpu;
        public double TotalCpu { get => _totalCpu; set { _totalCpu = value; OnPropertyChanged(); } }

        private double _totalRam;
        public double TotalRam { get => _totalRam; set { _totalRam = value; OnPropertyChanged(); } }

        public string FilterText { get; set; } = string.Empty;

        // helper properties for sorting
        public string SortColumn { get; set; } = "CPU";
        public bool IsAscending { get; set; } = false;

        public async Task RefreshDataAsync()
        {
            var allData = await Task.Run(() => _service.GetProcesses());

            var filteredData = allData.Where(p =>
                string.IsNullOrEmpty(FilterText) ||
                p.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

            // Sort
            IEnumerable<ProcessItem> sortedQuery = SortColumn switch
            {
                "Name" => IsAscending ? filteredData.OrderBy(p => p.Name) : filteredData.OrderByDescending(p => p.Name),
                "Memory" => IsAscending ? filteredData.OrderBy(p => ParseMemory(p.MemoryUsage)) : filteredData.OrderByDescending(p => ParseMemory(p.MemoryUsage)),
                _ => IsAscending ? filteredData.OrderBy(p => p.CpuUsage) : filteredData.OrderByDescending(p => p.CpuUsage)
            };

            var sortedList = sortedQuery.ToList();

            // Synchro thenization logic
            var currentItems = Processes.ToDictionary(p => p.Name);
            var newNames = sortedList.Select(p => p.Name).ToHashSet();

            for (int i = Processes.Count - 1; i >= 0; i--)
            {
                if (!newNames.Contains(Processes[i].Name)) Processes.RemoveAt(i);
            }

            bool isUserBusy = Processes.Any(p => p.IsFlyoutOpen);

            for (int i = 0; i < sortedList.Count; i++)
            {
                var newItem = sortedList[i];
                if (currentItems.TryGetValue(newItem.Name, out var existingItem))
                {
                    existingItem.CpuUsage = newItem.CpuUsage;
                    existingItem.MemoryUsage = newItem.MemoryUsage;
                    existingItem.SubProcesses = newItem.SubProcesses;
                    if (!isUserBusy)
                    {
                        int oldIndex = Processes.IndexOf(existingItem);
                        if (oldIndex != i) Processes.Move(oldIndex, i);
                    }
                }
                else
                {
                    Processes.Insert(i, newItem);
                }
            }

            // calculating totals
            TotalCpu = sortedList.Sum(p => p.CpuUsage);
            TotalRam = sortedList.Sum(p => ParseMemory(p.MemoryUsage));
        }

        private long ParseMemory(string memString) =>
            long.TryParse(System.Text.RegularExpressions.Regex.Match(memString, @"\d+").Value, out long res) ? res : 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}