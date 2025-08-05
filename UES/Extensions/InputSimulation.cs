using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace UES.Extensions
{
    /// <summary>
    /// Input simulation utilities for mouse and keyboard interaction
    /// Provides aimbot and input automation functionality
    /// </summary>
    public static class InputSimulation
    {
        #region Windows API Imports

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public MOUSEKEYBDHARDWAREINPUT Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct MOUSEKEYBDHARDWAREINPUT
        {
            [FieldOffset(0)] public MOUSEINPUT Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        #endregion

        #region Constants

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        #endregion

        /// <summary>
        /// Default screen resolution used for calculations
        /// </summary>
        public static Vector2 DefaultScreenResolution { get; set; } = new Vector2(2560, 1440);

        /// <summary>
        /// Smoothly aims towards a target position on screen
        /// </summary>
        /// <param name="targetScreenPosition">Target position on screen</param>
        /// <param name="smoothSpeed">Smoothing factor (higher = less smooth)</param>
        /// <param name="screenResolution">Screen resolution to use</param>
        public static void AimAtPosition(Vector2 targetScreenPosition, float smoothSpeed = 8.0f, Vector2? screenResolution = null)
        {
            var resolution = screenResolution ?? DefaultScreenResolution;
            var screenCenterX = resolution.X / 2;
            var screenCenterY = resolution.Y / 2;

            // Don't aim if target is too far from center (prevent wild movements)
            if (Math.Abs(targetScreenPosition.X - screenCenterX) > screenCenterX / 4) return;
            if (Math.Abs(targetScreenPosition.Y - screenCenterY) > screenCenterY / 4) return;

            float targetX = 0;
            float targetY = 0;

            // Calculate movement needed
            if (targetScreenPosition.X > screenCenterX)
            {
                targetX = -(screenCenterX - targetScreenPosition.X);
            }
            else if (targetScreenPosition.X < screenCenterX)
            {
                targetX = targetScreenPosition.X - screenCenterX;
            }

            if (targetScreenPosition.Y > screenCenterY)
            {
                targetY = -(screenCenterY - targetScreenPosition.Y);
            }
            else if (targetScreenPosition.Y < screenCenterY)
            {
                targetY = targetScreenPosition.Y - screenCenterY;
            }

            // Apply smoothing
            targetX /= smoothSpeed;
            targetY /= smoothSpeed;

            // Clamp to maximum movement per frame
            var maxSmooth = 15;
            if (targetX > maxSmooth) targetX = maxSmooth;
            if (targetY > maxSmooth) targetY = maxSmooth;
            if (targetX < -maxSmooth) targetX = -maxSmooth;
            if (targetY < -maxSmooth) targetY = -maxSmooth;

            // Execute mouse movement
            mouse_event(MOUSEEVENTF_MOVE, (int)Math.Round(targetX), (int)Math.Round(targetY), 0, 0);
        }

        /// <summary>
        /// Performs a smooth mouse movement to the specified position
        /// </summary>
        /// <param name="deltaX">X movement amount</param>
        /// <param name="deltaY">Y movement amount</param>
        public static void MoveMouse(int deltaX, int deltaY)
        {
            mouse_event(MOUSEEVENTF_MOVE, deltaX, deltaY, 0, 0);
        }

        /// <summary>
        /// Performs a left mouse click with proper timing
        /// </summary>
        /// <param name="clickDuration">Duration to hold the click in milliseconds</param>
        public static async Task LeftClick(int clickDuration = 15)
        {
            // Press down
            var inputDown = new INPUT[] 
            { 
                new INPUT { Type = 0, Data = new MOUSEKEYBDHARDWAREINPUT { Mouse = new MOUSEINPUT { Flags = MOUSEEVENTF_LEFTDOWN } } } 
            };
            SendInput((uint)inputDown.Length, inputDown, Marshal.SizeOf<INPUT>());

            // Wait
            await Task.Delay(clickDuration);

            // Release
            var inputUp = new INPUT[] 
            { 
                new INPUT { Type = 0, Data = new MOUSEKEYBDHARDWAREINPUT { Mouse = new MOUSEINPUT { Flags = MOUSEEVENTF_LEFTUP } } } 
            };
            SendInput((uint)inputUp.Length, inputUp, Marshal.SizeOf<INPUT>());
        }

        /// <summary>
        /// Performs a right mouse click with proper timing
        /// </summary>
        /// <param name="clickDuration">Duration to hold the click in milliseconds</param>
        public static async Task RightClick(int clickDuration = 15)
        {
            // Press down
            var inputDown = new INPUT[] 
            { 
                new INPUT { Type = 0, Data = new MOUSEKEYBDHARDWAREINPUT { Mouse = new MOUSEINPUT { Flags = MOUSEEVENTF_RIGHTDOWN } } } 
            };
            SendInput((uint)inputDown.Length, inputDown, Marshal.SizeOf<INPUT>());

            // Wait
            await Task.Delay(clickDuration);

            // Release
            var inputUp = new INPUT[] 
            { 
                new INPUT { Type = 0, Data = new MOUSEKEYBDHARDWAREINPUT { Mouse = new MOUSEINPUT { Flags = MOUSEEVENTF_RIGHTUP } } } 
            };
            SendInput((uint)inputUp.Length, inputUp, Marshal.SizeOf<INPUT>());
        }

        /// <summary>
        /// Performs rapid-fire clicking at specified intervals
        /// </summary>
        /// <param name="clickCount">Number of clicks to perform</param>
        /// <param name="intervalMs">Interval between clicks in milliseconds</param>
        public static async Task RapidFire(int clickCount, int intervalMs = 50)
        {
            for (int i = 0; i < clickCount; i++)
            {
                await LeftClick(10);
                if (i < clickCount - 1) // Don't wait after the last click
                {
                    await Task.Delay(intervalMs);
                }
            }
        }

        /// <summary>
        /// Smoothly aims at a target and optionally fires
        /// </summary>
        /// <param name="targetScreenPosition">Target position on screen</param>
        /// <param name="smoothSpeed">Aiming smooth speed</param>
        /// <param name="autoFire">Whether to automatically fire when aimed</param>
        /// <param name="screenResolution">Screen resolution to use</param>
        public static async Task AimAndFire(Vector2 targetScreenPosition, float smoothSpeed = 8.0f, 
            bool autoFire = true, Vector2? screenResolution = null)
        {
            AimAtPosition(targetScreenPosition, smoothSpeed, screenResolution);
            
            if (autoFire)
            {
                // Small delay to let the aim settle
                await Task.Delay(20);
                await LeftClick();
            }
        }

        /// <summary>
        /// Checks if a target position is within reasonable aiming distance
        /// </summary>
        /// <param name="targetScreenPosition">Target position on screen</param>
        /// <param name="maxDistance">Maximum distance from screen center</param>
        /// <param name="screenResolution">Screen resolution to use</param>
        /// <returns>True if target is within aiming range</returns>
        public static bool IsTargetInRange(Vector2 targetScreenPosition, float maxDistance = 200.0f, Vector2? screenResolution = null)
        {
            var resolution = screenResolution ?? DefaultScreenResolution;
            var screenCenter = new Vector2(resolution.X / 2, resolution.Y / 2);
            var distance = Vector2.Distance(targetScreenPosition, screenCenter);
            return distance <= maxDistance;
        }

        /// <summary>
        /// Gets the current mouse position relative to screen center
        /// </summary>
        /// <param name="screenResolution">Screen resolution to use</param>
        /// <returns>Position relative to screen center</returns>
        public static Vector2 GetMousePositionRelativeToCenter(Vector2? screenResolution = null)
        {
            var resolution = screenResolution ?? DefaultScreenResolution;
            var screenCenter = new Vector2(resolution.X / 2, resolution.Y / 2);
            
            // Note: Getting actual cursor position would require additional Windows API calls
            // For now, assuming center position (this could be enhanced)
            return Vector2.Zero;
        }
    }
}