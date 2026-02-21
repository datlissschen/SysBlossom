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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TaskManager
{
    /// <summary>
    /// Main window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private ProcessService _service = new ProcessService();
        public ObservableCollection<ProcessItem> Processes { get; set; } = new();

        public MainWindow()
        {
            this.InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            Processes.Clear();
            var data = _service.GetProcesses();
            foreach (var item in data) Processes.Add(item);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadData();

        private void KillButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessListView.SelectedItem is ProcessItem selected)
            {
                _service.KillProcess(selected.Id);
                LoadData(); // Refresh list after killing
            }
        }
    }
}


