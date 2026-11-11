using Avalonia;

namespace HudiPet;

internal sealed partial class MacPetWindow
{
    private string? edgeDragPet;
    private long edgeDragExpires;
    private PetAction nextEdgeAction = PetAction.EdgeLean;
    private (PetAction Action, int X, DesktopEdgeCue Cue)? edgePlayback;

    private bool CanPlaceDesktopEdge(PetAction action, out PixelPoint placement)
    {
        placement = Position;
        if (!library.DesktopEdges.TryGetValue(PetBehavior.ClipName(action), out var cue)) return false;
        var screen = Screens.ScreenFromPoint(BodyBottomCenter()) ?? Screens.ScreenFromWindow(this);
        if (screen is null) return false;
        var work = screen.WorkingArea;
        double scale = Height / library.Height * DesktopUnitScale;
        var body = library.DisplayBounds;
        if (!cue.TryPlace(work.X, work.Right, Position.X, scale, Position.X + (body.X + body.Width) * scale,
                body.Width * scale, 56 * DesktopUnitScale, out double left)) return false;
        if (!cue.TryFitVertical(work.Y, work.Bottom, Position.Y, scale, Position.Y + body.Y * scale,
                Position.Y + (body.Y + body.Height) * scale, 32 * DesktopUnitScale, out double top)) return false;
        int y = (int)(top < Position.Y ? Math.Floor(top) : Math.Ceiling(top));
        placement = new((int)Math.Floor(left), y);
        return true;
    }

    private bool PlayDesktopEdge(PetAction action)
    {
        if (behavior.IsPlaying || chat.Busy || !CanPlaceDesktopEdge(action, out var placement)) return false;
        edgeDragPet = null;
        Position = placement;
        edgePlayback = (action, placement.X, library.DesktopEdges[PetBehavior.ClipName(action)]);
        behavior.Trigger(action);
        SavePlacement();
        return true;
    }

    private void ApplyDesktopEdgePadding()
    {
        if (edgePlayback is not { } edge) return;
        bool active = behavior.CurrentAction == edge.Action;
        double padding = edge.Cue.PaddingAt(active ? behavior.CurrentStepIndex : -1);
        int x = edge.X - (int)Math.Ceiling(padding * Height / library.Height * DesktopUnitScale);
        if (x != Position.X) Position = new PixelPoint(x, Position.Y);
        if (!active) edgePlayback = null;
    }

    private void QueueDesktopEdgeAfterDrag()
    {
        long now = Environment.TickCount64;
        if (!pets.DesktopReactionsEnabled(selected) ||
            (!CanPlaceDesktopEdge(PetAction.EdgeLean, out _) && !CanPlaceDesktopEdge(PetAction.EdgePeek, out _))) return;
        edgeDragPet = selected.Id;
        edgeDragExpires = now + 10000;
    }

    private void UpdateDesktopEdge()
    {
        if (edgeDragPet is null) return;
        if (edgeDragPet != selected.Id || Environment.TickCount64 > edgeDragExpires || press is not null || chat.Busy ||
            OwnedWindows.Any(window => window.IsVisible))
        {
            edgeDragPet = null;
            return;
        }
        if (behavior.IsPlaying) return;
        var alternate = nextEdgeAction == PetAction.EdgeLean ? PetAction.EdgePeek : PetAction.EdgeLean;
        if (PlayDesktopEdge(nextEdgeAction)) nextEdgeAction = alternate;
        else PlayDesktopEdge(alternate);
        edgeDragPet = null;
    }
}
