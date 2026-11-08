using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hudi.Distribution;

namespace HudiPet;

internal static class LivingSpheresVerification
{
    internal static void Run(string directory,string icon,Dictionary<string,bool> checks)
    {
        var waterFrames=WaterOrbFrames.Ready.GetAwaiter().GetResult();
        checks["water_frames_are_shared_frozen_and_bounded"]=waterFrames.Length==120&&waterFrames.All(f=>f.IsFrozen&&f.PixelWidth==192&&f.PixelHeight==192)&&waterFrames.Length*192*192*4<18*1024*1024;
        var pixels=new byte[192*192*4];waterFrames[0].CopyPixels(pixels,192*4,0);
        int translucent=0,blue=0;
        for(int p=0;p<pixels.Length;p+=4)if(pixels[p+3] is >15 and <240){translucent++;if(pixels[p]>pixels[p+1]&&pixels[p+1]>pixels[p+2])blue++;}
        checks["water_keeps_partial_alpha_and_shoe_blue"]=translucent>5000&&blue>translucent*.95&&pixels[3]==0;
        var canvas=new Canvas{Background=new SolidColorBrush(Color.FromRgb(24,27,31))};
        var window=new Window{Title="HUDI — движение сфер",Width=416,Height=160,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Content=canvas};
        var orbs=new[]{new SiteOrb(new(Guid.NewGuid().ToString("N"),"Почта","https://example.com",icon,"Почта"),96),new SiteOrb(new(Guid.NewGuid().ToString("N"),"Значок","https://example.com",icon),96),new SiteOrb(new(Guid.NewGuid().ToString("N"),"ИИ","https://example.com","","ИИ"),96)};
        foreach(var orb in orbs)canvas.Children.Add(orb);window.Show();window.UpdateLayout();
        for(int i=0;i<orbs.Length;i++)orbs[i].Move(new Point(32+i*128,32),new Point(32+i*128,32),true,i,orbs.Length);
        UiVerification.Advance(600);
        var symbols=orbs.Select(o=>((Grid)o.Content).Children.OfType<FrameworkElement>().Single(child=>child is Viewbox||child is Grid grid&&grid.Children.OfType<Image>().Any())).ToArray();
        checks["water_overlays_symbols_inside_spheres"]=orbs.All(orb=>
        {
            var face=(Grid)orb.Content;var water=face.Children.OfType<WaterOrbSurface>().Single();
            var symbol=face.Children.OfType<FrameworkElement>().Single(child=>child is Viewbox||child is Grid grid&&grid.Children.OfType<Image>().Any());
            return face.Children.IndexOf(water)>face.Children.IndexOf(symbol);
        });
        Rect Bounds(int index)=>symbols[index].TransformToAncestor((Grid)orbs[index].Content).TransformBounds(new Rect(0,0,symbols[index].ActualWidth,symbols[index].ActualHeight));
        var symbolBounds=Enumerable.Range(0,orbs.Length).Select(Bounds).ToArray();
        bool bounded=true,steadySymbols=true,clickableEdge=false,individualRhythm=false;double movement=0;
        var waterIndices=orbs.Select(o=>o.Water.FrameIndex).ToArray();bool flowingWater=false;
        string frames=Path.Combine(directory,"living-frames");Directory.CreateDirectory(frames);
        for(int frame=0;frame<150;frame++)
        {
            UiVerification.Advance(40);
            foreach(var orb in orbs)
            {
                double sx=orb.LivingShape.ScaleX,sy=orb.LivingShape.ScaleY;
                movement=Math.Max(movement,Math.Abs(sx-sy));bounded&=sx>=.989&&sy>=.989&&sx<=SiteOrb.MaximumLivingScale+.001&&sy<=SiteOrb.MaximumLivingScale+.001&&Math.Abs(sx*sy-1)<.001;
            }
            steadySymbols&=Enumerable.Range(0,orbs.Length).All(i=>Bounds(i)==symbolBounds[i]);
            individualRhythm|=orbs.Max(o=>o.LivingShape.ScaleX)-orbs.Min(o=>o.LivingShape.ScaleX)>.003;
            flowingWater|=orbs.Select((o,i)=>o.Water.FrameIndex!=waterIndices[i]).All(changed=>changed);
            var target=orbs[0];double radius=target.Width/2;
            var shell=((Grid)target.Content).Children.OfType<WaterOrbSurface>().Single();
            var edge=target.LivingShape.ScaleX>target.LivingShape.ScaleY?new Point(radius*1.998,radius):new Point(radius,radius*1.998);
            edge=shell.TransformToAncestor(target).Transform(edge);
            if((edge-new Point(radius,radius)).Length>radius)clickableEdge|=target.ContainsPoint(edge);
            VisualPreview.Save(window,Path.Combine(frames,$"{frame:D3}.png"));
        }
        checks["living_contour_gently_becomes_oval"]=!SystemParameters.ClientAreaAnimation||movement>.012;
        checks["water_flows_inside_all_visible_spheres"]=!SystemParameters.ClientAreaAnimation||flowingWater;
        checks["transparent_water_center_is_clickable"]=orbs.All(o=>o.ContainsPoint(new Point(o.Width/2,o.Height/2)));
        checks["transparent_square_corners_do_not_capture_clicks"]=orbs.All(o=>!o.ContainsPoint(new Point(1,1)));
        checks["living_contour_preserves_area_and_stays_subtle"]=bounded;
        checks["living_spheres_have_individual_rhythms"]=!SystemParameters.ClientAreaAnimation||individualRhythm;
        checks["living_motion_does_not_distort_text_or_icons"]=steadySymbols;
        checks["stretched_edge_remains_clickable"]=!SystemParameters.ClientAreaAnimation||clickableEdge;
        checks["living_motion_respects_windows_animation_setting"]=orbs.All(o=>o.LivingShape.HasAnimatedProperties==SystemParameters.ClientAreaAnimation);
        orbs[0].ClickFeedback();UiVerification.Advance(60);
        checks["press_feedback_keeps_contour_motion"]=!SystemParameters.ClientAreaAnimation||(orbs[0].LivingShape.HasAnimatedProperties&&orbs[0].Press.HasAnimatedProperties);
        window.Close();UiVerification.Advance(40);
        checks["unloading_stops_living_clocks"]=orbs.All(o=>!o.LivingShape.HasAnimatedProperties&&!o.LivingTilt.HasAnimatedProperties);
        checks["unloading_stops_water_render_subscription"]=orbs.All(o=>!o.Water.IsAnimating)&&WaterOrbSurface.ActiveCount==0;
    }
}
