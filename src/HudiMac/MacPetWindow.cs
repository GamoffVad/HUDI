using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace HudiPet;

internal sealed partial class MacPetWindow : Window
{
    private readonly PetCatalog pets = new();
    private readonly ClickBindingsStore controls = new();
    private readonly MacPetView visual;
    private readonly DispatcherTimer playback = new() { Interval = TimeSpan.FromMilliseconds(1000d / 60) };
    private readonly DispatcherTimer clickTimer = new() { Interval = TimeSpan.FromMilliseconds(520) };
    private readonly DispatcherTimer activityTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private DesktopReactionQueue reactions = new();
    private MacDesktopActivity? desktopActivity;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly HttpModelClient modelClient = new();
    private readonly ChatSession chat;
    private LocalPet selected;
    private SpriteLibrary library;
    private PetBehavior behavior;
    private PetAppearance appearance;
    private ClickBindings bindings;
    private bool userActionsOnly, changingPet, closed;
    private Point? press;
    private PointerPressedEventArgs? nativeDragStart;
    private bool nativeDragInProgress;
    private PixelPoint lastNativeDragPosition;
    private PixelPoint initialPosition;
    private PixelPoint initialPointer;
    private bool dragging, modifiedClick;
    private int clickCount;
    private PetClickAction queuedClick;
    private double previousTime;
    private bool preferTopmost = true, matteDialogs = true;
    private double dialogTransparency = 15, playbackSpeed = 1;
    private double DesktopUnitScale => OperatingSystem.IsMacOS() ? 1 : RenderScaling;
    private static string PlacementPath => Path.Combine(AppStorage.Directory, "mac-window.json");
    private sealed record Placement(int X, int Y, double Height, bool Topmost)
    {
        public double Transparency { get; init; } = 15;
        public bool Matte { get; init; } = true;
        public double Speed { get; init; } = 1;
    }

    internal MacPetWindow()
    {
        selected = pets.Selected();
        try
        {
            library = pets.Open(selected);
            library.Get(library.NeutralFrame);
        }
        catch (Exception error) when (!selected.BuiltIn && error is IOException or InvalidDataException or JsonException)
        {
            selected = PetCatalog.Hudi;
            library = pets.Open(selected);
        }
        appearance = pets.Appearance(selected);
        userActionsOnly = pets.UserActionsOnly(selected);
        try { bindings = controls.Read(); }
        catch (Exception error) when (ClickBindingsStore.IsDataError(error)) { bindings = new(); }
        behavior = new(sprites: library);
        chat = new(modelClient);
        chat.Started += () => behavior.BeginChatThinking();
        chat.Finished += success => behavior.EndChatThinking(success);
        Title = selected.Name + " — живой питомец";
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowActivated = false;
        CanResize = false;
        visual = new(library);
        Content = visual;
        ResizeBody(300);
        clickTimer.Tick += (_, _) => CommitClick();
        playback.Tick += (_, _) =>
        {
            double now = clock.Elapsed.TotalSeconds;
            UpdateNativeDrag();
            UpdateDesktopEdge();
            UpdateActivityReactions();
            visual.Pose = behavior.Update((now - previousTime) * playbackSpeed, autonomous: !userActionsOnly);
            ApplyDesktopEdgePadding();
            previousTime = now;
        };
        activityTimer.Tick += (_, _) =>
        {
            if (desktopActivity is null) return;
            desktopActivity.Poll(activity =>
            {
                if (ActivityBlocked) return;
                if (!userActionsOnly || activity != DesktopActivity.IdleWaiting)
                    reactions.Offer(activity);
            });
        };
        Opened += (_, _) => { RestorePlacement(); RefreshActivity(); previousTime = clock.Elapsed.TotalSeconds; playback.Start(); };
        Closed += (_, _) =>
        {
            closed = true;
            playback.Stop();
            clickTimer.Stop();
            activityTimer.Stop();
            CloseOrbit();
            SavePlacement();
            chat.Dispose();
            modelClient.Dispose();
            if (!selected.BuiltIn) library.Dispose();
        };
        visual.PointerPressed += BeginGesture;
        visual.PointerMoved += MoveGesture;
        visual.PointerReleased += EndGesture;
        visual.PointerCaptureLost += (_, _) => { press = null; nativeDragStart = null; dragging = false; };
        visual.PointerWheelChanged += (_, e) =>
        {
            if (!visual.Hit(e.GetPosition(visual))) return;
            var feet = BodyBottomCenter();
            ResizeBody(BodyHeight * (e.Delta.Y > 0 ? 1.08 : 1 / 1.08));
            AlignFeet(feet);
            ClampToScreen();
            SavePlacement();
            e.Handled = true;
        };
    }

    private bool ActivityBlocked => press is not null || nativeDragInProgress || queuedClick != PetClickAction.None || chat.Busy || orbit.Count > 0
        || OwnedWindows.Any(window => window.IsVisible);

    private void UpdateActivityReactions()
    {
        if (desktopActivity is null || reactions.PendingCount == 0) return;
        if (ActivityBlocked) { reactions.Clear(); return; }
        if (reactions.Take(behavior.IsPlaying || behavior.ChatThinking, action => behavior.Supports(action)
            && (!PetBehavior.IsEdgeAction(action) || CanPlaceDesktopEdge(action, out _))) is not { } reaction) return;
        if (PetBehavior.IsEdgeAction(reaction)) PlayDesktopEdge(reaction);
        else behavior.Trigger(reaction);
    }

    private void RefreshActivity()
    {
        reactions = new();
        activityTimer.Stop();
        desktopActivity = OperatingSystem.IsMacOS() && pets.DesktopReactionsEnabled(selected) ? new() : null;
        if (desktopActivity is not null) activityTimer.Start();
    }

    internal async void VerifyRuntime(string report, IClassicDesktopStyleApplicationLifetime app)
    {
        bool success = false;
        try
        {
            await Task.Delay(5500);
            int actions = behavior.ActionsStarted;
            var first = library.Get(library.NeutralFrame).PixelSize;
            bool transparent = ActualTransparencyLevel == WindowTransparencyLevel.Transparent;
            pets.SetUserActionsOnly(selected, true);
            userActionsOnly = pets.UserActionsOnly(selected);
            OpenSettings();
            await Task.Delay(100);
            var controls = interactivePanels.Single(panel => panel.Title == "Управление кликами");
            bool controlsInFront = !Topmost && controls.Topmost == preferTopmost && controls.IsVisible
                && controls.SystemDecorations == SystemDecorations.None;
            var work = new PixelRect(0, 25, 1000, 650);
            var petRight = new PixelRect(850, 320, 150, 240);
            var panelSize = new PixelSize(380, 380);
            var placed = new PixelRect(PlacePanel(work, petRight, panelSize, 12), panelSize);
            bool placementFits = placed.X >= work.X && placed.Y >= work.Y && placed.Right <= petRight.X
                && placed.Bottom <= work.Bottom;
            var secondary = new PixelRect(-1440, 26, 1440, 774);
            var body = new PixelRect(34, 26, 118, 240);
            var dropped = ClampVisibleBody(secondary, new PixelPoint(-1560, 795), body);
            bool silhouetteFitsDropScreen = dropped.X + body.X == secondary.X && dropped.Y + body.Bottom == secondary.Bottom;
            controls.Close();
            await Task.Delay(50);
            bool restoredLayer = Topmost == preferTopmost && interactivePanels.Count == 0;
            success = actions > 0 && transparent && first.Width == library.Width && first.Height == library.Height
                && userActionsOnly && controlsInFront && placementFits && silhouetteFitsDropScreen && restoredLayer;
            File.WriteAllText(report, JsonSerializer.Serialize(new { success, transparent, actions, userActionsOnly,
                controlsInFront, placementFits, silhouetteFitsDropScreen, restoredLayer,
                frameWidth = first.Width, frameHeight = first.Height, os = Environment.OSVersion.Platform.ToString() }));
        }
        catch (Exception error)
        {
            File.WriteAllText(report, JsonSerializer.Serialize(new { success, error = error.ToString() }));
        }
        finally { app.Shutdown(success ? 0 : 1); }
    }

    private double BodyHeight => Height * library.DisplayBounds.Height / library.Height;
    private void ResizeBody(double height)
    {
        edgePlayback = null;
        double scale = Math.Clamp(height, 100, 720) / library.DisplayBounds.Height;
        Width = library.Width * scale;
        Height = library.Height * scale;
    }

    private PixelPoint BodyBottomCenter()
    {
        var body = library.DisplayBounds;
        double scale = Height / library.Height;
        return this.PointToScreen(new((body.X + body.Width / 2) * scale, (body.Y + body.Height) * scale));
    }

    private void AlignFeet(PixelPoint feet)
    {
        var current = BodyBottomCenter();
        Position = new(Position.X + feet.X - current.X, Position.Y + feet.Y - current.Y);
    }

    private void ClampToScreen(PixelPoint? pointer = null)
    {
        var screen = (pointer is { } point ? Screens.ScreenFromPoint(point) : null)
            ?? Screens.ScreenFromPoint(BodyBottomCenter()) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var body = library.DisplayBounds;
        double scale = Height / library.Height * (OperatingSystem.IsMacOS() ? 1 : screen.Scaling);
        int left = (int)Math.Floor(body.X * scale), top = (int)Math.Floor(body.Y * scale);
        int right = (int)Math.Ceiling((body.X + body.Width) * scale);
        int bottom = (int)Math.Ceiling((body.Y + body.Height) * scale);
        Position = ClampVisibleBody(screen.WorkingArea, Position, new PixelRect(left, top, right - left, bottom - top));
    }

    private static PixelPoint ClampVisibleBody(PixelRect work, PixelPoint position, PixelRect body) => new(
        Math.Clamp(position.X + body.X, work.X, Math.Max(work.X, work.Right - body.Width)) - body.X,
        Math.Clamp(position.Y + body.Y, work.Y, Math.Max(work.Y, work.Bottom - body.Height)) - body.Y);

    private void RestorePlacement()
    {
        try
        {
            if (File.Exists(PlacementPath) &&
                JsonSerializer.Deserialize<Placement>(File.ReadAllText(PlacementPath)) is { } saved &&
                double.IsFinite(saved.Height))
            {
                ResizeBody(saved.Height);
                Position = new(saved.X, saved.Y);
                preferTopmost = saved.Topmost;
                if (double.IsFinite(saved.Transparency)) dialogTransparency = Math.Clamp(saved.Transparency, 0, 90);
                matteDialogs = saved.Matte;
                if (double.IsFinite(saved.Speed)) playbackSpeed = Math.Clamp(saved.Speed, .75, 1.3);
                RefreshPetLayer();
                ClampToScreen();
                return;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        if (Screens.Primary is { } screen)
            AlignFeet(new(screen.WorkingArea.Right - (int)(150 * DesktopUnitScale), screen.WorkingArea.Bottom - 10));
        ClampToScreen();
    }

    private void SavePlacement()
    {
        try
        {
            Directory.CreateDirectory(AppStorage.Directory);
            string pending = PlacementPath + ".pending";
            File.WriteAllText(pending, JsonSerializer.Serialize(new Placement(Position.X, Position.Y, BodyHeight, preferTopmost)
            { Transparency = dialogTransparency, Matte = matteDialogs, Speed = playbackSpeed }));
            File.Move(pending, PlacementPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void BeginGesture(object? sender, PointerPressedEventArgs e)
    {
        Point point = e.GetPosition(visual);
        if (!visual.Hit(point)) return;
        var buttons = e.GetCurrentPoint(visual).Properties;
        if (buttons.IsRightButtonPressed) { OpenMenu(); e.Handled = true; return; }
        if (!buttons.IsLeftButtonPressed) return;
        edgeDragPet = null;
        edgePlayback = null;
        clickTimer.Stop();
        queuedClick = PetClickAction.None;
        press = point;
        nativeDragStart = OperatingSystem.IsMacOS() ? e : null;
        initialPosition = Position;
        initialPointer = visual.PointToScreen(point);
        dragging = false;
        clickCount = Math.Clamp(e.ClickCount, 1, 3);
        modifiedClick = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0;
        e.Pointer.Capture(visual);
    }

    private void MoveGesture(object? sender, PointerEventArgs e)
    {
        if (press is not { } start) return;
        Point point = e.GetPosition(visual);
        if (nativeDragStart is { } mouseDown)
        {
            if (Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y) < 5) return;
            press = null;
            nativeDragStart = null;
            dragging = false;
            e.Pointer.Capture(null);
            nativeDragInProgress = true;
            lastNativeDragPosition = Position;
            // AppKit hands the drag to the window server and may consume the release event.
            BeginMoveDrag(mouseDown);
            e.Handled = true;
            return;
        }
        var screenPoint = visual.PointToScreen(point);
        int x = screenPoint.X - initialPointer.X;
        int y = screenPoint.Y - initialPointer.Y;
        if (!dragging && Math.Abs(x) + Math.Abs(y) < 5) return;
        dragging = true;
        Position = new(initialPosition.X + x, initialPosition.Y + y);
        PlaceOrbit();
    }

    private void EndGesture(object? sender, PointerReleasedEventArgs e)
    {
        if (press is null) return;
        bool moved = dragging;
        var screenPoint = visual.PointToScreen(e.GetPosition(visual));
        press = null;
        nativeDragStart = null;
        dragging = false;
        e.Pointer.Capture(null);
        if (moved)
        {
            FinishDrag(screenPoint);
            return;
        }
        queuedClick = bindings.Get(clickCount, modifiedClick);
        if (queuedClick == PetClickAction.None) return;
        if (bindings.HasLaterAction(clickCount, modifiedClick)) clickTimer.Start();
        else CommitClick();
    }

    private void UpdateNativeDrag()
    {
        if (!nativeDragInProgress) return;
        if (MacDrag.LeftButtonDown())
        {
            if (lastNativeDragPosition != Position)
            {
                lastNativeDragPosition = Position;
                PlaceOrbit();
            }
            return;
        }
        nativeDragInProgress = false;
        FinishDrag(MacDrag.CursorPosition());
    }

    private void FinishDrag(PixelPoint? pointer = null)
    {
        ClampToScreen(pointer);
        PlaceOrbit();
        SavePlacement();
        QueueDesktopEdgeAfterDrag();
    }

    private static class MacDrag
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const int CombinedSessionState = 0;
        private const uint LeftButton = 0;

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct CursorPoint
        {
            internal readonly double X;
            internal readonly double Y;
        }

        internal static bool LeftButtonDown() => CGEventSourceButtonState(CombinedSessionState, LeftButton);

        internal static PixelPoint? CursorPosition()
        {
            var mouse = CGEventCreate(IntPtr.Zero);
            if (mouse == IntPtr.Zero) return null;
            try
            {
                var point = CGEventGetLocation(mouse);
                return new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));
            }
            finally { CFRelease(mouse); }
        }

        [DllImport(CoreGraphics)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGEventSourceButtonState(int state, uint button);
        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(CoreGraphics)] private static extern CursorPoint CGEventGetLocation(IntPtr mouse);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
    }

    private void CommitClick()
    {
        clickTimer.Stop();
        var action = queuedClick;
        queuedClick = PetClickAction.None;
        switch (action)
        {
            case PetClickAction.FavoriteSites: ToggleOrbit(); break;
            case PetClickAction.Notes: OpenNotes(); break;
            case PetClickAction.Chat: OpenChat(); break;
        }
    }

    private async Task SelectPet(LocalPet pet)
    {
        if (changingPet || chat.Busy) return;
        if (pet.SameRevision(selected)) return;
        bool samePet = pet.Id == selected.Id;
        changingPet = true;
        SpriteLibrary? next = null;
        try
        {
            next = pets.Open(pet);
            next.Get(next.NeutralFrame);
            var theme = pets.Appearance(pet);
            pets.Select(pet);
            var feet = BodyBottomCenter();
            double height = BodyHeight;
            CloseOrbit();
            if (!selected.BuiltIn) library.Dispose();
            library = next;
            selected = pet;
            edgeDragPet = null;
            edgePlayback = null;
            appearance = theme;
            RefreshPanelTheme();
            userActionsOnly = pets.UserActionsOnly(pet);
            RefreshActivity();
            behavior = new(sprites: next);
            visual.SetLibrary(next);
            if (!samePet) chat.ClearHistory();
            Title = pet.Name + " — живой питомец";
            ResizeBody(height);
            AlignFeet(feet);
            ClampToScreen();
            SavePlacement();
        }
        catch (Exception error)
        {
            if (next is not null && !pet.BuiltIn && next != library) next.Dispose();
            if (!closed) await ShowError(error.Message);
        }
        finally { changingPet = false; }
    }
}

internal sealed class MacPetView(SpriteLibrary library) : Control
{
    private SpriteLibrary sprites = library;
    private PetPose pose = new(library.NeutralFrame);
    internal PetPose Pose
    {
        set { if (pose != value) { pose = value; InvalidateVisual(); } }
    }
    internal void SetLibrary(SpriteLibrary next)
    {
        sprites = next;
        pose = new(next.NeutralFrame);
        InvalidateVisual();
    }
    internal bool Hit(Point point)
    {
        var body = sprites.DisplayBounds;
        double scale = Bounds.Height / sprites.Height;
        return new Rect(body.X * scale, body.Y * scale, body.Width * scale, body.Height * scale).Contains(point);
    }
    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, rect);
        if (pose.BlendFrom is { } previous)
        {
            using var fade = context.PushOpacity(1 - pose.Blend);
            context.DrawImage(sprites.Get(previous), rect);
        }
        using (context.PushOpacity(pose.Blend))
            context.DrawImage(sprites.Get(pose.Frame), rect);
    }
}
