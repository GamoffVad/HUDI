using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace HudiPet;

internal enum DesktopActivity { FilesCopied, TextCopied, ProgramOpened, AppSwitched, ReturnedToWork }

// Observe Windows event metadata only. Clipboard contents, filenames, window titles
// and keystrokes are neither read nor stored.
internal sealed class DesktopActivityMonitor : IDisposable
{
    private readonly HwndSource source;
    private readonly Action<DesktopActivity> onActivity;
    private readonly DispatcherTimer timer;
    private readonly HashSet<uint> knownApps = [];
    private readonly Dictionary<DesktopActivity, long> lastEvents = [];
    private uint previousForeground, clipboardSequence;
    private double previousIdle;
    private bool disposed;
    internal bool ClipboardAttached { get; }
    private readonly HashSet<string> shellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "StartMenuExperienceHost", "SearchHost", "SearchApp", "ShellExperienceHost",
        "TextInputHost", "LockApp", "dwm", "Widgets", "WidgetService", "ShellHost"
    };

    internal DesktopActivityMonitor(HwndSource source, Action<DesktopActivity> onActivity)
    {
        this.source = source;
        this.onActivity = onActivity;
        Native.EnumWindows((window, _) =>
        {
            if (Native.IsWindowVisible(window))
            {
                Native.GetWindowThreadProcessId(window, out uint pid);
                knownApps.Add(pid);
            }
            return true;
        }, IntPtr.Zero);
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out previousForeground);
        clipboardSequence = Native.GetClipboardSequenceNumber();
        previousIdle = IdleSeconds();
        source.AddHook(WindowMessage);
        ClipboardAttached = Native.AddClipboardFormatListener(source.Handle);
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += Tick;
        timer.Start();
    }

    private void Emit(DesktopActivity activity)
    {
        long now = Environment.TickCount64;
        long cooldown = activity == DesktopActivity.AppSwitched ? 18000 : 8000;
        if (lastEvents.TryGetValue(activity, out long last) && now - last < cooldown) return;
        lastEvents[activity] = now;
        onActivity(activity);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x031D && !disposed) // WM_CLIPBOARDUPDATE
        {
            uint sequence = Native.GetClipboardSequenceNumber();
            if (sequence != clipboardSequence)
            {
                clipboardSequence = sequence;
                if (IdleSeconds() < 8)
                {
                    if (Native.IsClipboardFormatAvailable(15)) Emit(DesktopActivity.FilesCopied); // CF_HDROP
                    else if (Native.IsClipboardFormatAvailable(13)) Emit(DesktopActivity.TextCopied); // CF_UNICODETEXT
                }
            }
        }
        return IntPtr.Zero;
    }

    private void Tick(object? sender, EventArgs e)
    {
        double idle = IdleSeconds();
        if (previousIdle >= 90 && idle < 3) Emit(DesktopActivity.ReturnedToWork);
        previousIdle = idle;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        if (pid == 0 || pid == previousForeground) return;
        previousForeground = pid;
        if (pid == Environment.ProcessId) return;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (shellProcesses.Contains(process.ProcessName)) return;
            Emit(knownApps.Add(pid) ? DesktopActivity.ProgramOpened : DesktopActivity.AppSwitched);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static double IdleSeconds()
    {
        var input = new Native.LastInput { Size = (uint)Marshal.SizeOf<Native.LastInput>() };
        return Native.GetLastInputInfo(ref input) ? unchecked((uint)Environment.TickCount - input.Time) / 1000d : 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        timer.Tick -= Tick;
        source.RemoveHook(WindowMessage);
        if (ClipboardAttached) Native.RemoveClipboardFormatListener(source.Handle);
    }

    private static class Native
    {
        internal delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)] internal struct LastInput { internal uint Size, Time; }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool RemoveClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetLastInputInfo(ref LastInput input);
    }
}
