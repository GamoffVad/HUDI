using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hudi.Distribution;

namespace HudiPet;

internal static class SitesArcVerification
{
    internal static void Run(string directory,Dictionary<string,bool> checks)
    {
        var screen=new Rect(0,0,1920,1080);
        var edges=new (string Name,Rect Pet,SitesArcSide Side)[]
        {
            ("right",new(1644,360,276,360),SitesArcSide.Left),("left",new(0,360,276,360),SitesArcSide.Right),
            ("top",new(822,0,276,360),SitesArcSide.Bottom),("bottom",new(822,720,276,360),SitesArcSide.Top),
            ("top_right",new(1644,0,276,360),SitesArcSide.Left),("top_left",new(0,0,276,360),SitesArcSide.Right),
            ("bottom_right",new(1644,720,276,360),SitesArcSide.Left),("bottom_left",new(0,720,276,360),SitesArcSide.Right)
        };
        foreach(var edge in edges)foreach(int count in new[]{2,5,20})
            checks[$"arc_turns_inward_{edge.Name}_{count}"]=SitesArc.Arrange(edge.Pet,screen,count,128).Side==edge.Side;
        var rightPet=edges[0].Pet;var pair=SitesArc.Arrange(rightPet,screen,2,128);
        double pairRadius=pair.Diameter*SiteOrb.MaximumLivingScale/2;
        checks["two_spheres_occupy_left_side_at_right_edge"]=pair.Side==SitesArcSide.Left&&pair.SweepDegrees==90&&
            pair.Centers.All(p=>p.X<pair.Origin.X)&&pair.Centers.Average(p=>p.X)<pair.Origin.X&&
            pair.Centers.All(p=>!rightPet.IntersectsWith(new Rect(p.X-pairRadius,p.Y-pairRadius,2*pairRadius,2*pairRadius)));
        var centered=SitesArc.Arrange(new Rect(822,420,276,360),screen,5,128);
        checks["arc_prefers_above_with_enough_room"]=centered.Side==SitesArcSide.Top;
        checks["normal_arc_is_a_true_centered_semicircle"]=centered.Origin==new Point(960,600)&&centered.SweepDegrees==180&&
            (centered.Centers[0]+(centered.Centers[^1]-centered.Centers[0])*.5-centered.Origin).Length<1e-6;
        checks["crowded_arc_reduces_sphere_size"]=SitesArc.Arrange(new Rect(822,420,276,360),screen,20,128).Diameter<centered.Diameter;
        foreach(var work in new[]{new Rect(-1920,0,1920,1080),new Rect(0,40,1280,680),new Rect(1920,-240,720,1080)})
        {
            bool bounded=true,clearOfPet=true,spaced=true,animationRoom=true,anchored=true,equalRadius=true;
            foreach(double height in new[]{180d,360,Math.Min(720,work.Height*.85)})
            foreach(double px in new[]{0d,.5,1})foreach(double py in new[]{0d,.5,1})
            foreach(int count in new[]{1,2,5,20,100})foreach(double maximum in new[]{24d,128})
            {
                double width=height*.768;var pet=new Rect(work.Left+(work.Width-width)*px,work.Top+(work.Height-height)*py,width,height);
                var arc=SitesArc.Arrange(pet,work,count,maximum);double r=arc.Diameter*SiteOrb.MaximumLivingScale/2,animated=arc.Diameter*.57+10;
                anchored&=(arc.Origin-new Point(pet.Left+pet.Width/2,pet.Top+pet.Height/2)).Length<1e-6;
                equalRadius&=arc.Centers.All(p=>Math.Abs((p-arc.Origin).Length-arc.Radius)<1e-6);
                bounded&=arc.Diameter>0&&arc.Centers.All(p=>work.Contains(new Rect(p.X-r,p.Y-r,2*r,2*r)));
                clearOfPet&=arc.Centers.All(p=>!pet.IntersectsWith(new Rect(p.X-r,p.Y-r,2*r,2*r)));
                spaced&=arc.Centers.Zip(arc.Centers.Skip(1),(a,b)=>(a-b).Length).All(distance=>distance>2*r);
                animationRoom&=arc.Centers.All(p=>work.Contains(new Rect(p.X-animated,p.Y-animated,2*animated,2*animated)));
            }
            checks[$"adaptive_arc_stays_on_monitor_{work.Left}"]=bounded;
            checks[$"adaptive_arc_does_not_cover_pet_{work.Left}"]=clearOfPet;
            checks[$"adaptive_arc_keeps_sphere_gaps_{work.Left}"]=spaced;
            checks[$"adaptive_arc_reserves_animation_room_{work.Left}"]=animationRoom;
            checks[$"adaptive_arc_keeps_pet_at_circle_center_{work.Left}"]=anchored;
            checks[$"adaptive_arc_spheres_share_one_radius_{work.Left}"]=equalRadius;
        }
        RenderSides(directory);
    }
    private static void RenderSides(string directory)
    {
        var root=new Grid();for(int i=0;i<2;i++){root.RowDefinitions.Add(new());root.ColumnDefinitions.Add(new());}
        var work=new Rect(0,0,640,480);
        var examples=new[]{("Справа — сферы слева",new Rect(456,120,184,240),2),("Слева — сферы справа",new Rect(0,120,184,240),5),("Сверху — сферы снизу",new Rect(228,0,184,240),5),("Снизу — сферы сверху",new Rect(228,240,184,240),5)};
        for(int i=0;i<examples.Length;i++)
        {
            var (title,pet,count)=examples[i];var canvas=new Canvas{Width=640,Height=480,Background=new SolidColorBrush(Color.FromRgb(24,27,31))};Grid.SetRow(canvas,i/2);Grid.SetColumn(canvas,i%2);root.Children.Add(canvas);
            var edge=new Border{Width=640,Height=480,BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromRgb(82,105,119))};canvas.Children.Add(edge);
            var layout=SitesArc.Arrange(pet,work,count,96);
            for(int n=0;n<count;n++){var face=SiteOrb.CreateFace("",(n+1).ToString(),layout.Diameter);Canvas.SetLeft(face,layout.Centers[n].X-layout.Diameter/2);Canvas.SetTop(face,layout.Centers[n].Y-layout.Diameter/2);canvas.Children.Add(face);}
            var library=SpriteLibrary.Shared.Value;
            var windowBounds=PetWindow.WindowForDisplay(pet,new(library.Width,library.Height),library.DisplayBounds);
            var character=new PetVisual{Width=windowBounds.Width,Height=windowBounds.Height};Canvas.SetLeft(character,windowBounds.Left);Canvas.SetTop(character,windowBounds.Top);canvas.Children.Add(character);
            var caption=Ui.Text(title,14,Ui.Accent);Canvas.SetLeft(caption,12);Canvas.SetTop(caption,12);canvas.Children.Add(caption);
        }
        var window=new Window{Width=1280,Height=960,Content=root};VisualPreview.Save(window,Path.Combine(directory,"adaptive-sides.png"));window.Close();
    }
}
