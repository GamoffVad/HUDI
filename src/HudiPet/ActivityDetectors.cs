namespace HudiPet;

internal sealed class TypingBurstDetector
{
    private long first,last;
    private int count;
    private bool emitted;
    internal void KeyDown(long now)
    {
        if(count==0||now-last>4000){first=now;count=0;emitted=false;}
        last=now;count++;
    }
    internal bool Poll(long now)
    {
        if(emitted||count<3||now-last>1600)return false;
        if(last-first>1600){count=1;first=last;return false;}
        emitted=true;return true;
    }
    internal void Reset(){first=last=0;count=0;emitted=false;}
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
