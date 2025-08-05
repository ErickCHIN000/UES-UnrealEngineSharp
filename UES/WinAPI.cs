using System;
using System.Runtime.InteropServices;

namespace UES
{
    /// <summary>
    /// Windows API imports for memory operations and process manipulation
    /// AOT-compatible implementation with explicit marshaling
    /// </summary>
    internal static class WinAPI
    {
        // Memory operations
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, nint lpBaseAddress, byte[] buffer, int nSize, out int lpNumberOfBytesRead);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, byte[] buffer, int nSize, out int lpNumberOfBytesWritten);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, int flAllocationType, int flProtect);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, int dwFreeType);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualProtectEx(IntPtr hProcess, nint lpAddress, int dwSize, uint flNewProtect, out uint lpflOldProtect);

        // Process and thread operations
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int CloseHandle(IntPtr hObject);

        // Window operations
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);
        
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        // Memory allocation types
        public const int MEM_COMMIT = 0x1000;
        public const int MEM_RESERVE = 0x2000;
        public const int MEM_RELEASE = 0x8000;
        
        // Memory protection constants
        public const uint PAGE_EXECUTE_READWRITE = 0x40;
        public const uint PAGE_READWRITE = 0x04;
        public const uint PAGE_READONLY = 0x02;
        public const uint PAGE_EXECUTE_READ = 0x20;
        
        // Wait constants
        public const uint INFINITE = 0xFFFFFFFF;
        public const uint WAIT_OBJECT_0 = 0x00000000;
        public const uint WAIT_TIMEOUT = 0x00000102;
    }
}
