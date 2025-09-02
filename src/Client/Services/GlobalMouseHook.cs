using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace Client;

public sealed class GlobalMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MOUSEWHEEL = 0x020A;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelMouseProc? _proc;

    public event Action<Shared.MouseAction, double, double, int>? OnMouse;

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException("Failed to set WH_MOUSE_LL hook");
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var vx = SystemParameters.VirtualScreenLeft;
            var vy = SystemParameters.VirtualScreenTop;
            var vw = SystemParameters.VirtualScreenWidth;
            var vh = SystemParameters.VirtualScreenHeight;
            var nx = Math.Clamp((data.pt.x - vx) / vw, 0, 1);
            var ny = Math.Clamp((data.pt.y - vy) / vh, 0, 1);

            switch (msg)
            {
                case WM_MOUSEMOVE:
                    OnMouse?.Invoke(Shared.MouseAction.Move, nx, ny, 0);
                    break;
                case WM_LBUTTONDOWN:
                    OnMouse?.Invoke(Shared.MouseAction.LeftDown, nx, ny, 0);
                    break;
                case WM_LBUTTONUP:
                    OnMouse?.Invoke(Shared.MouseAction.LeftUp, nx, ny, 0);
                    break;
                case WM_RBUTTONDOWN:
                    OnMouse?.Invoke(Shared.MouseAction.RightDown, nx, ny, 0);
                    break;
                case WM_RBUTTONUP:
                    OnMouse?.Invoke(Shared.MouseAction.RightUp, nx, ny, 0);
                    break;
                case WM_MOUSEWHEEL:
                    int delta = (short)((data.mouseData >> 16) & 0xffff);
                    OnMouse?.Invoke(Shared.MouseAction.Wheel, nx, ny, delta);
                    break;
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
