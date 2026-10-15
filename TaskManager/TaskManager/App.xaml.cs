using Microsoft.UI.Xaml;

namespace TaskManager
{
    /// <summary>
    /// Stellt anwendungsspezifisches Verhalten bereit, um die Standard-Application-Klasse zu ergänzen.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}