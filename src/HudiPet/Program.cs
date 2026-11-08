using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace HudiPet;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--verify")) return PetVerification.Run();
            if (args.Contains("--render-preview")) return PetVerification.ExportMotionPreview();
            if (args.Contains("--verify-chat")) return ChatVerification.Run().GetAwaiter().GetResult();
            if(args.Length==2&&args[0]=="--verify-pets")return PetCatalogVerification.Run(args[1]);
            if(args.Length==3&&args[0]=="--verify-pet-package")return PetPackageCommands.Verify(args[1],args[2]);
            if(args.Length==3&&args[0]=="--import-pet")return PetPackageCommands.Import(args[1],args[2]);
            if(args.Length==2&&args[0]=="--render-ui")return UiVerification.Run(args[1]);
            if(args.Contains("--preview-transparency"))return UiVerification.PreviewTransparency();
            if(args.Contains("--preview-matte"))return UiVerification.PreviewTransparency(matte:true);
            if(args.Contains("--preview-answer"))return UiVerification.PreviewTransparency(matte:true,answer:true);
            using var singleton = new Mutex(true, args.Contains("--smoke-test") || args.Contains("--ui-test") ? "Local\\HUDI.DesktopPet.Test" : "Local\\HUDI.DesktopPet", out bool first);
            if (!first) return 0;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Ui.Initialize();
            if (Hudi.Licensing.LicenseCodec.ActivationRequired && !Hudi.Licensing.LicenseCodec.IsActivated(out _) && new ActivationWindow().ShowDialog()!=true) return 0;
            var pet = new PetWindow(args.Contains("--smoke-test"), args.Contains("--ui-test"));
            app.MainWindow=pet;app.ShutdownMode=ShutdownMode.OnMainWindowClose;
            if (args.Contains("--show-settings")) pet.Loaded += (_,_) => pet.OpenSettings();
            return app.Run(pet);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(PetWindow.SettingsDirectory);
            File.WriteAllText(Path.Combine(PetWindow.SettingsDirectory, "last-error.txt"), exception.ToString());
            if (!args.Any(a => a.StartsWith("--"))) MessageBox.Show("Не удалось запустить HUDI. Подробности: " + PetWindow.SettingsDirectory, "HUDI");
            return 1;
        }
    }
}

internal sealed partial class PetWindow : Window
{
    internal static readonly string SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HUDI");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    private PetVisual character = null!;
    private PetBehavior behavior = null!;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly bool smokeTest;
    private readonly bool uiTest;
    private double lastDiagnostics;
    private double lastLicenseCheck;
    private int clickReactions;
    private double previousTime, speed = 1;
    internal double DialogTransparency { get; private set; } = 60;
    internal bool MatteDialogs { get; private set; } = true;
    internal event Action? DialogTransparencyChanged;
    private bool paused, dragging, restoring;
    private Point? dragOrigin;
    private int renderedFrames;
    private readonly DispatcherTimer clickSequence = new();
    private int pendingClicks,pressedClickCount;
    internal TimeSpan ClickSequenceDelay=>clickSequence.Interval;
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    internal PetWindow(bool smokeTest, bool uiTest,PetCatalog? pets=null)
    {
        this.smokeTest = smokeTest;
        this.uiTest = uiTest;
        InitializePet(pets);
        Title = PetName+" — живой питомец";
        Height = 360; Width = Height * library.Width / library.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = uiTest; ShowActivated = false;
        Content = character;
        InitializeChat();
        clickSequence.Interval=TimeSpan.FromMilliseconds(GetDoubleClickTime()+20);
        clickSequence.Tick+=(_,_)=>CommitPetClicks();
        MouseRightButtonUp+=(_,e)=> { if(character.ContainsCharacter(e.GetPosition(character))) { CancelPetClicks();OpenMenu();e.Handled=true; } };
        MouseLeftButtonDown += (_, e) =>
        {
            if (!character.ContainsCharacter(e.GetPosition(character))) return;
            clickSequence.Stop();pressedClickCount=Math.Min(e.ClickCount,3);
            dragOrigin = e.GetPosition(this);
            character.CaptureMouse();
        };
        MouseLeftButtonUp += (_, _) =>
        {
            int clicks=dragOrigin is not null&&!dragging?pressedClickCount:0;
            dragOrigin = null;
            pressedClickCount=0;
            character.ReleaseMouseCapture();
            if(clicks>0)QueuePetClick(clicks);
        };
        MouseMove += (_, e) =>
        {
            if (dragOrigin is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(this);
            if (Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y) < 4) return;
            dragOrigin = null; dragging = true;
            CancelPetClicks();
            character.ReleaseMouseCapture();
            try { DragMove(); } finally { dragging = false; ClampToScreen(); SaveSettings(); }
        };
        MouseWheel += (_, e) => { CancelPetClicks();ResizePet(Height * (e.Delta > 0 ? 1.08 : 1 / 1.08));e.Handled=true; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            RestoreSettings();
            previousTime = clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += Render;
        };
        Closed += (_, _) => { CompositionTarget.Rendering -= Render;clickSequence.Stop();DisposeChat();SaveSettings();if(!selectedPet.BuiltIn)library.Dispose(); };
    }

    internal PetAction? TriggerRandomClickAction()
    {
        CancelPetClicks();dragOrigin=null;character.ReleaseMouseCapture();
        paused=false;clickReactions++;
        return behavior.TriggerRandomAction();
    }

    private void CancelPetClicks()
    {
        clickSequence.Stop();pendingClicks=0;pressedClickCount=0;
    }
    internal void QueuePetClick(int count)
    {
        clickSequence.Stop();pendingClicks=Math.Clamp(count,1,3);
        if(pendingClicks==3)CommitPetClicks();
        else clickSequence.Start();
    }
    private void CommitPetClicks()
    {
        int count=pendingClicks;CancelPetClicks();
        if(count==3)OpenChat(true);
        else if(count==2)OpenNotes();
        else if(count==1)TriggerRandomClickAction();
    }

    private void Render(object? sender, EventArgs args)
    {
        double now = clock.Elapsed.TotalSeconds, dt = now - previousTime;
        if(Hudi.Licensing.LicenseCodec.ActivationRequired && now-lastLicenseCheck>60)
        {
            lastLicenseCheck=now;
            if(!Hudi.Licensing.LicenseCodec.IsActivated(out _))
            {
                paused=true;chat.Cancel();
                if(new ActivationWindow{Owner=this}.ShowDialog()!=true){Close();return;}
                paused=false;previousTime=clock.Elapsed.TotalSeconds;return;
            }
        }
        if (dt < 1d / 100) return;
        previousTime = now;
        if (!paused)
        {
            character.Apply(behavior.Update(dt * speed));
            renderedFrames++;
        }
        if (uiTest && now - lastDiagnostics > .25)
        {
            lastDiagnostics = now;
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(Path.Combine(SettingsDirectory, "ui-state-v7.json"), JsonSerializer.Serialize(new
            {
                clickReactions, action = behavior.CurrentAction?.ToString(), behavior.ActionsStarted,
                behavior.BreathingUpdates, behavior.Interruptions, behavior.HoldingPockets, behavior.PocketHoldDuration,
                cursorTracking = false, hoverReaction = false, eyeReconstruction = false,
                library.HorizontalMirrored,pet=PetName,
                panelMaterials = OwnedWindows.Cast<Window>().Select(window=>new { window.Title, acrylicMode=Hudi.Distribution.FrostedBackdrop.ReadMode(window), window.AllowsTransparency }).ToArray(),
                paused, Left, Top, Width, Height
            }));
        }
        if (smokeTest && now >= 10)
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(Path.Combine(SettingsDirectory, "smoke-test-v7.json"), JsonSerializer.Serialize(new
            {
                success = renderedFrames > 200 && behavior.ActionsStarted > 0 && character.PoseChanges > 20,
                renderedFrames, behavior.ActionsStarted, behavior.BreathingUpdates, character.PoseChanges,
                transparent = AllowsTransparency, Topmost, Width, Height, library.HorizontalMirrored,
                cursorTracking = false, hoverReaction = false, eyeReconstruction = false,
                estimatedFps = renderedFrames / now, workingSetMB = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024
            }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }

    internal void ResizePet(double height)
    {
        if (!double.IsFinite(height)) return;
        height = Math.Clamp(height, 180, Math.Min(720, SystemParameters.VirtualScreenHeight));
        double center = Left + Width / 2, bottom = Top + Height;
        Height = height; Width = height * library.Width / library.Height;
        Left = center - Width / 2; Top = bottom - Height;
        ClampToScreen(); SaveSettings();
    }

    private void RestoreSettings()
    {
        restoring = true;
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 30; Top = area.Bottom - Height - 5;
        if (!smokeTest && !uiTest && File.Exists(SettingsPath))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (settings is not null && double.IsFinite(settings.Left) && double.IsFinite(settings.Top))
                {
                    Height = double.IsFinite(settings.Height) ? Math.Clamp(settings.Height, 180, 720) : 360; Width = Height * library.Width / library.Height;
                    Left = settings.Left; Top = settings.Top;
                    speed = double.IsFinite(settings.Speed) ? Math.Clamp(settings.Speed, .5, 1.5) : 1;
                    Topmost = settings.Topmost;
                    DialogTransparency = NormalizeDialogTransparency(settings.DialogTransparency);
                    MatteDialogs = settings.MatteDialogs;
                }
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        }
        restoring = false; ClampToScreen();
    }

    private void ClampToScreen()
    {
        double x = SystemParameters.VirtualScreenLeft, y = SystemParameters.VirtualScreenTop;
        Left = Math.Clamp(Left, x, Math.Max(x, x + SystemParameters.VirtualScreenWidth - Width));
        Top = Math.Clamp(Top, y, Math.Max(y, y + SystemParameters.VirtualScreenHeight - Height));
    }

    private void SaveSettings()
    {
        if (smokeTest || uiTest || restoring || !IsLoaded) return;
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings { Left = Left, Top = Top, Height = Height, Speed = speed, Topmost = Topmost, DialogTransparency = DialogTransparency, MatteDialogs = MatteDialogs }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal static double NormalizeDialogTransparency(double value) => double.IsFinite(value)?Math.Clamp(value,0,90):60;
    internal void SetMatteDialogs(bool enabled)
    {
        if(MatteDialogs==enabled)return;
        MatteDialogs=enabled;DialogTransparencyChanged?.Invoke();SaveSettings();
    }
    internal void SetDialogTransparency(double value)
    {
        value=NormalizeDialogTransparency(value);
        if(Math.Abs(DialogTransparency-value)<.01)return;
        DialogTransparency=value;DialogTransparencyChanged?.Invoke();SaveSettings();
    }
    internal sealed class Settings
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Height { get; set; } = 360;
        public double Speed { get; set; } = 1;
        public double DialogTransparency { get; set; } = 60;
        public bool MatteDialogs { get; set; } = true;
        public bool FollowMouse { get; set; } = false;
        public bool Topmost { get; set; } = true;
        public bool ReactToDesktop { get; set; } = false;
    }
}
