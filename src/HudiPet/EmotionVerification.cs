using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HudiPet;

// Checks the approved additions through the same decoder, renderer and behavior
// used by the installed pet. This complements the source-video framing audit.
internal static class EmotionVerification
{
    internal static readonly PetAction[] Actions=
        [PetAction.Salute,PetAction.March,PetAction.Notebook,PetAction.Fire,PetAction.FireCalm];

    internal static Dictionary<string,bool> Run(SpriteLibrary library,string directory)
    {
        Directory.CreateDirectory(directory);
        var checks=new Dictionary<string,bool>();
        var metrics=new List<object>();
        foreach(var action in Actions)
        {
            string clipName=PetBehavior.ClipName(action);
            bool found=library.Clips.TryGetValue(clipName,out var clip);
            checks[$"{clipName}_approved_clip_available"]=found;
            if(!found||clip is null)continue;
            bool bounded=true,effectsBounded=true,clearBackground=true,softAlpha=false,hasBody=true;
            int auditedFrames=0;
            var badBounds=new List<string>();var badSoftBounds=new List<string>();var badGreen=new List<string>();
            foreach(string id in clip.Select(step=>step.Frame).Distinct())
            {
                var pixels=library.Pixels(id);int opaque=0,green=0;
                bool frameBounded=true,frameEffectsBounded=true;auditedFrames++;
                for(int y=0;y<library.Height;y++)for(int x=0;x<library.Width;x++)
                {
                    int p=(y*library.Width+x)*4,a=pixels[p+3];
                    softAlpha|=a is >0 and <255;
                    // Smoke can be nearly transparent while still visibly clipped.
                    // Audit every frame's full effect silhouette, not just the body.
                    if(a>1&&(x<2||y<2||x>=library.Width-2||y>=library.Height-2))frameEffectsBounded=false;
                    if(a>224)
                    {
                        opaque++;
                        if(x<2||y<2||x>=library.Width-2||y>=library.Height-2)frameBounded=false;
                    }
                    // Strong chroma green is absent from this character's approved
                    // grey/cyan/blue palette and orange flame; allow isolated noise.
                    if(a>128&&pixels[p+1]>pixels[p]+40&&pixels[p+1]>pixels[p+2]+40)green++;
                }
                hasBody&=opaque>100;
                bounded&=frameBounded;
                effectsBounded&=frameEffectsBounded;
                bool clean=green<=Math.Max(3,opaque/1000);clearBackground&=clean;
                if(!frameBounded&&badBounds.Count<20)badBounds.Add(id);
                if(!frameEffectsBounded)badSoftBounds.Add(id);
                if(!clean&&badGreen.Count<20)badGreen.Add(id);
            }
            checks[$"{clipName}_opaque_silhouette_inside_canvas"]=bounded&&hasBody;
            checks[$"{clipName}_all_soft_effect_pixels_inside_canvas"]=effectsBounded;
            checks[$"{clipName}_chroma_green_removed"]=clearBackground;
            checks[$"{clipName}_soft_edges_preserved"]=softAlpha;

            int maxDifference=0;
            var visual=new PetVisual(library);
            var samples=new List<BitmapSource>();
            foreach(int index in new[]{0,clip.Length/4,clip.Length/2,clip.Length*3/4,clip.Length-1})
            {
                string id=clip[index].Frame;visual.Apply(new PetPose(id));
                var snapshot=visual.Snapshot(library.Width,library.Height);
                byte[] actual=new byte[library.Width*library.Height*4];snapshot.CopyPixels(actual,library.Width*4,0);
                byte[] expected=library.Pixels(id);
                for(int p=0;p<actual.Length;p++)maxDifference=Math.Max(maxDifference,Math.Abs(actual[p]-expected[p]));
                samples.Add(snapshot);
                Save(snapshot,Path.Combine(directory,$"{clipName}-{index:D3}.png"));
            }
            checks[$"{clipName}_renderer_preserves_complete_source_frame"]=maxDifference<=1;
            SaveSheet(samples,Path.Combine(directory,clipName+"-review.png"));

            double duration=clip.Sum(step=>step.Seconds);
            var engine=new PetBehavior(4,library);engine.Trigger(action);
            var visited=new HashSet<string>();PetPose lastPose=default;
            for(int tick=0;tick<Math.Ceiling((duration+3)*120);tick++)
            {
                lastPose=engine.Update(1d/120,false);visited.Add(lastPose.Frame);
                if(engine.ActionsStarted>0&&!engine.IsPlaying)break;
            }
            bool complete=engine.ActionsStarted==1&&!engine.IsPlaying&&clip.All(step=>visited.Contains(step.Frame));
            string end=clip[^1].Frame;
            bool stable=lastPose.Frame==end&&lastPose.BlendFrom is null;
            for(int tick=0;tick<120;tick++)stable&=engine.Update(1d/120,false).Frame==end;
            checks[$"{clipName}_all_frames_play_and_action_finishes"]=complete;
            checks[$"{clipName}_natural_end_pose_is_kept"]=stable;

            // A request made mid-emotion must wait for its natural finish.
            var queued=new PetBehavior(4,library);queued.Trigger(action);
            for(int tick=0;tick<180;tick++)queued.Update(1d/120,false);
            PetAction next=action==PetAction.Salute?PetAction.March:PetAction.Salute;
            queued.Trigger(next);bool held=queued.CurrentAction==action;bool sawEnd=false;
            for(int tick=0;tick<Math.Ceiling((duration+3)*120)&&queued.CurrentAction!=next;tick++)
            {
                var pose=queued.Update(1d/120,false);
                if(pose.Frame==end)sawEnd=true;
            }
            checks[$"{clipName}_queued_emotion_waits_for_exit"]=held&&sawEnd&&queued.CurrentAction==next&&queued.Interruptions==0;
            metrics.Add(new{clip=clipName,frames=clip.Length,auditedFrames,seconds=duration,maxSourceDifference=maxDifference,badBounds,badSoftBounds,badGreen});
        }
        File.WriteAllText(Path.Combine(directory,"results.json"),JsonSerializer.Serialize(new
        {success=checks.Values.All(value=>value),checks,clips=metrics},new JsonSerializerOptions{WriteIndented=true}));
        return checks;
    }

    private static void Save(BitmapSource image,string path)
    {
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream=File.Create(path);encoder.Save(stream);
    }
    private static void SaveSheet(IReadOnlyList<BitmapSource> samples,string path)
    {
        int width=Math.Min(256,samples[0].PixelWidth);
        int height=(int)Math.Round(width*samples[0].PixelHeight/(double)samples[0].PixelWidth);
        var drawing=new DrawingVisual();
        using(var context=drawing.RenderOpen())
        {
            for(int row=0;row<2;row++)for(int i=0;i<samples.Count;i++)
            {
                var bounds=new Rect(i*width,row*height,width,height);
                context.DrawRectangle(row==0?new SolidColorBrush(Color.FromRgb(25,29,35)):Brushes.WhiteSmoke,null,bounds);
                context.DrawImage(samples[i],bounds);
            }
        }
        var bitmap=new RenderTargetBitmap(samples.Count*width,2*height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(drawing);Save(bitmap,path);
    }
}
