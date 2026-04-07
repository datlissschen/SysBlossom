using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
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
        // queue for icons
        private readonly ConcurrentQueue<ProcessItem> _iconQueue = new();


        private readonly List<double> _cpuHistory = new();
        private readonly List<double> _ramHistory = new();
        private const int MaxPoints = 50; // amount of points to show in the graph

        public MainWindow()
        {
            this.InitializeComponent();

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(2);
            _refreshTimer.Tick += async (s, e) => await RefreshDataAsync();

            // START ONE THROTTLED ICON WORKER
            _ = StartIconWorker();

            _refreshTimer.Start();
            _ = RefreshDataAsync();
        }

        /// <summary>
        /// Worker method for the icons.
        /// </summary>
        /// <returns></returns>
        private async Task StartIconWorker()
        {
            while (true)
            {
                if (_iconQueue.TryDequeue(out var item))
                {
                    // load only if no Icon in Cache
                    if (item.IconSource == null && !string.IsNullOrEmpty(item.ImagePath))
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

                                        // assign UI thread
                                        DispatcherQueue.TryEnqueue(async () =>
                                        {
                                            var bitmapSource = new SoftwareBitmapSource();
                                            await bitmapSource.SetBitmapAsync(softwareBitmap);
                                            item.IconSource = bitmapSource;
                                        });
                                    }
                                }
                            }
                            catch { /* ignore catc */ }
                        });
                    }
                    await Task.Delay(30); 
                }
                else
                {
                    await Task.Delay(500); 
                }
            }
        }

        /// <summary>
        /// Logic for the refresh button. It shows a loading animation while refreshing the data asynchronously. 
        /// The actual data refresh is done in the RefreshDataAsync method, which is called when the button is clicked and also periodically by the timer. 
        /// The loading animation is displayed until the data refresh is complete, providing visual feedback to the user that the refresh operation is in progress.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshLoadingPlayer.Visibility = Visibility.Visible;
            _ = RefreshLoadingPlayer.PlayAsync(0, 1, true);

            await RefreshDataAsync();

            RefreshLoadingPlayer.Stop();
            RefreshLoadingPlayer.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Method to check whether the text in the search box has changed and to trigger the refresh of the data accordingly.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
            var allData = await Task.Run(() => _service.GetProcesses());

            // Filter
            var filteredData = allData.Where(p =>
                string.IsNullOrEmpty(_filterText) ||
                p.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

            // APPLY DYNAMIC SORTING
            IEnumerable<ProcessItem> sortedQuery;
            switch (_sortColumn)
            {
                case "Name":
                    sortedQuery = _isAscending ? filteredData.OrderBy(p => p.Name) : filteredData.OrderByDescending(p => p.Name);
                    break;
                case "Memory":
                    sortedQuery = _isAscending ? filteredData.OrderBy(p => ParseMemory(p.MemoryUsage)) : filteredData.OrderByDescending(p => ParseMemory(p.MemoryUsage));
                    break;
                case "Status":
                    sortedQuery = _isAscending ? filteredData.OrderBy(p => p.Status) : filteredData.OrderByDescending(p => p.Status);
                    break;
                default: // CPU
                    sortedQuery = _isAscending ? filteredData.OrderBy(p => p.CpuUsage) : filteredData.OrderByDescending(p => p.CpuUsage);
                    break;
            }
            var sortedList = sortedQuery.ToList();

            // SAVE SELECTION
            var selectedIds = ProcessListView.SelectedItems
                .Cast<ProcessItem>()
                .Select(p => p.Id)
                .ToList();

            // YNC LOGIC (In-place update)
            var currentItems = Processes.ToDictionary(p => p.Id);
            var newIds = sortedList.Select(p => p.Id).ToHashSet();

            // Remove old
            for (int i = Processes.Count - 1; i >= 0; i--)
            {
                if (!newIds.Contains(Processes[i].Id)) Processes.RemoveAt(i);
            }

            // Update/Move/Add
            for (int i = 0; i < sortedList.Count; i++)
            {
                var newItem = sortedList[i];
                if (currentItems.TryGetValue(newItem.Id, out var existingItem))
                {
                    existingItem.CpuUsage = newItem.CpuUsage;
                    existingItem.MemoryUsage = newItem.MemoryUsage;
                    existingItem.Status = newItem.Status;

                    int oldIndex = Processes.IndexOf(existingItem);
                    if (oldIndex != i) Processes.Move(oldIndex, i);
                }
                else
                {
                    Processes.Insert(i, newItem);

                    // Only queue the icon if it's not already cached
                    if (!string.IsNullOrEmpty(newItem.ImagePath) && !IconCache.TryGetIcon(newItem.ImagePath, out _))
                    {
                        _iconQueue.Enqueue(newItem);
                    }
                }
            }

            // clear and re-add only if the selection actually changed or items moved
            ProcessListView.SelectedItems.Clear();
            foreach (var id in selectedIds)
            {
                var itemToReselect = Processes.FirstOrDefault(p => p.Id == id);
                if (itemToReselect != null)
                {
                    ProcessListView.SelectedItems.Add(itemToReselect);
                }
            }
            // get data
            double totalCpu = sortedList.Sum(p => p.CpuUsage);
            double totalRam = sortedList.Sum(p => ParseMemory(p.MemoryUsage));

            if (totalCpu > 70)
            {
                // Make the mascot look "panicked" or energetic
                MascotPlayer.PlaybackRate = 2.0;
                if (MascotSource.UriSource.ToString().Contains("Happy"))
                {
                    MascotSource.UriSource = new Uri("ms-appx:///Assets/Lottie/Mascot_Stress.json");
                    _ = MascotPlayer.PlayAsync(0, 1, true);
                }
            }
            else
            {
                MascotPlayer.PlaybackRate = 1.0;
                if (MascotSource.UriSource.ToString().Contains("Stress"))
                {
                    MascotSource.UriSource = new Uri("ms-appx:///Assets/Lottie/Mascot_Happy.json");
                    _ = MascotPlayer.PlayAsync(0, 1, true);
                }
            }

            // update graph on UI Thread
            UpdateGraph(CpuGraphLine, _cpuHistory, totalCpu, 100, CpuSparkle);
            UpdateGraph(RamGraphLine, _ramHistory, totalRam, 32000, RamSparkle);

        }

        /// <summary>
        /// helper for drawing the graph. It takes the new value, adds it to the history, and updates the Polyline points accordingly.
        /// </summary>
        /// <param name="line"></param>
        /// <param name="history"></param>
        /// <param name="newValue"></param>
        /// <param name="max"></param>
        private void UpdateGraph(Polyline line, List<double> history, double newValue, double max, TextBlock sparkle)
        {
            history.Add(newValue);
            if (history.Count > MaxPoints) history.RemoveAt(0);

            var points = new PointCollection();

            // Dynamical width
            var canvas = line.Parent as Canvas;
            double width = canvas.ActualWidth;

            if (width <= 0) width = 300;

            double height = canvas.Height;

            double stepX = width / (MaxPoints - 1);

            double lastX = 0;
            double lastY = height;

            for (int i = 0; i < history.Count; i++)
            {
                double x = i * stepX;
                double y = height - (history[i] / max * height);
                y = Math.Clamp(y, 0, height);

                points.Add(new Windows.Foundation.Point(x, y));

                lastX = x;
                lastY = y;
            }

            line.Points = points;

            // Sparkle position
            if (history.Count > 0)
            {
                sparkle.Visibility = Visibility.Visible;
                Canvas.SetLeft(sparkle, lastX - 8);
                Canvas.SetTop(sparkle, lastY - 12);
            }
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

        /// <summary>
        /// Handles the click event for the Kill button, terminating the selected process from the list.
        /// </summary>
        /// <remarks>If a process is selected in the ProcessListView, it attempts to kill the process
        /// using its ID. The method refreshes the data asynchronously after the process is killed.</remarks>
        /// <param name="sender">The source of the event, typically the Kill button that was clicked.</param>
        /// <param name="e">The event data associated with the click event, providing additional information about the event.</param>
        private void KillButton_Click(object sender, RoutedEventArgs e)
        {
            // Get all selected items
            var selectedProcesses = ProcessListView.SelectedItems.Cast<ProcessItem>().ToList();

            if (selectedProcesses.Count == 0) return;

            // loop and kill
            bool atLeastOneKilled = false;

            foreach (var process in selectedProcesses)
            {
                if (_service.KillProcess(process.Id))
                {
                    atLeastOneKilled = true;
                }
            }

            // Refresh the list if we actually closed something
            if (atLeastOneKilled)
            {
                _ = RefreshDataAsync();
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

        /// <summary>
        /// Handles the SelectionChanged event for the process list view and plays an animation for each newly selected
        /// item.
        /// </summary>
        /// <remarks>The animation is only played for items that have just been selected. The method is
        /// asynchronous and will not block the UI thread while animations are playing.</remarks>
        /// <param name="sender">The source of the event, typically the process list view control.</param>
        /// <param name="e">An object that contains event data, including the items that were added to or removed from the selection.</param>
        private async void ProcessListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var item in e.AddedItems)
            {
                //get visual for data object
                var container = ProcessListView.ContainerFromItem(item) as ListViewItem;
                if (container == null) continue;

                // search heartburstplayer
                var heartPlayer = FindChild<AnimatedVisualPlayer>(container, "HeartBurstPlayer");

                if (heartPlayer != null)
                {
                    heartPlayer.Visibility = Visibility.Visible;
                    await heartPlayer.PlayAsync(0, 1, false);
                    heartPlayer.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// helper to find child control by name and type. It recursively searches the visual tree starting from the specified parent object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="parent"></param>
        /// <param name="childName"></param>
        /// <returns></returns>
        public static T FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T tChild && (child as FrameworkElement).Name == childName) return tChild;

                var result = FindChild<T>(child, childName);
                if (result != null) return result;
            }
            return null;
        }
    }
}

