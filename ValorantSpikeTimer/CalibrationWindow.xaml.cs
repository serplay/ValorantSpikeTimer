using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ValorantSpikeTimer
{
    public partial class CalibrationWindow : Window
    {
        private int _clickCount = 0;
        private Config _config = new Config();
        private Ellipse? _firstMarker;
        private Ellipse? _secondMarker;

        // For sampling pixel colors
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public Config? Result { get; private set; }

        private int[] _redSamples = new int[3];
        private int[] _greenSamples = new int[3];
        private int[] _blueSamples = new int[3];

        public CalibrationWindow()
        {
            InitializeComponent();
            
            MouseLeftButtonDown += OnMouseClick;
            KeyDown += OnKeyDown;
            Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Make window topmost and prevent activation to stay above Valorant
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    // Apply WS_EX_NOACTIVATE to prevent stealing focus
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

                    // Set as topmost window without activating
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
            }
            catch { }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelCalibration();
            }
        }

        private (int r, int g, int b) GetPixelColorAt(int x, int y)
        {
            try
            {
                IntPtr desktopHwnd = GetDesktopWindow();
                IntPtr hdc = GetDC(desktopHwnd);
                if (hdc == IntPtr.Zero)
                    return (0, 0, 0);

                uint pixel = GetPixel(hdc, x, y);
                ReleaseDC(desktopHwnd, hdc);

                int b = (int)(pixel >> 16) & 0xFF;
                int g = (int)(pixel >> 8) & 0xFF;
                int r = (int)pixel & 0xFF;

                return (r, g, b);
            }
            catch
            {
                return (0, 0, 0);
            }
        }

        private void OnMouseClick(object sender, MouseButtonEventArgs e)
        {
            Point clickPosition = e.GetPosition(this);
            int x = (int)clickPosition.X;
            int y = (int)clickPosition.Y;

            // Sample color at this position
            (int r, int g, int b) = GetPixelColorAt(x, y);
            
            _clickCount++;

            if (_clickCount == 1)
            {
                // First click - left pixel
                _config.LeftPixelX = x;
                _config.LeftPixelY = y;
                _redSamples[0] = r;
                _greenSamples[0] = g;
                _blueSamples[0] = b;

                // Draw marker
                _firstMarker = CreateMarker(x, y, Brushes.Red);
                CalibrationCanvas.Children.Add(_firstMarker);

                // Update instructions
                InstructionText.Text = "Click on the CENTER of the spike indicator";
                StatusText.Text = "Point 2 of 3";
                ColorInfoText.Text = $"Sampled RGB({r},{g},{b}) at point 1";
            }
            else if (_clickCount == 2)
            {
                // Second click - center pixel
                _config.CenterPixelX = x;
                _config.CenterPixelY = y;
                _redSamples[1] = r;
                _greenSamples[1] = g;
                _blueSamples[1] = b;

                // Draw marker
                _secondMarker = CreateMarker(x, y, Brushes.Yellow);
                CalibrationCanvas.Children.Add(_secondMarker);

                // Update instructions
                InstructionText.Text = "Click on the RIGHT edge of the spike indicator";
                StatusText.Text = "Point 3 of 3";
                ColorInfoText.Text = $"Sampled RGB({r},{g},{b}) at point 2";
            }
            else if (_clickCount == 3)
            {
                // Third click - right pixel
                _config.RightPixelX = x;
                _config.RightPixelY = y;
                _redSamples[2] = r;
                _greenSamples[2] = g;
                _blueSamples[2] = b;

                // Draw marker
                var thirdMarker = CreateMarker(x, y, Brushes.Lime);
                CalibrationCanvas.Children.Add(thirdMarker);

                // Calculate color thresholds from sampled colors
                CalculateThresholds();

                // Save and close
                _config.Save();
                Result = _config;
                
                InstructionText.Text = "Calibration complete!";
                StatusText.Text = $"Thresholds: R>{_config.RedMin}, G<{_config.GreenMax}, B<{_config.BlueMax}";
                StatusText.Foreground = Brushes.LimeGreen;
                ColorInfoText.Text = $"Sampled RGB({r},{g},{b}) at point 3";

                // Close after a brief delay
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    DialogResult = true;
                    Close();
                };
                timer.Start();
            }
        }

        private Ellipse CreateMarker(int x, int y, Brush color)
        {
            var marker = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = color,
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            System.Windows.Controls.Canvas.SetLeft(marker, x - 6);
            System.Windows.Controls.Canvas.SetTop(marker, y - 6);
            return marker;
        }

        private void CalculateThresholds()
        {
            // Calculate thresholds based on sampled colors with tolerance margin
            
            // Red minimum: Use the lowest red value minus 30 as minimum threshold
            int minRed = Math.Min(_redSamples[0], Math.Min(_redSamples[1], _redSamples[2]));
            _config.RedMin = Math.Max(80, minRed - 30); // Don't go below 80

            // Green maximum: Use the highest green value plus 15 as maximum threshold
            int maxGreen = Math.Max(_greenSamples[0], Math.Max(_greenSamples[1], _greenSamples[2]));
            _config.GreenMax = Math.Min(50, maxGreen + 15); // Don't go above 50

            // Blue maximum: Use the highest blue value plus 15 as maximum threshold
            int maxBlue = Math.Max(_blueSamples[0], Math.Max(_blueSamples[1], _blueSamples[2]));
            _config.BlueMax = Math.Min(50, maxBlue + 15); // Don't go above 50
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelCalibration();
        }

        private void CancelCalibration()
        {
            Result = null;
            DialogResult = false;
            Close();
        }
    }
}
