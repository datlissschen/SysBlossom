using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
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
    public partial class MainViewModel(ProcessService service) : INotifyPropertyChanged, IDisposable
    {
        private readonly ProcessService _service = service ?? throw new ArgumentNullException(nameof(service));
        private readonly ConcurrentQueue<ProcessItem> _iconQueue = new();
        private readonly ConcurrentDictionary<string, ImageSource> _localIconCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private CancellationTokenSource? _cts;
        private DispatcherQueue? _dispatcher;
        private bool _isDisposed;

        public MainViewModel() : this(new ProcessService())
        {
        }

        public ObservableCollection<ProcessItem> Processes { get; } = [];

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

        public void Initialize(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _ = StartIconWorkerAsync(_cts.Token);
        }

        public async Task RefreshDataAsync()
        {
            if (_isDisposed || !await _refreshLock.WaitAsync(0))
                return;

            try
            {
                var allData = await Task.Run(_service.GetProcesses);

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

                bool isUserBusy = Processes.Any(p => p.IsFlyoutOpen) || IsListFocused || HasUserSelection;

                for (int i = 0; i < sortedList.Count; i++)
                {
                    var newItem = sortedList[i];
                    if (currentItems.TryGetValue(newItem.Name, out var existingItem))
                    {
                        existingItem.CpuUsage = newItem.CpuUsage;
                        existingItem.MemoryMB = newItem.MemoryMB;
                        existingItem.Status = newItem.Status;
                        existingItem.SubProcesses = newItem.SubProcesses;

                        // If the user is navigating or a flyout is open, update values in place without reordering!
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
                        // If icon is already cached, assign it immediately before inserting!
                        if (!string.IsNullOrEmpty(newItem.ImagePath) &&
                            _localIconCache.TryGetValue(newItem.ImagePath, out var cached))
                        {
                            newItem.IconSource = cached;
                        }
                        else if (!string.IsNullOrEmpty(newItem.ImagePath))
                        {
                            _iconQueue.Enqueue(newItem);
                        }

                        Processes.Insert(i, newItem);
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

        public async Task<(bool Success, string? ErrorMessage)> KillSelectedProcessesAsync(IEnumerable<ProcessItem> selectedItems)
        {
            var itemsList = selectedItems.ToList();
            if (itemsList.Count == 0) return (true, null);

            var pids = itemsList
                .Where(item => !ProcessService.IsCriticalProcess(item.Name))
                .SelectMany(item => item.SubProcesses.Select(s => s.Pid))
                .ToList();

            var result = await Task.Run(() =>
            {
                bool ok = _service.KillProcesses(pids, out string? error);
                return (Success: ok, Error: error);
            });

            await RefreshDataAsync();
            return result;
        }

        private async Task StartIconWorkerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_iconQueue.TryDequeue(out var item))
                    {
                        string? path = item.ImagePath;
                        if (string.IsNullOrEmpty(path) || item.IconSource != null)
                        {
                            continue;
                        }

                        if (_localIconCache.TryGetValue(path, out var fastCached))
                        {
                            _dispatcher?.TryEnqueue(() => item.IconSource = fastCached);
                            continue;
                        }

                        if (IconCache.TryGetIcon(path, out var cachedIcon) && cachedIcon != null)
                        {
                            _localIconCache[path] = cachedIcon;
                            _dispatcher?.TryEnqueue(() => item.IconSource = cachedIcon);
                            continue;
                        }

                        var softwareBitmap = await Task.Run(() =>
                        {
                            try
                            {
                                if (File.Exists(path))
                                {
                                    using Icon? icon = Icon.ExtractAssociatedIcon(path);
                                    if (icon != null)
                                    {
                                        return IconHelper.GetSoftwareBitmapFromIcon(icon);
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore locked or inaccessible executables
                            }
                            return null;
                        }, token);

                        if (softwareBitmap != null && _dispatcher != null && !token.IsCancellationRequested)
                        {
                            var tcs = new TaskCompletionSource();
                            bool enqueued = _dispatcher.TryEnqueue(async () =>
                            {
                                try
                                {
                                    var bitmapSource = new SoftwareBitmapSource();
                                    await bitmapSource.SetBitmapAsync(softwareBitmap);
                                    _localIconCache[path] = bitmapSource;
                                    item.IconSource = bitmapSource;
                                }
                                catch
                                {
                                    // Ignore WinRT imaging errors
                                }
                                finally
                                {
                                    tcs.TrySetResult();
                                }
                            });

                            if (enqueued)
                            {
                                await tcs.Task;
                            }
                        }

                        await Task.Delay(15, token);
                    }
                    else
                    {
                        await Task.Delay(250, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Prevent background task from crashing the app
                }
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _cts?.Cancel();
                _cts?.Dispose();
                _refreshLock.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        private bool _isListFocused;
        public bool IsListFocused
        {
            get => _isListFocused;
            set
            {
                if (_isListFocused == value) return;
                _isListFocused = value;
                OnPropertyChanged();
            }
        }

        private bool _hasUserSelection;
        public bool HasUserSelection
        {
            get => _hasUserSelection;
            set
            {
                if (_hasUserSelection == value) return;
                _hasUserSelection = value;
                OnPropertyChanged();
            }
        }
    }
}