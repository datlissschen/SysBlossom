using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskManager.Models
{
    /// <summary>
    /// Represents a single grouped entry in the task manager table.
    /// Marked partial for CsWinRT AOT / ABI source generation.
    /// </summary>
    public partial class ProcessItem : INotifyPropertyChanged
    {
        private List<SubProcessInfo> _subProcesses = [];
        private double _cpuUsage;
        private double _memoryMB;
        private string _status = "Active";
        private bool _isFlyoutOpen;
        private ImageSource? _iconSource;

        public string Name { get; set; } = string.Empty;
        public string? ImagePath { get; set; }

        public List<SubProcessInfo> SubProcesses
        {
            get => _subProcesses;
            set
            {
                _subProcesses = value ?? [];
                OnPropertyChanged();
                OnPropertyChanged(nameof(PidCount));
            }
        }

        public int PidCount => _subProcesses.Count;

        public double CpuUsage
        {
            get => _cpuUsage;
            set
            {
                if (Math.Abs(_cpuUsage - value) < 0.1) return;
                _cpuUsage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuUsageDisplay));
            }
        }

        public string CpuUsageDisplay => $"{CpuUsage:F1}%";

        public double MemoryMB
        {
            get => _memoryMB;
            set
            {
                if (Math.Abs(_memoryMB - value) < 0.1) return;
                _memoryMB = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MemoryUsage));
            }
        }

        public string MemoryUsage => $"{MemoryMB:F0} MB";

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
            }
        }

        public ImageSource? IconSource
        {
            get => _iconSource;
            set
            {
                if (_iconSource == value) return;
                _iconSource = value;
                OnPropertyChanged();
            }
        }

        public bool IsFlyoutOpen
        {
            get => _isFlyoutOpen;
            set
            {
                if (_isFlyoutOpen == value) return;
                _isFlyoutOpen = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}