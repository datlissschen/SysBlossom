using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Drawing;

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
        /// Method loads the data, filters it and then adds them, loading their icons if possible.
        /// </summary>
        /// <returns></returns>
        private async Task RefreshDataAsync()
        {
            //  Get the raw data on a background thread
            var allData = await Task.Run(() => _service.GetProcesses());

            // Filter it
            var filteredData = allData.Where(p =>
                string.IsNullOrEmpty(_filterText) ||
                p.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // Update the collection
            Processes.Clear();
            foreach (var item in filteredData)
            {
                Processes.Add(item);
            }

            // Load Icons 
            _ = Task.Run(async () =>
            {
                foreach (var item in filteredData)
                {
                    if (!string.IsNullOrEmpty(item.ImagePath) && item.IconSource == null)
                    {
                        // Check cache first
                        if (IconCache.TryGetIcon(item.ImagePath, out var cachedIcon))
                        {
                            // Update UI property on UI Thread
                            DispatcherQueue.TryEnqueue(() => { item.IconSource = cachedIcon; });
                        }
                        else
                        {
                            try
                            {
                                if (System.IO.File.Exists(item.ImagePath))
                                {
                                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(item.ImagePath);
                                    if (icon != null)
                                    {
                                        // Helper conversion
                                        var bitmapSource = await IconHelper.GetWinUI3BitmapSourceFromIcon(icon);
                                        IconCache.AddIcon(item.ImagePath, bitmapSource);

                                        // Update UI property on UI Thread
                                        DispatcherQueue.TryEnqueue(() => { item.IconSource = bitmapSource; });
                                    }
                                }
                            }
                            catch { /* Ignore icon errors */ }
                        }
                    }
                }
            });
        }
        private void KillButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessListView.SelectedItem is ProcessItem selected)
            {
                if (_service.KillProcess(selected.Id)) _ = RefreshDataAsync();
            }
        }
    }
}

