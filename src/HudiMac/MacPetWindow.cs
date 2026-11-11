using System.Diagnostics;
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
    private readonly DispatcherTimer activityTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
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
    private PixelPoint initialPosition;
    private bool dragging, modifiedClick;
    private int clickCount;
    private PetClickAction queuedClick;
    private double previousTime;
    private static string PlacementPath => Path.Combine(AppStorage.Directory, "mac-window.json");
    private sealed record Placement(int X, int Y, double Height, bool Topmost);

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
            visual.Pose = behavior.Update(now - previousTime, autonomous: !userActionsOnly);
            previousTime = now;
        };
        activityTimer.Tick += (_, _) =>
        {
            if (desktopActivity is null) return;
            desktopActivity.Poll(activity =>
            {
                if (press is not null || chat.Busy || orbit.Count > 0 || OwnedWindows.Any(window => window.IsVisible)) return;
                if (!userActionsOnly || activity != DesktopActivity.IdleWaiting)
                    reactions.Offer(activity, Environment.TickCount64);
            });
            if (press is not null || chat.Busy || orbit.Count > 0 || OwnedWindows.Any(window => window.IsVisible))
                reactions.Clear();
            else if (reactions.Take(Environment.TickCount64, behavior.IsPlaying, behavior.Supports) is { } action)
                behavior.Trigger(action);
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
        visual.PointerCaptureLost += (_, _) => { press = null; dragging = false; };
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
            success = actions > 0 && transparent && first.Width == library.Width && first.Height == library.Height && userActionsOnly;
            File.WriteAllText(report, JsonSerializer.Serialize(new { success, transparent, actions, userActionsOnly,
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

    private void ClampToScreen()
    {
        var screen = Screens.ScreenFromPoint(BodyBottomCenter()) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var body = library.DisplayBounds;
        var work = screen.WorkingArea;
        double scale = Height / library.Height * RenderScaling;
        int left = (int)Math.Round(body.X * scale), top = (int)Math.Round(body.Y * scale);
        int width = (int)Math.Round(body.Width * scale), height = (int)Math.Round(body.Height * scale);
        Position = new(Math.Clamp(Position.X + left, work.X, Math.Max(work.X, work.Right - width)) - left,
            Math.Clamp(Position.Y + top, work.Y, Math.Max(work.Y, work.Bottom - height)) - top);
    }

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
                Topmost = saved.Topmost;
                ClampToScreen();
                return;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        if (Screens.Primary is { } screen)
            AlignFeet(new(screen.WorkingArea.Right - (int)(150 * screen.Scaling), screen.WorkingArea.Bottom - 10));
        ClampToScreen();
    }

    private void SavePlacement()
    {
        try
        {
            Directory.CreateDirectory(AppStorage.Directory);
            string pending = PlacementPath + ".pending";
            File.WriteAllText(pending, JsonSerializer.Serialize(new Placement(Position.X, Position.Y, BodyHeight, Topmost)));
            File.Move(pending, PlacementPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void BeginGesture(object? sender, PointerPressedEventArgs e)
    {
        Point point = e.GetPosition(visual);
        if (!visual.Hit(point)) return;
        var buttons = e.GetCurrentPoint(visual).Properties;
        if (buttons.IsRightButtonPressed) { BuildMenu().Open(visual); return; }
        if (!buttons.IsLeftButtonPressed) return;
        clickTimer.Stop();
        queuedClick = PetClickAction.None;
        press = point;
        initialPosition = Position;
        dragging = false;
        clickCount = Math.Clamp(e.ClickCount, 1, 3);
        modifiedClick = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0;
        e.Pointer.Capture(visual);
    }

    private void MoveGesture(object? sender, PointerEventArgs e)
    {
        if (press is not { } start) return;
        var point = e.GetPosition(visual);
        var screenPoint = visual.PointToScreen(point);
        double x = screenPoint.X - initialPosition.X - start.X * RenderScaling;
        double y = screenPoint.Y - initialPosition.Y - start.Y * RenderScaling;
        if (!dragging && Math.Abs(x) + Math.Abs(y) < 5) return;
        dragging = true;
        Position = new(initialPosition.X + (int)Math.Round(x), initialPosition.Y + (int)Math.Round(y));
        ClampToScreen();
        PlaceOrbit();
    }

    private void EndGesture(object? sender, PointerReleasedEventArgs e)
    {
        if (press is null) return;
        bool moved = dragging;
        press = null;
        e.Pointer.Capture(null);
        if (moved) { SavePlacement(); return; }
        queuedClick = bindings.Get(clickCount, modifiedClick);
        if (queuedClick == PetClickAction.None) return;
        if (bindings.HasLaterAction(clickCount, modifiedClick)) clickTimer.Start();
        else CommitClick();
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
        if (pet == selected) return;
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
            appearance = theme;
            userActionsOnly = pets.UserActionsOnly(pet);
            RefreshActivity();
            behavior = new(sprites: next);
            visual.SetLibrary(next);
            chat.ClearHistory();
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
