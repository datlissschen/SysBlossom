using Microsoft.UI;
using Microsoft.UI.Windowing;
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
using WinRT.Interop;
using TaskManager.Models;
using TaskManager.Services;
using TaskManager.Helper;

namespace TaskManager
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new();
        private DispatcherTimer _refreshTimer;
        private readonly List<double> _cpuHistory = new();
        private readonly List<double> _ramHistory = new();
        private const int MaxPoints = 50;
        public MainWindow()
        {
            this.InitializeComponent();

            SetWindowIcon("Assets/AppIcon.ico");

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(2);
            _refreshTimer.Tick += async (s, e) => await RefreshDataAsync();
            //  set DataContext for Bindings
            if (this.Content is FrameworkElement root) root.DataContext = ViewModel;

            // Initialize the ViewModel with the DispatcherQueue to allow it to update the UI from background threads.
            // This is necessary for the ViewModel to perform asynchronous operations and update the UI without blocking the main thread.
            ViewModel.Initialize(this.DispatcherQueue);

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += async (s, e) => {
                // find intern scollviewer to save the scroll position before refreshing data
                var scrollViewer = FindChild<ScrollViewer>(ProcessListView, null);
                double offset = scrollViewer?.VerticalOffset ?? 0;

                await ViewModel.RefreshDataAsync();
                UpdateUIEffects();

                if (scrollViewer != null)
                {
                    _ = DispatcherQueue.TryEnqueue(() => {
                        scrollViewer.ChangeView(null, offset, null, true);
                    });
                }
            };

            ViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(MainViewModel.TotalCpu)) UpdateMascot();
            };

            _refreshTimer.Start();
            _ = ViewModel.RefreshDataAsync();
        }

        /// <summary>
        /// Updates the visual effects and data displayed in the CPU and RAM usage graphs.
        /// </summary>
        /// <remarks>Call this method to refresh the UI elements that represent CPU and RAM usage. This
        /// ensures that the displayed information reflects the latest performance data.</remarks>
        private void UpdateUIEffects()
        {
            UpdateGraph(CpuGraphLine, _cpuHistory, ViewModel.TotalCpu, 100, CpuSparkle);
            UpdateGraph(RamGraphLine, _ramHistory, ViewModel.TotalRam, 32000, RamSparkle);
        }

        /// <summary>
        /// Updates the mascot's playback rate based on the current CPU usage.
        /// </summary>
        /// <remarks>Sets the mascot's playback rate to 2.0 if the total CPU usage exceeds 70 percent;
        /// otherwise, sets it to 1.0. This method has no effect if the mascot player is not initialized.</remarks>
        private void UpdateMascot()
        {
            if (MascotPlayer != null)
                MascotPlayer.PlaybackRate = ViewModel.TotalCpu > 70 ? 2.0 : 1.0;
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
                if (ViewModel.SortColumn == column)
                    ViewModel.IsAscending = !ViewModel.IsAscending;
                else
                {
                    ViewModel.SortColumn = column;
                    ViewModel.IsAscending = false;
                }
                await ViewModel.RefreshDataAsync();
            }
        }

        /// <summary>
        /// Handles the Click event of the Refresh button by asynchronously refreshing the data in the associated view
        /// model.
        /// </summary>
        /// <param name="sender">The source of the event, typically the Refresh button.</param>
        /// <param name="e">The event data associated with the Click event.</param>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
            => await ViewModel.RefreshDataAsync();

        /// <summary>
        /// Method for the create the context menu for the process list. 
        /// It creates a menu with a "Kill Process" option that allows the user to terminate the selected processes.
        /// </summary>
        /// <param name="sender">The source of the event, typically the Kill button.</param>
        /// <param name="e">The event data associated with the Click event.</param>
        private void KillButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProcessListView.SelectedItems.Cast<ProcessItem>().ToList();
            if (!selected.Any()) return;

            var service = new ProcessService();
            foreach (var item in selected)
            {
                var pids = item.SubProcesses.Select(s => s.Pid).ToList();
                service.KillProcesses(pids);
            }
            _ = ViewModel.RefreshDataAsync();
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
                // get visual for data object
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

        /// <summary>
        /// Handles the Opening event of a Flyout control to update the associated data context state.
        /// </summary>
        /// <remarks>This method is typically used to synchronize the UI state with the underlying data
        /// model when a Flyout is opened. It assumes that the Flyout's Target has a DataContext of type
        /// ProcessItem.</remarks>
        /// <param name="sender">The source of the event, expected to be a Flyout control whose Opening event is being handled.</param>
        /// <param name="e">The event data associated with the Opening event.</param>
        private void Flyout_Opening(object sender, object e)
        {
            if ((sender as Flyout).Target.DataContext is ProcessItem item)
            {
                item.IsFlyoutOpen = true;
            }
        }
        /// <summary>
        /// Handles the Closing event of a Flyout control to update the associated data context state.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Flyout_Closed(object sender, object e)
        {
            if ((sender as Flyout).Target.DataContext is ProcessItem item)
            {
                item.IsFlyoutOpen = false;
            }
        }

        /// <summary>
        /// Method to set the window icon using the AppWindow API. 
        /// It retrieves the window handle, gets the AppWindow instance, and sets the icon from the specified path.
        /// </summary>
        /// <param name="iconPath"></param>
        private void SetWindowIcon(string iconPath)
        {
            // Hol dir das Fenster-Handle (HWND)
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            // Icon zuweisen
            appWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, iconPath));
        }
        
        /// Method to watch over if the serachbox text changes and update the filter text in the viewmodel accordingly. 
        /// This allows the process list to be filtered based on the user's input in real-time.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.FilterText = (sender as TextBox)?.Text ?? string.Empty;
        }
    }
}

