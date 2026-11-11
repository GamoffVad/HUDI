namespace HudiPet;

internal sealed class InputBurstDetector
{
    private const long QuietPeriod = 700;
    private long? last;
    private bool pending;

    internal void Observe(long now)
    {
        if (last is null || now - last.Value >= QuietPeriod) pending = true;
        last = now;
    }

    internal bool Take()
    {
        bool result = pending;
        pending = false;
        return result;
    }

    internal void Reset() { last = null; pending = false; }
}

internal sealed class IdleActivityDetector
{
    private bool waiting;
    internal DesktopActivity? Observe(double seconds)
    {
        if(!waiting&&seconds>=90){waiting=true;return DesktopActivity.IdleWaiting;}
        if(waiting&&seconds<3){waiting=false;return DesktopActivity.ReturnedToWork;}
        return null;
    }
}
