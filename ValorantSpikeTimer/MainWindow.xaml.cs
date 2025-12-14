using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ValorantSpikeTimer
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _pollTimer;
        private double _totalMilliseconds = 45000; // 45 seconds in milliseconds
        private Stopwatch _stopwatch;


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
            _stopwatch = Stopwatch.StartNew();
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };


            _pollTimer.Tick += (_, _) => Poll();
            _pollTimer.Start();
        }


        private void Poll()
        {
            // Use actual elapsed time instead of counting fixed deltas
            _totalMilliseconds = 45000 - _stopwatch.Elapsed.TotalMilliseconds;
            if (_totalMilliseconds < 0) 
            {
                _stopwatch.Restart();
                _totalMilliseconds = 45000;
            }

            int minutes = (int)(_totalMilliseconds / 60000);
            int seconds = (int)((_totalMilliseconds % 60000) / 1000);
            int milliseconds = (int)(_totalMilliseconds % 1000) / 10;

            TimerText.Text = $"{minutes}:{seconds:D2}";

            if (_totalMilliseconds < 8000)
            {
                TimerText.Foreground = new SolidColorBrush(Colors.Red);
                TimerText.Text = $"{seconds:D2}:{milliseconds:D2}";
            }
            else if (_totalMilliseconds < 20000)
            {
                TimerText.Foreground = new SolidColorBrush(Colors.Yellow);
            }
            else
            {
                TimerText.Foreground = new SolidColorBrush(Colors.LimeGreen);
            }
        }


        #endregion
    }
}