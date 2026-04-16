using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TaskManager.Helper;
using TaskManager.Services;
using TaskManager.Models;

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

        // private fields for properties that require change notification
        private double _cpuUsage;
        private bool _isFlyoutOpen;
        private ImageSource? _iconSource;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Method to trigger the PropertyChanged event, which notifies the UI that a property value has changed and the display should be updated accordingly.
        /// </summary>
        /// <param name="name">The name of the property that changed.</param>
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

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

        /// <summary>
        /// Method to get or set the icon source for the process. 
        /// It uses the ImageSource type, which is compatible with WinUI, to represent the icon associated with the process.
        /// </summary>
        public ImageSource? IconSource
        {
            get => _iconSource;
            set { _iconSource = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the flyout is currently open.
        /// </summary>
        public bool IsFlyoutOpen
        {
            get => _isFlyoutOpen;
            set { _isFlyoutOpen = value; OnPropertyChanged(); }
        }
    }
}