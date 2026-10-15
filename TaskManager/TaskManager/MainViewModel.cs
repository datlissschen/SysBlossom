using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TaskManager.Helper;
using TaskManager.Models;
using TaskManager.Services;

namespace TaskManager
{
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ProcessService _service;
        private readonly ConcurrentQueue<ProcessItem> _iconQueue = new();
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private CancellationTokenSource? _cts;
        private DispatcherQueue? _dispatcher;
        private bool _isDisposed;

        public ObservableCollection<ProcessItem> Processes { get; } = new();

        private double _totalCpu;
        public double TotalCpu
        {
            get => _totalCpu;
            set
            {
                if (Math.Abs(_totalCpu - value) < 0.05) return;
                _totalCpu = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalCpuDisplay));
            }
        }

        private double _totalRam;
        public double TotalRam
        {
            get => _totalRam;
            set
            {
                if (Math.Abs(_totalRam - value) < 0.5) return;
                _totalRam = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalRamDisplay));
            }
        }

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (_filterText == value) return;
                _filterText = value;
                OnPropertyChanged();
            }
        }

        public string SortColumn { get; set; } = "CPU";
        public bool IsAscending { get; set; } = false;

        public string TotalCpuDisplay => $"{TotalCpu:F1}%";
        public string TotalRamDisplay => TotalRam > 1024
            ? $"{(TotalRam / 1024.0):F2} GB"
            : $"{TotalRam:F0} MB";

        public MainViewModel() : this(new ProcessService())
        {
        }

        public MainViewModel(ProcessService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Initialisiert das ViewModel mit dem UI-Dispatcher und startet den Hintergrund-Worker für Icons.
        /// </summary>
        public void Initialize(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _ = StartIconWorkerAsync(_cts.Token);
        }

        /// <summary>
        /// Aktualisiert die Prozessliste asynchron und verhindert überlappende Aufrufe via SemaphoreSlim.
        /// </summary>
        public async Task RefreshDataAsync()
        {
            if (_isDisposed || !await _refreshLock.WaitAsync(0))
                return;

            try
            {
                var allData = await Task.Run(() => _service.GetProcesses());

                // Gesamtauslastung über ALLE Prozesse berechnen (unabhängig vom Suchfilter)
                double overallCpu = Math.Clamp(allData.Sum(p => p.CpuUsage), 0.0, 100.0);
                double overallRam = allData.Sum(p => p.MemoryMB);

                var filteredData = allData.Where(p =>
                    string.IsNullOrWhiteSpace(FilterText) ||
                    p.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

                IEnumerable<ProcessItem> sortedQuery = SortColumn switch
                {
                    "Name" => IsAscending
                        ? filteredData.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        : filteredData.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase),
                    "Memory" => IsAscending
                        ? filteredData.OrderBy(p => p.MemoryMB)
                        : filteredData.OrderByDescending(p => p.MemoryMB),
                    _ => IsAscending
                        ? filteredData.OrderBy(p => p.CpuUsage)
                        : filteredData.OrderByDescending(p => p.CpuUsage)
                };

                var sortedList = sortedQuery.ToList();

                // Sicheres Erstellen des Lookup-Dictionaries (resistent gegen evtl. Duplikate)
                var currentItems = Processes
                    .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var newNames = sortedList
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                for (int i = Processes.Count - 1; i >= 0; i--)
                {
                    if (!newNames.Contains(Processes[i].Name))
                    {
                        Processes.RemoveAt(i);
                    }
                }

                bool isUserBusy = Processes.Any(p => p.IsFlyoutOpen);

                for (int i = 0; i < sortedList.Count; i++)
                {
                    var newItem = sortedList[i];
                    if (currentItems.TryGetValue(newItem.Name, out var existingItem))
                    {
                        existingItem.CpuUsage = newItem.CpuUsage;
                        existingItem.MemoryMB = newItem.MemoryMB;
                        existingItem.Status = newItem.Status;
                        existingItem.SubProcesses = newItem.SubProcesses;

                        if (!isUserBusy)
                        {
                            int oldIndex = Processes.IndexOf(existingItem);
                            if (oldIndex != -1 && oldIndex != i)
                            {
                                Processes.Move(oldIndex, i);
                            }
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

                TotalCpu = overallCpu;
                TotalRam = overallRam;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <summary>
        /// Beendet ausgewählte Prozessgruppen sicher über den ProcessService und aktualisiert anschließend die Ansicht.
        /// </summary>
        public async Task<(bool Success, string? ErrorMessage)> KillSelectedProcessesAsync(IEnumerable<ProcessItem> selectedItems)
        {
            if (selectedItems == null) return (true, null);

            var itemsList = selectedItems.ToList();
            if (itemsList.Count == 0) return (true, null);

            var pids = itemsList
                .Where(item => !_service.IsCriticalProcess(item.Name))
                .SelectMany(item => item.SubProcesses.Select(s => s.Pid))
                .ToList();

            bool success = await Task.Run(() => _service.KillProcesses(pids, out string? error)
                ? (Result: true, Error: (string?)null)
                : (Result: false, Error: error)) is var res && res.Result;

            await RefreshDataAsync();
            return (res.Result, res.Error);
        }

        /// <summary>
        /// Hintergrund-Worker, der Prozess-Icons aus der Warteschlange extrahiert und dabei CancellationTokens beachtet.
        /// </summary>
        private async Task StartIconWorkerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_iconQueue.TryDequeue(out var item))
                    {
                        if (string.IsNullOrEmpty(item.ImagePath))
                        {
                            continue;
                        }

                        if (IconCache.TryGetIcon(item.ImagePath, out var cachedIcon))
                        {
                            _dispatcher?.TryEnqueue(() => item.IconSource = cachedIcon);
                        }
                        else if (item.IconSource == null)
                        {
                            var softwareBitmap = await Task.Run(() =>
                            {
                                try
                                {
                                    if (File.Exists(item.ImagePath))
                                    {
                                        using Icon? icon = Icon.ExtractAssociatedIcon(item.ImagePath);
                                        if (icon != null)
                                        {
                                            return IconHelper.GetSoftwareBitmapFromIcon(icon);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Icon-Extraktion fehlgeschlagen ({item.ImagePath}): {ex.Message}");
                                }
                                return null;
                            }, token);

                            if (softwareBitmap != null && _dispatcher != null && !token.IsCancellationRequested)
                            {
                                _dispatcher.TryEnqueue(async () =>
                                {
                                    try
                                    {
                                        var bitmapSource = new SoftwareBitmapSource();
                                        await bitmapSource.SetBitmapAsync(softwareBitmap);
                                        item.IconSource = bitmapSource;
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Fehler beim Setzen von SoftwareBitmapSource: {ex.Message}");
                                    }
                                    finally
                                    {
                                        softwareBitmap.Dispose();
                                    }
                                });
                            }
                            else
                            {
                                softwareBitmap?.Dispose();
                            }
                        }

                        await Task.Delay(25, token);
                    }
                    else
                    {
                        await Task.Delay(300, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Unerwarteter Fehler im IconWorker: {ex.Message}");
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _cts?.Cancel();
            _cts?.Dispose();
            _refreshLock.Dispose();
        }
    }
}