using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskManager
{
    /// <summary>
    /// class represents a single entry in the taskmanager table.
    /// </summary>
    public class ProcessItem : INotifyPropertyChanged
    {
        public List<SubProcessInfo> SubProcesses { get; set; } = new();
        public int PidCount => SubProcesses?.Count ?? 0;
        public string Name { get; set; } = string.Empty;
        public string MemoryUsage { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public string? ImagePath { get; set; }
        public string CpuUsageDisplay => $"{CpuUsage:F1}%";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private double _cpuUsage;

        /// <summary>
        /// method triggers the change of the CPU usage and notifies the UI to update the display.
        /// </summary>
        public double CpuUsage
        {
            get => _cpuUsage;
            set
            {
                // Only notify if the change is significant (> 0.1%)
                if (Math.Abs(_cpuUsage - value) < 0.1) return;
                _cpuUsage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuUsageDisplay));
            }
        }

        private ImageSource? _iconSource;
        public ImageSource? IconSource
        {
            get => _iconSource;
            set { _iconSource = value; OnPropertyChanged(); }
        }

        private bool _isFlyoutOpen;
        public bool IsFlyoutOpen
        {
            get => _isFlyoutOpen;
            set { _isFlyoutOpen = value; OnPropertyChanged(); }
        }
    }
}