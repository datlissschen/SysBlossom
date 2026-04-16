using Microsoft.UI.Xaml;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TaskManager.Helper;
using TaskManager.Models;
using TaskManager.Services;

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

        private readonly ConcurrentQueue<ProcessItem> _iconQueue = new();
        private Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;

        /// <summary>
        /// Initializes the component with the specified dispatcher queue.
        /// </summary>
        /// <param name="dispatcher">The dispatcher queue to associate with the component. Cannot be null.</param>
        public void Initialize(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
            _ = StartIconWorker();
        }

        /// <summary>
        /// Method to start a background worker that processes the icon queue and updates the UI accordingly.
        /// </summary>
        /// <returns></returns>
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

                    if (!string.IsNullOrEmpty(newItem.ImagePath))
                    {
                        _iconQueue.Enqueue(newItem);
                    }
                }
            }

            // calculating totals
            TotalCpu = sortedList.Sum(p => p.CpuUsage);
            TotalRam = sortedList.Sum(p => ParseMemory(p.MemoryUsage));

            // Values have changed, notify UI to update header display
            OnPropertyChanged(nameof(TotalCpuDisplay));
            OnPropertyChanged(nameof(TotalRamDisplay));
        }

        private long ParseMemory(string memString) =>
            long.TryParse(System.Text.RegularExpressions.Regex.Match(memString, @"\d+").Value, out long res) ? res : 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // formatting CPU for header display
        public string TotalCpuDisplay => $"{TotalCpu:F1}%";

        // formatting RAM for header display
        public string TotalRamDisplay => TotalRam > 1024
            ? $"{(TotalRam / 1024.0):F2} GB"
            : $"{TotalRam:F0} MB";

        /// <summary>
        /// Method to start a background worker that continuously processes the icon queue, extracting icons for processes and updating the UI accordingly. 
        /// The worker runs indefinitely, checking for new items in the queue and updating the UI with the extracted icons while handling potential file access issues gracefully.
        /// </summary>
        /// <returns></returns>
        private async Task StartIconWorker()
        {
            while (true)
            {
                if (_iconQueue.TryDequeue(out var item))
                {
                    if (IconCache.TryGetIcon(item.ImagePath, out var cachedIcon))
                    {
                        _dispatcher.TryEnqueue(() => {
                            item.IconSource = cachedIcon;
                        });
                    }
                    else if (item.IconSource == null && !string.IsNullOrEmpty(item.ImagePath))
                    {
                        await Task.Run(() =>
                        {
                            try
                            {
                                if (File.Exists(item.ImagePath))
                                {
                                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(item.ImagePath);
                                    if (icon != null)
                                    {
                                        var softwareBitmap = IconHelper.GetSoftwareBitmapFromIcon(icon);

                                        _dispatcher.TryEnqueue(async () =>
                                        {
                                            var bitmapSource = new Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource();
                                            await bitmapSource.SetBitmapAsync(softwareBitmap);
                                            item.IconSource = bitmapSource;
                                        });
                                    }
                                }
                            }
                            catch { /*Acces denied or file locked - ignore */ }
                        });
                    }
                    await Task.Delay(20); 
                }
                else
                {
                    await Task.Delay(400); 
                }
            }
        }
    }
}