using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace TaskManager
{
    /// <summary>
    /// class represents a single entry in the taskmanager table.
    /// </summary>
    public class ProcessItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double CpuUsage { get; set; }
        public string CpuUsageDisplay => $"{CpuUsage:F1}%";
        public string MemoryUsage { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public string? ImagePath { get; set; }

        private ImageSource? _iconSource;
        public ImageSource? IconSource
        {
            get => _iconSource;
            set
            {
                _iconSource = value;
                OnPropertyChanged(); // Tells the UI to update the Image
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}