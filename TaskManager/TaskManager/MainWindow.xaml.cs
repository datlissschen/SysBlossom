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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TaskManager
{
    /// <summary>
    /// Main window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private DispatcherTimer _refreshTimer;
        private readonly ProcessService _service = new();
        public ObservableCollection<ProcessItem> Processes { get; } = new();

        public MainWindow()
        {
            this.InitializeComponent();
            _ = RefreshDataAsync(); // Initial load
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(2);
            _refreshTimer.Tick += async (s, e) => await RefreshDataAsync();
            _refreshTimer.Start();

            _ = RefreshDataAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDataAsync();
        }

        private async Task RefreshDataAsync()
        {
            var data = await Task.Run(() => _service.GetProcesses());

            int? selectedId = (ProcessListView.SelectedItem as ProcessItem)?.Id;

            Processes.Clear();
            foreach (var item in data)
            {
                Processes.Add(item);
            }

            if (selectedId.HasValue)
            {
                var itemToSelect = Processes.FirstOrDefault(p => p.Id == selectedId.Value);
                if (itemToSelect != null)
                {
                    ProcessListView.SelectedItem = itemToSelect;
                }
            }
        }



        private void KillButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessListView.SelectedItem is ProcessItem selected)
            {
                if (_service.KillProcess(selected.Id))
                {
                    _ = RefreshDataAsync(); // Refresh after success
                }
            }
        }
    }
}


