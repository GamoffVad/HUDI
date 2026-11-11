using System.Windows;

namespace HudiPet;

internal sealed partial class PetWindow
{
    private string? edgeDragPet;
    private long edgeDragExpires;
    private PetAction nextEdgeAction = PetAction.EdgeLean;
    private (PetAction Action, double Left, DesktopEdgeCue Cue)? edgePlayback;

    private bool CanPlaceDesktopEdge(PetAction action, out Point placement)
    {
        placement = new(Left, Top);
        if (!library.DesktopEdges.TryGetValue(PetBehavior.ClipName(action), out var cue)) return false;
        var work = ScreenPlacement.WorkArea(this);
        var body = DisplayedBounds;
        double scale = Height / library.Height;
        if (!cue.TryPlace(work.Left, work.Right, Left, scale, body.Right, body.Width, 56, out double left) ||
            !cue.TryFitVertical(work.Top, work.Bottom, Top, scale, body.Top, body.Bottom, 32, out double top)) return false;
        placement = new(left, top);
        return true;
    }

    private bool PlayDesktopEdge(PetAction action)
    {
        if (behavior.IsPlaying || chat.Busy || !CanPlaceDesktopEdge(action, out var placement)) return false;
        edgeDragPet = null;
        Left = placement.X;
        Top = placement.Y;
        edgePlayback = (action, placement.X, library.DesktopEdges[PetBehavior.ClipName(action)]);
        paused = false;
        behavior.Trigger(action);
        SaveSettings();
        return true;
    }

    private void ApplyDesktopEdgePadding()
    {
        if (edgePlayback is not { } edge) return;
        bool active = behavior.CurrentAction == edge.Action;
        double left = edge.Left - edge.Cue.PaddingAt(active ? behavior.CurrentStepIndex : -1) * (Height / library.Height);
        if (Math.Abs(Left - left) > .01) Left = left;
        if (!active) edgePlayback = null;
    }

    private void QueueDesktopEdgeAfterDrag()
    {
        long now = Environment.TickCount64;
        if (!DesktopReactionsEnabled ||
            (!CanPlaceDesktopEdge(PetAction.EdgeLean, out _) && !CanPlaceDesktopEdge(PetAction.EdgePeek, out _))) return;
        edgeDragPet = selectedPet.Id;
        edgeDragExpires = now + 10000;
    }

    private void UpdateDesktopEdge()
    {
        if (edgeDragPet is null) return;
        if (edgeDragPet != selectedPet.Id || Environment.TickCount64 > edgeDragExpires || DesktopInteractionBlocked)
        {
            edgeDragPet = null;
            return;
        }
        if (behavior.IsPlaying) return;
        PetAction alternate = nextEdgeAction == PetAction.EdgeLean ? PetAction.EdgePeek : PetAction.EdgeLean;
        if (PlayDesktopEdge(nextEdgeAction)) nextEdgeAction = alternate;
        else PlayDesktopEdge(alternate);
        edgeDragPet = null;
    }
}
