using System.Runtime.InteropServices;

namespace HudiPet;

internal sealed class MacDesktopActivity
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const int Session = 0;
    private const uint KeyDown = 10, AnyInput = uint.MaxValue;
    private static readonly HashSet<string> Editors = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.apple.TextEdit", "com.apple.Preview", "com.apple.iWork.Pages", "com.apple.iWork.Numbers",
        "com.apple.iWork.Keynote", "com.microsoft.Word", "com.microsoft.Excel", "com.microsoft.Powerpoint",
        "org.libreoffice.script", "com.adobe.Acrobat.Pro"
    };
    private readonly TypingBurstDetector typing = new();
    private readonly IdleActivityDetector idle = new();
    private readonly IntPtr workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
    private readonly IntPtr frontmost = sel_registerName("frontmostApplication");
    private readonly IntPtr processId = sel_registerName("processIdentifier");
    private readonly IntPtr bundleId = sel_registerName("bundleIdentifier");
    private readonly IntPtr utf8 = sel_registerName("UTF8String");
    private readonly IntPtr poolClass = objc_getClass("NSAutoreleasePool");
    private readonly IntPtr create = sel_registerName("new");
    private readonly IntPtr drain = sel_registerName("drain");
    private uint previousKeys = CGEventSourceCounterForEventType(Session, KeyDown);
    private int previousProcess;

    internal void Poll(Action<DesktopActivity> notify)
    {
        IntPtr pool = Send(poolClass, create);
        try
        {
            IntPtr app = Send(workspace, frontmost);
            int pid = SendInt(app, processId);
            string? name = Marshal.PtrToStringUTF8(Send(Send(app, bundleId), utf8));
            bool eligible = pid > 0 && pid != Environment.ProcessId;
            long now = Environment.TickCount64;
            uint keys = CGEventSourceCounterForEventType(Session, KeyDown);
            uint delta = unchecked(keys - previousKeys);
            if (pid != previousProcess || !eligible)
            {
                typing.Reset();
                if (eligible && previousProcess != 0)
                    notify(name is not null && Editors.Contains(name) ? DesktopActivity.DocumentActivated : DesktopActivity.AppSwitched);
            }
            else
            {
                for (int i = 0; i < Math.Min(delta, 3u); i++) typing.KeyDown(now);
                if (typing.Poll(now)) notify(DesktopActivity.TypingStarted);
            }
            previousProcess = pid;
            previousKeys = keys;
            if (idle.Observe(CGEventSourceSecondsSinceLastEventType(Session, AnyInput)) is { } activity) notify(activity);
        }
        finally { SendVoid(pool, drain); }
    }

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr message);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern int SendInt(IntPtr receiver, IntPtr message);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoid(IntPtr receiver, IntPtr message);
    [DllImport(CoreGraphics)] private static extern uint CGEventSourceCounterForEventType(int state, uint type);
    [DllImport(CoreGraphics)] private static extern double CGEventSourceSecondsSinceLastEventType(int state, uint type);
}
