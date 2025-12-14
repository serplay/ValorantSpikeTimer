using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace ValorantSpikeTimer.Benchmarks
{
    public class SpikeDetectionBenchmark
    {
        private Config _config;
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
        [GlobalSetup]
        public void Setup()
        {
            _config = new Config
            {
                LeftPixelX = 100,
                LeftPixelY = 100,
                CenterPixelX = 150,
                CenterPixelY = 100,
                RightPixelX = 200,
                RightPixelY = 100,
                RedMin = 120,
                GreenMax = 30,
                BlueMax = 30
            };
        }

        [Benchmark(Baseline = true)]
        public bool CurrentSpikeDetection()
        {
            // Simulate current detection logic
            int leftX = _config.LeftPixelX;
            int leftY = _config.LeftPixelY;
            int centerX = _config.CenterPixelX;
            int centerY = _config.CenterPixelY;
            int rightX = _config.RightPixelX;
            int rightY = _config.RightPixelY;
            // Get pixel colors at all three positions
            (int r1, int g1, int b1) = GetPixelColorAbsolute(leftX, leftY);
            (int r2, int g2, int b2) = GetPixelColorAbsolute(centerX, centerY);
            (int r3, int g3, int b3) = GetPixelColorAbsolute(rightX, rightY);
            // Method 1: Absolute threshold check
            bool leftMatchesAbsolute = r1 > _config.RedMin && g1 < _config.GreenMax && b1 < _config.BlueMax;
            bool centerMatchesAbsolute = r2 > _config.RedMin && g2 < _config.GreenMax && b2 < _config.BlueMax;
            bool rightMatchesAbsolute = r3 > _config.RedMin && g3 < _config.GreenMax && b3 < _config.BlueMax;
            // Method 2: Ratio-based check
            bool leftMatchesRatio = r1 > 100 && r1 > 2 * (g1 + b1);
            bool centerMatchesRatio = r2 > 100 && r2 > 2 * (g2 + b2);
            bool rightMatchesRatio = r3 > 100 && r3 > 2 * (g3 + b3);
            bool leftMatches = leftMatchesAbsolute || leftMatchesRatio;
            bool centerMatches = centerMatchesAbsolute || centerMatchesRatio;
            bool rightMatches = rightMatchesAbsolute || rightMatchesRatio;
            // At least 2 of 3 points must match
            int matchCount = (leftMatches ? 1 : 0) + (centerMatches ? 1 : 0) + (rightMatches ? 1 : 0);
            return matchCount >= 2;
        }

        [Benchmark]
        public bool OptimizedBatchedSpikeDetection()
        {
            // Optimized: Get DC once, read all pixels, release once
            IntPtr desktopHwnd = GetDesktopWindow();
            IntPtr hdc = GetDC(desktopHwnd);
            if (hdc == IntPtr.Zero)
                return false;

            try
            {
                // Read all 3 pixels with single DC
                uint pixel1 = GetPixel(hdc, _config.LeftPixelX, _config.LeftPixelY);
                uint pixel2 = GetPixel(hdc, _config.CenterPixelX, _config.CenterPixelY);
                uint pixel3 = GetPixel(hdc, _config.RightPixelX, _config.RightPixelY);

                // Extract RGB for all pixels
                int r1 = (int)pixel1 & 0xFF;
                int g1 = (int)(pixel1 >> 8) & 0xFF;
                int b1 = (int)(pixel1 >> 16) & 0xFF;

                int r2 = (int)pixel2 & 0xFF;
                int g2 = (int)(pixel2 >> 8) & 0xFF;
                int b2 = (int)(pixel2 >> 16) & 0xFF;

                int r3 = (int)pixel3 & 0xFF;
                int g3 = (int)(pixel3 >> 8) & 0xFF;
                int b3 = (int)(pixel3 >> 16) & 0xFF;

                // Early exit optimization: check each point and count matches
                int matchCount = 0;

                // Check left pixel
                if ((r1 > _config.RedMin && g1 < _config.GreenMax && b1 < _config.BlueMax) ||
                    (r1 > 100 && r1 > 2 * (g1 + b1)))
                {
                    matchCount++;
                }

                // Check center pixel
                if ((r2 > _config.RedMin && g2 < _config.GreenMax && b2 < _config.BlueMax) ||
                    (r2 > 100 && r2 > 2 * (g2 + b2)))
                {
                    matchCount++;
                    if (matchCount >= 2) return true; // Early exit!
                }

                // Check right pixel only if needed
                if (matchCount < 2 &&
                    ((r3 > _config.RedMin && g3 < _config.GreenMax && b3 < _config.BlueMax) ||
                     (r3 > 100 && r3 > 2 * (g3 + b3))))
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

        private (int r, int g, int b) GetPixelColorAbsolute(int x, int y)
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
    }
}