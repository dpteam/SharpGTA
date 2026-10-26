using System;
using System.Runtime.InteropServices;

namespace SharpGTA.D3D9
{
    internal static class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

        internal const uint WsOverlappedWindow = 0x00CF0000;
        internal const uint CsHRedraw = 0x0002;
        internal const uint CsVRedraw = 0x0001;
        internal const uint CsOwnDc = 0x0020;
        internal const uint PmRemove = 1;
        internal const uint WmDestroy = 0x0002;
        internal const uint WmClose = 0x0010;
        internal const uint WmQuit = 0x0012;
        internal const uint WmSize = 0x0005;
        internal const int SwShow = 5;
        internal const int IdcArrow = 32512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WndClassEx
        {
            internal uint Size;
            internal uint Style;
            internal IntPtr WndProc;
            internal int ClassExtra;
            internal int WindowExtra;
            internal IntPtr Instance;
            internal IntPtr Icon;
            internal IntPtr Cursor;
            internal IntPtr BackgroundBrush;
            [MarshalAs(UnmanagedType.LPWStr)]
            internal string MenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            internal string ClassName;
            internal IntPtr SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Message
        {
            internal IntPtr Window;
            internal uint MessageId;
            internal IntPtr WParam;
            internal IntPtr LParam;
            internal uint Time;
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "RegisterClassExW", SetLastError = true)]
        internal static extern ushort RegisterClassEx(ref WndClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "UnregisterClassW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "CreateWindowExW", SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName,
            uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu,
            IntPtr instance, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "DefWindowProcW")]
        internal static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "DestroyWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "ShowWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "UpdateWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateWindow(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "PeekMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PeekMessage(ref Message message, IntPtr window,
            uint minMessage, uint maxMessage, uint removeMessage);

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "TranslateMessage")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "PostQuitMessage")]
        internal static extern void PostQuitMessage(int exitCode);

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "GetClientRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr window, ref Rect rectangle);

        [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "AdjustWindowRect", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AdjustWindowRect(ref Rect rectangle, uint style,
            [MarshalAs(UnmanagedType.Bool)] bool menu);

        [DllImport("user32.dll", EntryPoint = "LoadCursorW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("d3d9.dll", EntryPoint = "Direct3DCreate9", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr Direct3DCreate9(uint sdkVersion);
    }
}
