using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace TaskManager
{
    public sealed partial class MainWindow : Window
    {
        private string _sortColumn = "CPU"; // Default sort
        private bool _isAscending = false;   // Default to highest CPU first
        private DispatcherTimer _refreshTimer;
        private readonly ProcessService _service = new();
        public ObservableCollection<ProcessItem> Processes { get; } = new();
        private string _filterText = string.Empty; 

        public MainWindow()
        {
            this.InitializeComponent();

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(2);
            _refreshTimer.Tick += async (s, e) => await RefreshDataAsync();
            _refreshTimer.Start();

            _ = RefreshDataAsync(); 
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filterText = (sender as TextBox)?.Text ?? string.Empty;
            await RefreshDataAsync();
        }

        /// <summary>
        /// Asynchronously refreshes the list of processes based on the current filter criteria.
        /// </summary>
        /// <remarks>This method retrieves process data in a background thread, filters it according to
        /// the specified filter text, and updates the UI list of processes with minimal disruption. Existing items are
        /// updated to preserve UI state, such as scroll position. Icons for the filtered processes are loaded in the
        /// background to avoid blocking the UI.</remarks>
        /// <returns>A task that represents the asynchronous refresh operation.</returns>
        private async Task RefreshDataAsync()
        {
            // 1. Fetch and sort data in the background
            var allData = await Task.Run(() => _service.GetProcesses());

            // Filter and Sort 
            var filteredData = allData.Where(p =>
                string.IsNullOrEmpty(_filterText) ||
                p.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

            // Apply Sorting 
            var sortedList = filteredData.OrderByDescending(p => p.CpuUsage).ToList();

            // 2. SYNC LOGIC 

            // Create a dictionary of current items for fast lookup
            var currentItems = Processes.ToDictionary(p => p.Id);
            var newIds = sortedList.Select(p => p.Id).ToHashSet();

            // A. Remove processes that are no longer running
            for (int i = Processes.Count - 1; i >= 0; i--)
            {
                if (!newIds.Contains(Processes[i].Id))
                {
                    Processes.RemoveAt(i);
                }
            }

            // B. Update existing items or Add new ones
            for (int i = 0; i < sortedList.Count; i++)
            {
                var newItem = sortedList[i];

                if (currentItems.TryGetValue(newItem.Id, out var existingItem))
                {
                    // Update the data (INotifyPropertyChanged handles the UI refresh)
                    existingItem.CpuUsage = newItem.CpuUsage;
                    existingItem.MemoryUsage = newItem.MemoryUsage;
                    existingItem.Status = newItem.Status;

                    // Handle sorting: If the item moved position, move it in the collection
                    int oldIndex = Processes.IndexOf(existingItem);
                    if (oldIndex != i)
                    {
                        Processes.Move(oldIndex, i);
                    }
                }
                else
                {
                    // Item is brand new, insert it at the correct sorted position
                    Processes.Insert(i, newItem);
                }
            }

            // 3. Kick off icon loading for new items only
            _ = LoadIconsInBackground(sortedList);
        }

        /// <summary>
        /// Helper to turn "150 MB" into 150 for sorting
        /// </summary>
        /// <param name="memString"></param>
        /// <returns></returns>
        private long ParseMemory(string memString)
        {
            if (long.TryParse(System.Text.RegularExpressions.Regex.Match(memString, @"\d+").Value, out long result))
                return result;
            return 0;
        }
        private async Task LoadIconsInBackground(List<ProcessItem> items)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.ImagePath) || item.IconSource != null) continue;

                // Check Cache first (Instant)
                if (IconCache.TryGetIcon(item.ImagePath, out var cachedIcon))
                {
                    item.IconSource = cachedIcon;
                    continue;
                }

                // Only do the heavy lifting for a few icons per second to prevent "choke"
                await Task.Run(async () =>
                {
                    try
                    {
                        if (System.IO.File.Exists(item.ImagePath))
                        {
                            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(item.ImagePath);
                            if (icon != null)
                            {
                                var softwareBitmap = IconHelper.GetSoftwareBitmapFromIcon(icon);

                                // Hand back to UI thread ONLY for the final display step
                                DispatcherQueue.TryEnqueue(async () =>
                                {
                                    var bitmapSource = new Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource();
                                    await bitmapSource.SetBitmapAsync(softwareBitmap);
                                    IconCache.AddIcon(item.ImagePath!, bitmapSource);
                                    item.IconSource = bitmapSource;
                                });
                            }
                        }
                    }
                    catch { /* Skip stubborn files */ }
                });
            }
        }

        /// <summary>
        /// Handles the click event for the Kill button, terminating the selected process from the list.
        /// </summary>
        /// <remarks>If a process is selected in the ProcessListView, it attempts to kill the process
        /// using its ID. The method refreshes the data asynchronously after the process is killed.</remarks>
        /// <param name="sender">The source of the event, typically the Kill button that was clicked.</param>
        /// <param name="e">The event data associated with the click event, providing additional information about the event.</param>
        private void KillButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessListView.SelectedItem is ProcessItem selected)
            {
                if (_service.KillProcess(selected.Id)) _ = RefreshDataAsync();
            }
        }

        /// <summary>
        /// Click handler for the columns.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Header_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string column)
            {
                // If clicking the same column, flip the direction. Otherwise, set to Descending.
                if (_sortColumn == column)
                {
                    _isAscending = !_isAscending;
                }
                else
                {
                    _sortColumn = column;
                    _isAscending = false;
                }

                await RefreshDataAsync();
            }
        }
    }
}

