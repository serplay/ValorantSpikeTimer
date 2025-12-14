using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ValorantSpikeTimer
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _pollTimer;
        private DispatcherTimer _detectionTimer;
        private DispatcherTimer _verificationTimer;
        private Stopwatch _stopwatch;

        private double _totalMilliseconds = 45000;
        private IntPtr _valorantHandle = IntPtr.Zero;
        private bool _valorantVisible = false;
        private Config? _config;
        private bool _cooldownActive = false;
        private DateTime _cooldownEndTime;
        private bool _timerRunning = false;

        private double _scaleFactor;

        // ===== Overlay baseline (for timer positioning) =====
        private const double BASELINE_SCREEN_HEIGHT = 1440;
        private const double BASELINE_OVERLAY_HEIGHT = 80;
        private const double BASELINE_OVERLAY_WIDTH = 300;
        private const double BASELINE_FONT_SIZE = 24;
        private const double BASELINE_TOP_OFFSET = 115;

        // ===== Win32 constants =====
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        // ===== Win32 imports =====
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_UP = 0x26;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            KeyDown += OnKeyDown;
            SourceInitialized += OnSourceInitialized;
        }

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Up && 
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                ShowCalibration();
                e.Handled = true;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _config = Config.Load();
                if (_config == null || !_config.IsValid())
                {
                    ShowCalibration();
                    
                    if (_config == null || !_config.IsValid())
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                }

                CalculateScaleFactor();
                ScaleOverlay();
                PositionOverlayCenter();

                MakeClickThrough();
                SetWindowForFullscreen();

                Visibility = Visibility.Hidden;

                StartDetectionTimer();
                InitializePolling();
                InitializeVerificationTimer();
            }
            catch { }
        }

        private void ShowCalibration()
        {
            try
            {
                var calibrationWindow = new CalibrationWindow();
                Hide();
                
                bool? result = calibrationWindow.ShowDialog();
                
                if (result == true && calibrationWindow.Result != null)
                {
                    _config = calibrationWindow.Result;
                    Show();
                    
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!_timerRunning && IsSpikeIndicatorVisible())
                            {
                                if (_pollTimer != null && !_pollTimer.IsEnabled)
                                {
                                    StartPolling();
                                }
                            }
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    Show();
                }
            }
            catch
            {
                Show();
            }
        }

        // ===========================
        // Scaling & Positioning
        // ===========================
        private void CalculateScaleFactor()
        {
            _scaleFactor = SystemParameters.PrimaryScreenHeight / BASELINE_SCREEN_HEIGHT;
        }

        private void ScaleOverlay()
        {
            Width = BASELINE_OVERLAY_WIDTH * _scaleFactor;
            Height = BASELINE_OVERLAY_HEIGHT * _scaleFactor;
            TimerText.FontSize = BASELINE_FONT_SIZE * _scaleFactor;
        }

        private void PositionOverlayCenter()
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = BASELINE_TOP_OFFSET * _scaleFactor;
        }

        // ===========================
        // Click-through overlay
        // ===========================
        private void MakeClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            int newStyle = style | WS_EX_LAYERED | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, newStyle);
        }

        // ===========================
        // Fullscreen support
        // ===========================
        private void SetWindowForFullscreen()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            int newExStyle = exStyle | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, newExStyle);
            
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // ===========================
        // Valorant detection
        // ===========================
        private void StartDetectionTimer()
        {
            _detectionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            _detectionTimer.Tick += (_, _) => CheckValorantState();
            _detectionTimer.Start();
        }

        private void CheckValorantState()
        {
            IntPtr handle = FindValorantWindow();

            if (handle != IntPtr.Zero)
            {
                _valorantHandle = handle;

                if (!_valorantVisible)
                {
                    _valorantVisible = true;
                    Visibility = Visibility.Visible;
                }
            }
            else
            {
                _valorantHandle = IntPtr.Zero;

                if (_valorantVisible)
                {
                    _valorantVisible = false;
                    Visibility = Visibility.Hidden;
                    
                    if (_pollTimer != null && _pollTimer.IsEnabled)
                    {
                        _pollTimer.Stop();
                    }
                    
                    if (_verificationTimer != null && _verificationTimer.IsEnabled)
                    {
                        _verificationTimer.Stop();
                    }
                }
            }
        }

        private IntPtr FindValorantWindow()
        {
            IntPtr found = IntPtr.Zero;

            try
            {
                var valorantProcesses = Process.GetProcessesByName("VALORANT-Win64-Shipping");
                
                if (valorantProcesses.Length == 0)
                    return IntPtr.Zero;

                IntPtr foregroundWindow = GetForegroundWindow();

                foreach (var proc in valorantProcesses)
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        bool isVisible = IsWindowVisible(proc.MainWindowHandle);
                        bool isMinimized = IsIconic(proc.MainWindowHandle);
                        bool isFocused = proc.MainWindowHandle == foregroundWindow;
                        
                        if (isVisible && !isMinimized && isFocused)
                        {
                            if (_timerRunning)
                            {
                                return proc.MainWindowHandle;
                            }

                            if (_cooldownActive)
                            {
                                if (DateTime.Now < _cooldownEndTime)
                                {
                                    found = proc.MainWindowHandle;
                                    break;
                                }
                                else
                                {
                                    _cooldownActive = false;
                                }
                            }

                            bool spikeVisible = IsSpikeIndicatorVisible();
                            
                            if (spikeVisible)
                            {
                                found = proc.MainWindowHandle;
                                
                                if (_pollTimer != null && !_pollTimer.IsEnabled)
                                {
                                    StartPolling();
                                }
                                
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            return found;
        }

        private bool IsSpikeIndicatorVisible()
        {
            try
            {
                if (_config == null || !_config.IsValid())
                    return false;

                int leftX = _config.LeftPixelX;
                int leftY = _config.LeftPixelY;
                int centerX = _config.CenterPixelX;
                int centerY = _config.CenterPixelY;
                int rightX = _config.RightPixelX;
                int rightY = _config.RightPixelY;

                IntPtr desktopHwnd = GetDesktopWindow();
                IntPtr hdc = GetDC(desktopHwnd);
                if (hdc == IntPtr.Zero)
                    return false;

                try
                {
                    uint pixel1 = GetPixel(hdc, leftX, leftY);
                    uint pixel2 = GetPixel(hdc, centerX, centerY);
                    uint pixel3 = GetPixel(hdc, rightX, rightY);

                    int r1 = (int)pixel1 & 0xFF;
                    int g1 = (int)(pixel1 >> 8) & 0xFF;
                    int b1 = (int)(pixel1 >> 16) & 0xFF;

                    int r2 = (int)pixel2 & 0xFF;
                    int g2 = (int)(pixel2 >> 8) & 0xFF;
                    int b2 = (int)(pixel2 >> 16) & 0xFF;

                    int r3 = (int)pixel3 & 0xFF;
                    int g3 = (int)(pixel3 >> 8) & 0xFF;
                    int b3 = (int)(pixel3 >> 16) & 0xFF;

                    int matchCount = 0;

                    if ((r1 > _config.RedMin && g1 < _config.GreenMax && b1 < _config.BlueMax) ||
                        (r1 > 100 && r1 > 2 * (g1 + b1)))
                    {
                        matchCount++;
                    }

                    if ((r2 > _config.RedMin && g2 < _config.GreenMax && b2 < _config.BlueMax) ||
                        (r2 > 100 && r2 > 2 * (g2 + b2)))
                    {
                        matchCount++;
                        if (matchCount >= 2) return true;
                    }

                    if ((r3 > _config.RedMin && g3 < _config.GreenMax && b3 < _config.BlueMax) ||
                        (r3 > 100 && r3 > 2 * (g3 + b3)))
                    {
                        matchCount++;
                    }

                    return matchCount >= 2;
                }
                finally
                {
                    ReleaseDC(desktopHwnd, hdc);
                }
            }
            catch
            {
                return false;
            }
        }

        // ===========================
        // Timer logic
        // ===========================
        private void InitializePolling()
        {
            _stopwatch = Stopwatch.StartNew();

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };

            _pollTimer.Tick += (_, _) => Poll();
        }

        private void InitializeVerificationTimer()
        {
            _verificationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };

            _verificationTimer.Tick += (_, _) => VerifySpikeStillPlanted();
        }

        private void VerifySpikeStillPlanted()
        {
            if (!_timerRunning)
                return;

            if (!IsSpikeIndicatorVisible())
            {
                StopTimerDefused();
            }
        }

        private void StopTimerDefused()
        {
            _pollTimer?.Stop();
            _verificationTimer?.Stop();
            _stopwatch.Stop();
            _totalMilliseconds = 45000;
            TimerText.Visibility = Visibility.Hidden;
            
            _timerRunning = false;
            _cooldownActive = true;
            _cooldownEndTime = DateTime.Now.AddSeconds(10);
        }

        private void StartPolling()
        {
            if (_pollTimer == null)
            {
                InitializePolling();
            }
            
            if (_verificationTimer == null)
            {
                InitializeVerificationTimer();
            }
            
            _stopwatch.Restart();
            _totalMilliseconds = 45000;
            
            TimerText.Visibility = Visibility.Visible;
            
            _cooldownActive = false;
            _timerRunning = true;
            
            _pollTimer.Start();
            _verificationTimer.Start();
        }

        private void Poll()
        {
            _totalMilliseconds = 45000 - _stopwatch.Elapsed.TotalMilliseconds;

            if (_totalMilliseconds <= 0)
            {
                _pollTimer?.Stop();
                _verificationTimer?.Stop();
                _stopwatch.Restart();
                _totalMilliseconds = 45000;
                TimerText.Visibility = Visibility.Hidden;
                
                _timerRunning = false;
                _cooldownActive = true;
                _cooldownEndTime = DateTime.Now.AddSeconds(10);
                return;
            }

            int seconds = (int)(_totalMilliseconds / 1000);
            int centiseconds = (int)((_totalMilliseconds % 1000) / 10);

            if (_totalMilliseconds <= 10000)
            {
                TimerText.Foreground = Brushes.Red;
                TimerText.Text = $"{seconds}:{centiseconds:D2}";
            }
            else if (_totalMilliseconds <= 20000)
            {
                TimerText.Foreground = Brushes.Yellow;
                TimerText.Text = $"0:{seconds}";
            }
            else
            {
                TimerText.Foreground = Brushes.LimeGreen;
                TimerText.Text = $"0:{seconds}";
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    HwndSource source = HwndSource.FromHwnd(helper.Handle);
                    source.AddHook(WndProc);
                    RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_UP);
                }
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Dispatcher.BeginInvoke(new Action(() => ShowCalibration()));
                handled = true;
            }

            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    UnregisterHotKey(helper.Handle, HOTKEY_ID);
                }
            }
            catch { }

            base.OnClosed(e);
        }
    }
}
