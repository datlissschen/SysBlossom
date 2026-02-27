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
            // 1. Get the process data (Background Thread)
            var allData = await Task.Run(() => _service.GetProcesses());

            // 2. Filter (Background Thread)
            var filteredData = allData.Where(p =>
                string.IsNullOrEmpty(_filterText) ||
                p.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // 3. Update the List (UI Thread) - Fast & Minimal
            // We update existing items instead of Clear/Add to prevent scroll-jump
            if (Processes.Count != filteredData.Count)
            {
                Processes.Clear();
                foreach (var item in filteredData) Processes.Add(item);
            }
            else
            {
                for (int i = 0; i < filteredData.Count; i++)
                {
                    Processes[i].CpuUsage = filteredData[i].CpuUsage;
                    Processes[i].MemoryUsage = filteredData[i].MemoryUsage;
                    Processes[i].Status = filteredData[i].Status;
                }
            }

            // 4. Fire-and-forget the icon loader (Doesn't block the list)
            _ = LoadIconsInBackground(filteredData);
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
    }
}

