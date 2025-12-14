using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ValorantSpikeTimer
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _pollTimer;


        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }


        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            MakeClickThrough();
            StartPolling();
        }


        #region ClickThrough


        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;


        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);


        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);


        private void MakeClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int styles = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, styles | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        }


        #endregion


        #region Polling


        private void StartPolling()
        {
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };


            _pollTimer.Tick += (_, _) => Poll();
            _pollTimer.Start();
        }


        private void Poll()
        {
            // TEMP: just animate the number so you can see it working
            if (int.TryParse(TimerText.Text, out int v))
            {
                v--;
                if (v < 0) v = 45;
                TimerText.Text = v.ToString();
            }
        }


        #endregion
    }
}