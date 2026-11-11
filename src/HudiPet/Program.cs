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
            if(args.Length==2&&args[0]=="--verify-sites")return FavoriteSitesVerification.Run(args[1]);
            if(args.Length==2&&args[0]=="--verify-appearance")return PetAppearanceVerification.Run(args[1]);
            if(args.Length==2&&args[0]=="--verify-conversation")return ConversationVerification.Run(args[1]);
            if(args.Length==2&&args[0]=="--verify-desktop-reactions")return DesktopReactionsVerification.Run(args[1]);
            if(args.Length==2&&args[0]=="--preview-sites")return FavoriteSitesVerification.Preview(args[1]);
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
    private bool pendingControl,pressedControl;
    internal TimeSpan ClickSequenceDelay=>clickSequence.Interval;
    internal object PointerGestureDiagnostics=>new
    {
        hasOrigin=dragOrigin is not null,dragging,pressedClickCount,pressedControl,pendingClicks,pendingControl,
        clickTimerRunning=clickSequence.IsEnabled,captureOwned=ReferenceEquals(Mouse.Captured,character)
    };
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    internal PetWindow(bool smokeTest, bool uiTest,PetCatalog? pets=null,FavoriteSitesStore? favoriteSites=null,ClickBindingsStore? clickBindings=null)
    {
        this.smokeTest = smokeTest;
        this.uiTest = uiTest;
        WaterOrbFrames.WarmUp();
        InitializePet(pets);
        Title = PetName+" — живой питомец";
        Height = 360 * library.Height / library.DisplayBounds.Height; Width = Height * library.Width / library.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = uiTest; ShowActivated = false;
        Content = character;
        InitializeChat();
        InitializeFavoriteSites(favoriteSites);
        InitializeClickBindings(clickBindings);
        clickSequence.Interval=TimeSpan.FromMilliseconds(GetDoubleClickTime()+20);
        clickSequence.Tick+=(_,_)=>CommitPetClicks();
        MouseRightButtonUp+=(_,e)=> { if(character.ContainsCharacter(e.GetPosition(character))) { CancelPetClicks();OpenMenu();e.Handled=true; } };
        MouseLeftButtonDown += (_, e) =>
        {
            if (!character.ContainsCharacter(e.GetPosition(character))) return;
            BeginPointerGesture(e.GetPosition(this),e.ClickCount,(Keyboard.Modifiers&ModifierKeys.Control)!=0);
        };
        MouseLeftButtonUp += (_, _) =>EndPointerGesture();
        LostMouseCapture+=(_,_)=>CancelPointerGesture();
        MouseMove += (_, e) =>
        {
            if(MovePointerGesture(e.GetPosition(this),e.LeftButton==MouseButtonState.Pressed))e.Handled=true;
        };
        MouseWheel += (_, e) => { CancelPetClicks();ResizePet(Height * (e.Delta > 0 ? 1.08 : 1 / 1.08));e.Handled=true; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) {if(favoriteSitesOrbit is not null)favoriteSitesOrbit.Dismiss();else Close();e.Handled=true;} };
        Loaded += (_, _) =>
        {
            RestoreSettings();
            RefreshDesktopReactions();
            previousTime = clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += Render;
        };
        Closed += (_, _) => { CompositionTarget.Rendering -= Render;clickSequence.Stop();DisposeDesktopReactions();DisposeChat();SaveSettings();if(!selectedPet.BuiltIn)library.Dispose(); };
    }

    internal void BeginPointerGesture(Point point,int clickCount,bool control)
    {
        desktopReactions.Clear();
        clickSequence.Stop();pressedClickCount=Math.Clamp(clickCount,1,3);pressedControl=control;
        dragOrigin=point;dragging=false;
        if(!character.CaptureMouse())CancelPointerGesture();
    }
    internal bool MovePointerGesture(Point point,bool leftPressed)
    {
        if(dragOrigin is not {} start||!leftPressed)return false;
        if(!dragging)
        {
            if(Math.Abs(point.X-start.X)+Math.Abs(point.Y-start.Y)<4)return false;
            dragging=true;CancelPetClicks();
        }
        // Keep the grabbed body point under the pointer. Native DragMove
        // moves the whole padded window and can constrain its empty edges.
        Left+=point.X-start.X;Top+=point.Y-start.Y;ClampToScreen();return true;
    }
    internal void EndPointerGesture()
    {
        int clicks=dragOrigin is not null&&!dragging?pressedClickCount:0;
        bool wasDragging=dragging,control=pressedControl;
        dragging=false;dragOrigin=null;pressedClickCount=0;pressedControl=false;
        // Clear gesture state before releasing capture: normal release must
        // not cancel the click queued immediately below.
        character.ReleaseMouseCapture();
        if(wasDragging){ClampToScreen();SaveSettings();}
        if(clicks>0)QueuePetClick(clicks,control);
    }
    private void CancelPointerGesture()
    {
        if(dragOrigin is null&&!dragging)return;
        bool wasDragging=dragging;dragging=false;dragOrigin=null;CancelPetClicks();
        if(wasDragging){ClampToScreen();SaveSettings();}
    }

    internal PetAction? TriggerRandomClickAction()
    {
        CancelPetClicks();dragOrigin=null;character.ReleaseMouseCapture();
        paused=false;clickReactions++;
        return behavior.TriggerRandomAction();
    }

    private void CancelPetClicks()
    {
        clickSequence.Stop();pendingClicks=0;pressedClickCount=0;pendingControl=pressedControl=false;
    }
    internal void QueuePetClick(int count,bool control=false)
    {
        CancelPetClicks();
        int clicks=Math.Clamp(count,1,3);
        if(ClickBindings.Get(clicks,control)==PetClickAction.None)return;
        pendingClicks=clicks;pendingControl=control;
        if(ClickBindings.HasLaterAction(pendingClicks,control))clickSequence.Start();
        else CommitPetClicks();
    }
    private void CommitPetClicks()
    {
        int count=pendingClicks;bool control=pendingControl;CancelPetClicks();
        switch(ClickBindings.Get(count,control))
        {
            case PetClickAction.Chat:OpenChat(true);break;
            case PetClickAction.Notes:OpenNotes();break;
            case PetClickAction.FavoriteSites:ToggleFavoriteSites();break;
        }
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
        UpdateDesktopReactions();
        if (!paused)
        {
            character.Apply(behavior.Update(dt * speed, autonomous: !UserActionsOnly));
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
        double ratio=library.DisplayBounds.Height/library.Height;
        double displayHeight=Math.Clamp(height*ratio,180,Math.Max(180,Math.Min(720,SystemParameters.VirtualScreenHeight)));
        var display=DisplayedBounds;
        SetWindowBounds(WindowForDisplay(new(display.X,display.Bottom-displayHeight,display.Width,displayHeight),new(library.Width,library.Height),library.DisplayBounds));
        ClampToScreen(); SaveSettings();
    }

    internal double DisplayHeight=>Height*library.DisplayBounds.Height/library.Height;
    internal Rect DisplayedBounds
    {
        get
        {
            double scale=Height/library.Height;var bounds=library.DisplayBounds;
            return new(Left+bounds.X*scale,Top+bounds.Y*scale,bounds.Width*scale,bounds.Height*scale);
        }
    }
    private void SetWindowBounds(Rect bounds){Height=bounds.Height;Width=bounds.Width;Left=bounds.Left;Top=bounds.Top;}
    internal static Rect WindowForDisplay(Rect display,Size canvas,Rect viewport)
    {
        double scale=display.Height/viewport.Height;
        return new(display.Left+display.Width/2-(viewport.X+viewport.Width/2)*scale,
            display.Bottom-viewport.Bottom*scale,canvas.Width*scale,canvas.Height*scale);
    }
    internal static Rect RestoreWindowBounds(Settings settings,bool builtIn,Size canvas,Rect viewport)
    {
        bool savedViewport=settings.CanvasWidth>0&&settings.CanvasHeight>0&&
            double.IsFinite(settings.DisplayX)&&double.IsFinite(settings.DisplayY)&&double.IsFinite(settings.DisplayWidth)&&double.IsFinite(settings.DisplayHeight)&&
            settings.DisplayX>=0&&settings.DisplayY>=0&&settings.DisplayWidth>0&&settings.DisplayHeight>0&&
            settings.DisplayX+settings.DisplayWidth<=settings.CanvasWidth&&settings.DisplayY+settings.DisplayHeight<=settings.CanvasHeight;
        // Settings without canvas metadata predate the padded built-in pack.
        var previousCanvas=savedViewport?new Size(settings.CanvasWidth,settings.CanvasHeight):builtIn?new Size(384,500):canvas;
        var previousViewport=savedViewport?new Rect(settings.DisplayX,settings.DisplayY,settings.DisplayWidth,settings.DisplayHeight):builtIn?new Rect(0,0,384,500):viewport;
        double displayHeight=double.IsFinite(settings.Height)&&settings.Height>0?settings.Height*previousViewport.Height/previousCanvas.Height:360;
        displayHeight=Math.Clamp(displayHeight,180,720);
        double scale=displayHeight/previousViewport.Height;
        var display=new Rect(settings.Left+previousViewport.X*scale,settings.Top+previousViewport.Y*scale,previousViewport.Width*scale,displayHeight);
        return WindowForDisplay(display,canvas,viewport);
    }

    private void RestoreSettings()
    {
        restoring = true;
        var area = SystemParameters.WorkArea;
        double displayWidth=DisplayHeight*library.DisplayBounds.Width/library.DisplayBounds.Height;
        SetWindowBounds(WindowForDisplay(new(area.Right-displayWidth-30,area.Bottom-DisplayHeight-5,displayWidth,DisplayHeight),new(library.Width,library.Height),library.DisplayBounds));
        if (!smokeTest && !uiTest && File.Exists(SettingsPath))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath));
                if (settings is not null && double.IsFinite(settings.Left) && double.IsFinite(settings.Top))
                {
                    SetWindowBounds(RestoreWindowBounds(settings,selectedPet.BuiltIn,new(library.Width,library.Height),library.DisplayBounds));
                    speed = double.IsFinite(settings.Speed) ? Math.Clamp(settings.Speed, .5, 1.5) : 1;
                    Topmost = settings.Topmost;
                    DialogTransparency = NormalizeDialogTransparency(settings.DialogTransparency);
                    MatteDialogs = settings.MatteDialogs;
                }
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        }
        restoring = false; ClampToScreen();SaveSettings();
    }

    private void ClampToScreen()
    {
        var screen=new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight);
        var bounds=ClampWindowToScreen(new(Left,Top,Width,Height),new(library.Width,library.Height),library.DisplayBounds,screen);
        Left=bounds.Left;Top=bounds.Top;
    }
    internal static Rect ClampWindowToScreen(Rect window,Size canvas,Rect viewport,Rect screen)
    {
        double scale=window.Height/canvas.Height;
        var display=new Rect(window.Left+viewport.Left*scale,window.Top+viewport.Top*scale,viewport.Width*scale,viewport.Height*scale);
        double x=Math.Clamp(display.Left,screen.Left,Math.Max(screen.Left,screen.Right-display.Width));
        double y=Math.Clamp(display.Top,screen.Top,Math.Max(screen.Top,screen.Bottom-display.Height));
        // Only the body is constrained; the unchanged transparent canvas may
        // extend beyond the virtual desktop on any side.
        window.Offset(x-display.Left,y-display.Top);return window;
    }

    private void SaveSettings()
    {
        if (smokeTest || uiTest || restoring || !IsLoaded) return;
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings { Left = Left, Top = Top, Height = Height, Speed = speed, Topmost = Topmost, DialogTransparency = DialogTransparency, MatteDialogs = MatteDialogs,
                PetId=selectedPet.Id,CanvasWidth=library.Width,CanvasHeight=library.Height,DisplayX=library.DisplayBounds.X,DisplayY=library.DisplayBounds.Y,DisplayWidth=library.DisplayBounds.Width,DisplayHeight=library.DisplayBounds.Height }));
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
        public string? PetId { get; set; }
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }
        public double DisplayX { get; set; }
        public double DisplayY { get; set; }
        public double DisplayWidth { get; set; }
        public double DisplayHeight { get; set; }
    }
}
