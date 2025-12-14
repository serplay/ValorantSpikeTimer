using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ValorantSpikeTimer
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _pollTimer;
        private DispatcherTimer _detectionTimer;
        private Stopwatch _stopwatch;

        private double _totalMilliseconds = 45000;
        private IntPtr _valorantHandle = IntPtr.Zero;
        private bool _valorantVisible = false;

        // ===== Scaling baseline =====
        private const double BASELINE_SCREEN_HEIGHT = 1440;
        private const double BASELINE_OVERLAY_HEIGHT = 80;
        private const double BASELINE_OVERLAY_WIDTH = 300;
        private const double BASELINE_FONT_SIZE = 24;
        private const double BASELINE_TOP_OFFSET = 92;

        private double _scaleFactor;

        // ===== Win32 constants =====
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;

        // ===== Win32 imports =====
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // ===========================
        // Startup
        // ===========================
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("[VSTimer] Initialization starting...");

                CalculateScaleFactor();
                ScaleOverlay();
                PositionOverlayCenter();

                // Apply window styles BEFORE showing
                MakeClickThrough();
                SetWindowForFullscreen();

                // Set properties
                Topmost = true;
                Visibility = Visibility.Hidden;

                Debug.WriteLine($"[VSTimer] Overlay initialized: Size={Width}x{Height}, Position=({Left},{Top})");

                StartDetectionTimer();
                StartPolling();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VSTimer] Error during initialization: {ex.Message}");
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
            {
                Debug.WriteLine("[VSTimer] Failed to get window handle in MakeClickThrough");
                return;
            }
            
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            Debug.WriteLine($"[VSTimer] Current window style: 0x{style:X8}");
            
            int newStyle = style | WS_EX_LAYERED | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, newStyle);
            
            Debug.WriteLine($"[VSTimer] New window style: 0x{newStyle:X8}");
        }

        // ===========================
        // Fullscreen support
        // ===========================
        private void SetWindowForFullscreen()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                Debug.WriteLine("[VSTimer] Failed to get window handle in SetWindowForFullscreen");
                return;
            }
            
            // WS_EX_TOOLWINDOW (0x80) + existing styles
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            int newExStyle = exStyle | 0x00000080; // WS_EX_TOOLWINDOW
            SetWindowLong(hwnd, GWL_EXSTYLE, newExStyle);
            
            Debug.WriteLine($"[VSTimer] Applied toolwindow style for fullscreen support: 0x{newExStyle:X8}");
        }

        // ===========================
        // Valorant detection
        // ===========================
        private void StartDetectionTimer()
        {
            _detectionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
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
                    Debug.WriteLine("[VSTimer] Valorant detected - showing overlay");
                    
                    // Ensure overlay is positioned and visible
                    Visibility = Visibility.Visible;
                    Topmost = true;
                    
                    // Bring window to front
                    try
                    {
                        var hwnd = new WindowInteropHelper(this).Handle;
                        SetForegroundWindow(hwnd);
                    }
                    catch { }
                }
            }
            else
            {
                _valorantHandle = IntPtr.Zero;

                if (_valorantVisible)
                {
                    _valorantVisible = false;
                    Debug.WriteLine("[VSTimer] Valorant not found - hiding overlay");
                    Visibility = Visibility.Hidden;
                }
            }
        }

        private IntPtr FindValorantWindow()
        {
            IntPtr found = IntPtr.Zero;

            try
            {
                // First, try to find the Valorant process
                var valorantProcesses = Process.GetProcessesByName("VALORANT-Win64-Shipping");
                
                if (valorantProcesses.Length == 0)
                {
                    Debug.WriteLine("[VSTimer] No Valorant process found");
                    return IntPtr.Zero;
                }

                Debug.WriteLine($"[VSTimer] Found {valorantProcesses.Length} Valorant process(es)");

                // Get the currently focused window
                IntPtr foregroundWindow = GetForegroundWindow();
                Debug.WriteLine($"[VSTimer] Foreground window: {foregroundWindow.ToString()}");

                // For each Valorant process, find its main window
                foreach (var proc in valorantProcesses)
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        bool isVisible = IsWindowVisible(proc.MainWindowHandle);
                        bool isMinimized = IsIconic(proc.MainWindowHandle);
                        bool isFocused = proc.MainWindowHandle == foregroundWindow;
                        
                        Debug.WriteLine($"[VSTimer] Valorant window: Visible={isVisible}, Minimized={isMinimized}, Focused={isFocused}");
                        
                        // Check if window is visible, not minimized, AND in focus
                        if (isVisible && !isMinimized && isFocused)
                        {
                            found = proc.MainWindowHandle;
                            string mode = DetectWindowMode(proc.MainWindowHandle);
                            Debug.WriteLine($"[VSTimer] Found Valorant window in focus in {mode} mode");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VSTimer] Exception in FindValorantWindow: {ex.Message}");
            }

            return found;
        }

        private string DetectWindowMode(IntPtr hWnd)
        {
            try
            {
                // Get window rect
                if (GetWindowRect(hWnd, out RECT rect))
                {
                    int windowWidth = rect.Right - rect.Left;
                    int windowHeight = rect.Bottom - rect.Top;

                    int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
                    int screenHeight = (int)SystemParameters.PrimaryScreenHeight;

                    // Check if window covers entire screen
                    if (windowWidth >= screenWidth && windowHeight >= screenHeight)
                    {
                        return "Fullscreen";
                    }
                    else
                    {
                        return "Windowed/Borderless";
                    }
                }
            }
            catch { }

            return "Unknown";
        }

        // ===========================
        // Timer logic
        // ===========================
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
            _totalMilliseconds = 45000 - _stopwatch.Elapsed.TotalMilliseconds;

            if (_totalMilliseconds <= 0)
            {
                _stopwatch.Restart();
                _totalMilliseconds = 45000;
            }

            int seconds = (int)(_totalMilliseconds / 1000);
            int milliseconds = (int)(_totalMilliseconds % 1000) / 10;

            if (_totalMilliseconds <= 10000)
            {
                TimerText.Foreground = Brushes.Red;
                TimerText.Text = $"{seconds:D2}:{milliseconds:D2}";
            }
            else if (_totalMilliseconds <= 25000)
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
    }
}
