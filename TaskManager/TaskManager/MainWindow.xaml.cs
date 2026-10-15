using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TaskManager.Models;
using Windows.Foundation;
using WinRT.Interop;
using Path = System.IO.Path;

namespace TaskManager
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new();
        private readonly DispatcherTimer _refreshTimer;
        private readonly List<double> _cpuHistory = new();
        private readonly List<double> _ramHistory = new();
        private const int MaxPoints = 50;

        public MainWindow()
        {
            this.InitializeComponent();

            SetWindowIcon("Assets/AppIcon.ico");

            if (this.Content is FrameworkElement root)
            {
                root.DataContext = ViewModel;
            }

            ViewModel.Initialize(this.DispatcherQueue);

            // EINZIGER Timer für das Intervall-Update (Duplikat wurde entfernt)
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += OnRefreshTimerTick;

            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.TotalCpu))
                {
                    UpdateMascot();
                }
            };

            // Sauberes Aufräumen beim Schließen des Fensters
            this.Closed += OnWindowClosed;

            _refreshTimer.Start();
            _ = ViewModel.RefreshDataAsync();
        }

        private async void OnRefreshTimerTick(object? sender, object e)
        {
            var scrollViewer = FindChild<ScrollViewer>(ProcessListView);
            double offset = scrollViewer?.VerticalOffset ?? 0;

            await ViewModel.RefreshDataAsync();
            UpdateUIEffects();

            if (scrollViewer != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    scrollViewer.ChangeView(null, offset, null, true);
                });
            }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTimerTick;
            ViewModel.Dispose();
        }

        private void UpdateUIEffects()
        {
            UpdateGraph(CpuGraphLine, _cpuHistory, ViewModel.TotalCpu, 100.0, CpuSparkle);
            UpdateGraph(RamGraphLine, _ramHistory, ViewModel.TotalRam, 32768.0, RamSparkle);
        }

        private void UpdateMascot()
        {
            if (MascotPlayer != null)
            {
                MascotPlayer.PlaybackRate = ViewModel.TotalCpu > 70.0 ? 2.0 : 1.0;
            }
        }

        private async void Header_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string column })
            {
                if (ViewModel.SortColumn == column)
                {
                    ViewModel.IsAscending = !ViewModel.IsAscending;
                }
                else
                {
                    ViewModel.SortColumn = column;
                    ViewModel.IsAscending = false;
                }
                await ViewModel.RefreshDataAsync();
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.RefreshDataAsync();
            UpdateUIEffects();
        }

        private async void KillButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProcessListView.SelectedItems.OfType<ProcessItem>().ToList();
            if (selected.Count == 0) return;

            var (success, errorMessage) = await ViewModel.KillSelectedProcessesAsync(selected);
            if (!success && !string.IsNullOrEmpty(errorMessage) && this.Content is FrameworkElement root)
            {
                var dialog = new ContentDialog
                {
                    Title = "Prozess konnte nicht beendet werden",
                    Content = errorMessage,
                    CloseButtonText = "OK",
                    XamlRoot = root.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        private void UpdateGraph(Polyline? line, List<double> history, double newValue, double max, TextBlock? sparkle)
        {
            if (line == null) return;

            history.Add(newValue);
            if (history.Count > MaxPoints)
            {
                history.RemoveAt(0);
            }

            var points = new PointCollection();
            var canvas = line.Parent as Canvas;

            double width = canvas is { ActualWidth: > 0 } ? canvas.ActualWidth : 300.0;
            double height = canvas is { ActualHeight: > 0 } ? canvas.ActualHeight : (canvas?.Height > 0 ? canvas.Height : 60.0);

            double stepX = width / (MaxPoints - 1);
            double lastX = 0;
            double lastY = height;
            double safeMax = max <= 0 ? 100.0 : max;

            for (int i = 0; i < history.Count; i++)
            {
                double x = i * stepX;
                double y = height - (history[i] / safeMax * height);
                y = Math.Clamp(y, 0.0, height);

                points.Add(new Point(x, y));

                lastX = x;
                lastY = y;
            }

            line.Points = points;

            if (sparkle != null && history.Count > 0)
            {
                sparkle.Visibility = Visibility.Visible;
                Canvas.SetLeft(sparkle, lastX - 8);
                Canvas.SetTop(sparkle, lastY - 12);
            }
        }

        private async void ProcessListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var item in e.AddedItems)
            {
                if (ProcessListView.ContainerFromItem(item) is not ListViewItem container)
                    continue;

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
        /// Sucht rekursiv im visuellen Baum nach einem Kind-Element bestimmten Typs und optionalen Namens.
        /// </summary>
        public static T? FindChild<T>(DependencyObject? parent, string? childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T tChild)
                {
                    if (string.IsNullOrEmpty(childName) ||
                        (child is FrameworkElement fe && fe.Name == childName))
                    {
                        return tChild;
                    }
                }

                var result = FindChild<T>(child, childName);
                if (result != null) return result;
            }
            return null;
        }

        private void Flyout_Opening(object sender, object e)
        {
            if (sender is Flyout { Target.DataContext: ProcessItem item })
            {
                item.IsFlyoutOpen = true;
            }
        }

        private void Flyout_Closed(object sender, object e)
        {
            if (sender is Flyout { Target.DataContext: ProcessItem item })
            {
                item.IsFlyoutOpen = false;
            }
        }

        private void SetWindowIcon(string iconPath)
        {
            try
            {
                string fullPath = Path.Combine(AppContext.BaseDirectory, iconPath);
                if (!File.Exists(fullPath)) return;

                IntPtr hWnd = WindowNative.GetWindowHandle(this);
                WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
                appWindow.SetIcon(fullPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fenster-Icon konnte nicht geladen werden: {ex.Message}");
            }
        }

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.FilterText = (sender as TextBox)?.Text ?? string.Empty;
            await ViewModel.RefreshDataAsync();
        }
    }
}