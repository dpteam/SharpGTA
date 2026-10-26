using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGTA.D3D9
{
    internal enum WindowInputType
    {
        MouseMove,
        LeftButtonDown,
        KeyDown
    }

    internal struct WindowInputEvent
    {
        internal WindowInputType Type;
        internal int X;
        internal int Y;
        internal int Key;
    }

    internal sealed class Win32Window : IDisposable
    {
        private readonly string className = "SharpGTA.D3D9.Window." + Guid.NewGuid().ToString("N");
        private readonly Queue<WindowInputEvent> input = new Queue<WindowInputEvent>();
        private NativeMethods.WndProc wndProcDelegate;
        private IntPtr instance;
        private IntPtr window;
        private bool classRegistered;
        private bool disposed;

        public IntPtr Handle { get { return window; } }
        public int ClientWidth { get; private set; }
        public int ClientHeight { get; private set; }
        public bool CloseRequested { get; private set; }

        public void Create(string title, int width, int height, bool hidden)
        {
            if (disposed)
                throw new ObjectDisposedException("Win32Window");
            if (window != IntPtr.Zero || classRegistered)
                throw new InvalidOperationException("Window is already created.");
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");

            instance = NativeMethods.GetModuleHandle(null);
            if (instance == IntPtr.Zero)
                throw Error("GetModuleHandleW");
            wndProcDelegate = WindowProcedure;
            NativeMethods.WndClassEx windowClass = new NativeMethods.WndClassEx
            {
                Size = (uint)Marshal.SizeOf(typeof(NativeMethods.WndClassEx)),
                Style = NativeMethods.CsHRedraw | NativeMethods.CsVRedraw | NativeMethods.CsOwnDc,
                WndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate),
                Instance = instance,
                Cursor = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(NativeMethods.IdcArrow)),
                ClassName = className
            };

            try
            {
                if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
                    throw Error("RegisterClassExW");
                classRegistered = true;

                NativeMethods.Rect bounds = new NativeMethods.Rect { Right = width, Bottom = height };
                if (!NativeMethods.AdjustWindowRect(ref bounds, NativeMethods.WsOverlappedWindow, false))
                    throw Error("AdjustWindowRect");
                window = NativeMethods.CreateWindowEx(0, className, title,
                    NativeMethods.WsOverlappedWindow, 64, 64,
                    bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                    IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (window == IntPtr.Zero)
                    throw Error("CreateWindowExW");
                UpdateClientSize();
                if (ClientWidth != width || ClientHeight != height)
                    throw new InvalidOperationException("Win32 client size does not match the requested back buffer.");
                if (!hidden)
                {
                    NativeMethods.ShowWindow(window, NativeMethods.SwShow);
                    if (!NativeMethods.UpdateWindow(window))
                        throw new InvalidOperationException("UpdateWindow failed.");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public bool ProcessMessages()
        {
            if (disposed)
                throw new ObjectDisposedException("Win32Window");
            NativeMethods.Message message = new NativeMethods.Message();
            while (NativeMethods.PeekMessage(ref message, IntPtr.Zero, 0, 0, NativeMethods.PmRemove))
            {
                if (message.MessageId == NativeMethods.WmQuit)
                    return false;
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
            return !CloseRequested;
        }

        public bool TryReadInput(out WindowInputEvent value)
        {
            if (input.Count == 0)
            {
                value = new WindowInputEvent();
                return false;
            }
            value = input.Dequeue();
            return true;
        }

        public void ResizeClient(int width, int height)
        {
            if (disposed)
                throw new ObjectDisposedException("Win32Window");
            if (window == IntPtr.Zero)
                throw new InvalidOperationException("Window has not been created.");
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");
            NativeMethods.Rect bounds = new NativeMethods.Rect { Right = width, Bottom = height };
            if (!NativeMethods.AdjustWindowRect(ref bounds, NativeMethods.WsOverlappedWindow, false))
                throw Error("AdjustWindowRect");
            if (!NativeMethods.SetWindowPos(window, IntPtr.Zero, 0, 0,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate))
                throw Error("SetWindowPos");
            UpdateClientSize();
            if (ClientWidth != width || ClientHeight != height)
                throw new InvalidOperationException("Win32 client resize did not reach the requested size.");
        }

        public void UpdateClientSize()
        {
            UpdateClientSize(window);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (window != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(window);
                window = IntPtr.Zero;
            }
            if (classRegistered)
            {
                NativeMethods.UnregisterClass(className, instance);
                classRegistered = false;
            }
            // Native code can no longer dispatch after DestroyWindow/UnregisterClass.
            // Until both complete, this field roots the reverse P/Invoke delegate.
            wndProcDelegate = null;
        }

        private void UpdateClientSize(IntPtr handle)
        {
            NativeMethods.Rect rectangle = new NativeMethods.Rect();
            if (handle != IntPtr.Zero && NativeMethods.GetClientRect(handle, ref rectangle))
            {
                ClientWidth = rectangle.Right - rectangle.Left;
                ClientHeight = rectangle.Bottom - rectangle.Top;
            }
        }

        private IntPtr WindowProcedure(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == NativeMethods.WmClose)
            {
                CloseRequested = true;
                return IntPtr.Zero;
            }
            if (message == NativeMethods.WmDestroy)
            {
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
            }
            if (message == NativeMethods.WmSize)
                UpdateClientSize(hWnd);
            else if (message == NativeMethods.WmMouseMove)
                QueueInput(new WindowInputEvent { Type = WindowInputType.MouseMove,
                    X = SignedLowWord(lParam), Y = SignedHighWord(lParam) });
            else if (message == NativeMethods.WmLButtonDown)
                QueueInput(new WindowInputEvent { Type = WindowInputType.LeftButtonDown,
                    X = SignedLowWord(lParam), Y = SignedHighWord(lParam) });
            else if ((message == NativeMethods.WmKeyDown || message == NativeMethods.WmSysKeyDown) &&
                (lParam.ToInt64() & 0x40000000L) == 0)
                QueueInput(new WindowInputEvent { Type = WindowInputType.KeyDown, Key = wParam.ToInt32() });
            return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
        }

        private void QueueInput(WindowInputEvent value)
        {
            if (input.Count >= 256)
                input.Dequeue();
            input.Enqueue(value);
        }

        private static int SignedLowWord(IntPtr value)
        {
            return unchecked((short)(value.ToInt64() & 0xFFFF));
        }

        private static int SignedHighWord(IntPtr value)
        {
            return unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));
        }

        private static InvalidOperationException Error(string operation)
        {
            return new InvalidOperationException(operation + " failed: Win32 error " +
                Marshal.GetLastWin32Error().ToString());
        }
    }
}
