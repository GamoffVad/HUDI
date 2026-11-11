using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace HudiPet;

// Metadata only. The keyboard callback never reads lParam (the key payload).
// Clipboard text, document contents, file names and window titles are not read.
internal sealed class DesktopActivityMonitor : IDisposable
{
    private readonly HwndSource source;
    private readonly Action<DesktopActivity> onActivity;
    private readonly DispatcherTimer timer;
    private readonly Native.KeyboardCallback keyboardCallback;
    private readonly InputBurstDetector typing=new(), pointer=new();
    private readonly IdleActivityDetector idle=new();
    private IntPtr keyboardHook,previousWindow;
    private uint clipboardSequence;
    private Native.CursorPoint? previousPointer;
    private bool disposed,initialized;
    internal bool ClipboardAttached {get;private set;}
    internal bool KeyboardAttached=>keyboardHook!=IntPtr.Zero;
    private static readonly HashSet<string> ShellProcesses=new(StringComparer.OrdinalIgnoreCase)
    {"explorer","StartMenuExperienceHost","SearchHost","SearchApp","ShellExperienceHost","TextInputHost","LockApp","dwm","Widgets","WidgetService","ShellHost"};
    private static readonly HashSet<string> DocumentProcesses=new(StringComparer.OrdinalIgnoreCase)
    {"WINWORD","EXCEL","POWERPNT","Acrobat","AcroRd32","notepad","notepad++","wordpad","soffice","soffice.bin","FoxitPDFReader","SumatraPDF"};

    internal DesktopActivityMonitor(HwndSource source,Action<DesktopActivity> onActivity)
    {
        this.source=source;this.onActivity=onActivity;
        clipboardSequence=Native.GetClipboardSequenceNumber();source.AddHook(WindowMessage);
        ClipboardAttached=Native.AddClipboardFormatListener(source.Handle);
        keyboardCallback=KeyboardEvent;
        keyboardHook=Native.SetWindowsHookEx(13,keyboardCallback,Native.GetModuleHandle(null),0);
        if(Native.GetCursorPos(out var initialPointer))previousPointer=initialPointer;
        timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(150)};
        timer.Tick+=Tick;timer.Start();
    }
    private IntPtr KeyboardEvent(int code,IntPtr message,IntPtr payload)
    {
        // Count key-down notifications; never marshal/read the key structure.
        if(code>=0&&!disposed&&(message==(IntPtr)0x100||message==(IntPtr)0x104))typing.Observe(Environment.TickCount64);
        return Native.CallNextHookEx(keyboardHook,code,message,payload);
    }
    private bool EligibleForeground(out IntPtr window,out uint pid,out string processName,bool allowExplorer=false)
    {
        window=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(window,out pid);processName="";
        if(pid==0||pid==Environment.ProcessId)return false;
        try
        {
            using var process=Process.GetProcessById((int)pid);processName=process.ProcessName;
            return !ShellProcesses.Contains(processName)||(allowExplorer&&processName.Equals("explorer",StringComparison.OrdinalIgnoreCase));
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return false;}
    }
    private IntPtr WindowMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x031D&&!disposed)
        {
            uint sequence=Native.GetClipboardSequenceNumber();if(sequence==clipboardSequence)return IntPtr.Zero;
            clipboardSequence=sequence;
            if(IdleSeconds()<8&&EligibleForeground(out _,out _,out _,allowExplorer:true))
            {
                if(Native.IsClipboardFormatAvailable(15))onActivity(DesktopActivity.FilesCopied);
                else if(Native.IsClipboardFormatAvailable(13))onActivity(DesktopActivity.TextCopied);
            }
        }
        return IntPtr.Zero;
    }
    private void Tick(object? sender,EventArgs args)
    {
        if(disposed)return;
        long now=Environment.TickCount64;
        bool eligible=EligibleForeground(out var window,out _,out string name,allowExplorer:true)
            && window!=Native.GetShellWindow();
        if(initialized&&eligible&&window!=previousWindow)
        {
            // Editor activation is not proof of a file-open/save operation.
            if(DocumentProcesses.Contains(name))onActivity(DesktopActivity.DocumentActivated);
            else onActivity(DesktopActivity.AppSwitched);
        }
        previousWindow=window;initialized=true;
        if(eligible&&typing.Take())onActivity(DesktopActivity.TypingStarted);
        else if(!eligible)typing.Reset();
        if(Native.GetCursorPos(out var position))
        {
            if(previousPointer is {} old&&(position.X!=old.X||position.Y!=old.Y))pointer.Observe(now);
            previousPointer=position;
            if(pointer.Take())onActivity(DesktopActivity.PointerMoved);
        }
        if(idle.Observe(IdleSeconds()) is {} activity)onActivity(activity);
    }
    private static double IdleSeconds()
    {
        var input=new Native.LastInput{Size=(uint)Marshal.SizeOf<Native.LastInput>()};
        return Native.GetLastInputInfo(ref input)?unchecked((uint)Environment.TickCount-input.Time)/1000d:0;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;timer.Stop();timer.Tick-=Tick;
        source.RemoveHook(WindowMessage);if(ClipboardAttached){Native.RemoveClipboardFormatListener(source.Handle);ClipboardAttached=false;}
        if(keyboardHook!=IntPtr.Zero){Native.UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero;}
        typing.Reset();
    }
    private static class Native
    {
        internal delegate IntPtr KeyboardCallback(int code,IntPtr message,IntPtr payload);
        [StructLayout(LayoutKind.Sequential)] internal struct LastInput {internal uint Size,Time;}
        [StructLayout(LayoutKind.Sequential)] internal struct CursorPoint {internal int X,Y;}
        [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out CursorPoint point);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool RemoveClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GetLastInputInfo(ref LastInput input);
        [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int type,KeyboardCallback callback,IntPtr module,uint threadId);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr payload);
        [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    }
}
