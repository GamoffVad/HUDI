using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Hudi.Distribution;

namespace HudiPet;

internal static class Ui
{
    internal static Brush Muted => PetAppearance.Current.Muted;
    internal static Brush Accent => PetAppearance.Current.Brush(PetAppearance.Current.Accent);
    internal static readonly Brush Error = new SolidColorBrush(Color.FromRgb(255,181,174));
    internal static void Initialize()
    {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/HUDI;component/Theme.xaml", UriKind.Relative) });
        PetAppearance.Current.Apply();
    }
    internal static TextBlock Text(string value, double size = 14, Brush? color = null)
    {
        var text=new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap};
        if(color is null)text.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");
        else if(color==Error)text.Foreground=color;
        else text.SetResourceReference(TextBlock.ForegroundProperty,color is SolidColorBrush b&&b.Color==PetAppearance.Current.AccentColor?"AccentBrush":"MutedBrush");
        return text;
    }
    internal static Button Button(string text, Action action, bool primary = false, bool quiet = false)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : quiet ? "QuietButton" : typeof(Button)) };
        button.Click += (_, _) => action(); AutomationProperties.SetName(button, text); return button;
    }
    internal static Button MenuRow(string glyph, string label, Action action, string? hint = null, bool compact = false)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(compact?26:30) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var icon = Text(glyph, 17, Accent); icon.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon);
        var title = Text(label, compact?13:14); title.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(title, 1); row.Children.Add(title);
        if (hint is not null) { var note = Text(hint, 11, Muted); note.VerticalAlignment = VerticalAlignment.Center; note.Margin = new Thickness(12,0,0,0); Grid.SetColumn(note,2); row.Children.Add(note); }
        var button = Button(label, action, quiet:true); button.Content = row; button.Padding = compact?new Thickness(8,6,8,6):new Thickness(10,11,10,11); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; return button;
    }
    internal static Border Separator() => new() { Height = 1, Background = new SolidColorBrush(Color.FromArgb(70,147,180,208)), Margin = new Thickness(0,10,0,10) };
    internal static TextBox Input(string value = "", string? name = null)
    {
        var input = new TextBox { Text = value, MinWidth = 0 };
        if (name is not null) AutomationProperties.SetName(input, name);
        return input;
    }
    internal static void Field(Panel panel, string label, FrameworkElement control, string? hint = null)
    {
        var title = Text(label, 12, Muted); title.Margin = new Thickness(0,12,0,6); panel.Children.Add(title); panel.Children.Add(control);
        if (hint is not null) { var note = Text(hint,11,Muted); note.Margin = new Thickness(0,5,0,0); panel.Children.Add(note); }
    }
    internal static CheckBox Toggle(string label, bool value) => new() { Content = label, IsChecked = value };
    internal static ComboBox Select(string[] items, int selected = 0)
    { var select = new ComboBox { ItemsSource = items, SelectedIndex = selected }; return select; }
    internal static StackPanel Row(params FrameworkElement[] items)
    { var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var item in items) { item.Margin = new Thickness(0,0,8,0); row.Children.Add(item); } return row; }
    internal static FrameworkElement Mark()
    {
        var inside = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (int _ in new[] {0,1}) inside.Children.Add(new Border { Width=4, Height=9, Background=Accent, Margin=new Thickness(4,0,4,0) });
        return new Border { Width=32, Height=27, BorderThickness=new Thickness(1.5), BorderBrush=Accent, Background=new SolidColorBrush(Color.FromRgb(16,33,47)), Child=inside, Margin=new Thickness(0,0,10,0) };
    }
}

internal class GlassWindow : Window, IWindowClipProvider
{
    internal readonly StackPanel Body = new();
    internal readonly TextBlock Heading;
    internal readonly Button CloseButton;
    internal readonly DialogSurface Surface;
    internal readonly DockPanel Header;
    private readonly PetWindow? anchor;
    private bool repositioning;
    private bool dismissQueued;
    private Brush? originalTint;
    internal bool FollowAnchor { get; set; } = true;
    internal bool IsClosing { get; private set; }
    protected virtual double AnchorGap=>12;
    protected virtual double PlacementHeight=>ActualHeight;
    public Geometry GetContentClip(Size size)=>DialogBackdrop.CreateClip(this,size);
    internal GlassWindow(PetWindow? anchor, string title, double width = 420, string? headerGlyph = null)
    {
        this.anchor = anchor; Title = title; Width = width; SizeToContent = SizeToContent.Height;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; DialogBackdrop.Attach(this);
        ShowInTaskbar=false; Topmost=anchor?.Topmost ?? true; Owner=anchor; FontFamily=new FontFamily("Segoe UI");
        var root = new Grid();
        var content = new StackPanel { Margin = new Thickness(20,17,20,18) };
        Surface = new DialogSurface { CornerRadius=new CornerRadius(FrostedBackdrop.CornerRadius), BorderThickness=new Thickness(1), BorderBrush=FrostedBackdrop.Edge,
            Background=FrostedBackdrop.Tint, Child=content };
        RefreshPetTheme();
        root.Children.Add(Surface);
        Surface.OutlineChanged+=()=>{if(!IsClosing)DialogBackdrop.Refresh(this);};
        var header = Header = new DockPanel { LastChildFill=true, Margin=new Thickness(0,0,0,14) };
        CloseButton=Ui.Button("×",Close,quiet:true); CloseButton.FontSize=20; CloseButton.Padding=new Thickness(6,0,6,1); DockPanel.SetDock(CloseButton,Dock.Right); header.Children.Add(CloseButton);
        FrameworkElement mark=Ui.Mark();
        if(headerGlyph is not null){var glyph=Ui.Text(headerGlyph,28);glyph.FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");glyph.Margin=new Thickness(0,0,13,0);glyph.VerticalAlignment=VerticalAlignment.Center;mark=glyph;}
        header.Children.Add(mark); Heading=Ui.Text(title,17); Heading.FontWeight=FontWeights.SemiBold; Heading.VerticalAlignment=VerticalAlignment.Center; header.Children.Add(Heading);
        content.Children.Add(header); content.Children.Add(Body); Content=root;
        KeyDown += (_,e) => { if (e.Key==System.Windows.Input.Key.Escape) { Close(); e.Handled=true; } };
        Loaded += (_,_) => { RefreshTransparency();Place(); };
        SizeChanged += (_,_) => Place();
        Closed += (_,_) => { IsClosing=true; if (anchor is not null) { anchor.LocationChanged-=AnchorMoved; anchor.SizeChanged-=AnchorSized; anchor.DialogTransparencyChanged-=RefreshTransparency; } };
        if (anchor is not null) { anchor.LocationChanged+=AnchorMoved; anchor.SizeChanged+=AnchorSized; anchor.DialogTransparencyChanged+=RefreshTransparency; }
    }
    protected void UseSquareFrame()
    {
        SizeToContent=SizeToContent.Manual;Width=Height=DialogSurface.SquareSize;
        Surface.SquareFrame=true;Surface.CornerRadius=new CornerRadius(0);Surface.BorderThickness=new Thickness(0);
        ((StackPanel)Surface.Child).Margin=new Thickness(DialogSurface.ContentInset);
        Header.Height=32;Header.Margin=new Thickness(0,0,0,12);
        DialogBackdrop.SetOutline(this,Surface.CurrentOutline);
    }
    protected double SquareBodyHeight=>Height-DialogSurface.ContentInset*2-(Header.Visibility==Visibility.Visible?Header.Height+Header.Margin.Top+Header.Margin.Bottom:0);
    protected Grid SquareBody()=>new() {Height=SquareBodyHeight};
    internal void RefreshPetTheme()
    {
        PetAppearance.Current.ApplyResources(Resources);
        Surface.Background=PetAppearance.Current.Panel;Surface.BorderBrush=PetAppearance.Current.Edge;
        originalTint=null;
        if(IsLoaded)
        {
            RefreshTransparency();
            OnPetThemeRefreshed();
        }
    }
    protected virtual void OnPetThemeRefreshed() { }
    private void RefreshTransparency()
    {
        if(!IsLoaded||IsClosing||anchor is null)return;
        // Capture each panel's final themed tint after construction; never fade its text.
        originalTint ??= Surface.Background.CloneCurrentValue();
        var tint=originalTint.CloneCurrentValue();double value=anchor.DialogTransparency;
        Color Adjust(Color color)
        {
            double alpha=255*(1-value/100);
            return Color.FromArgb((byte)Math.Clamp(Math.Round(alpha),0,255),color.R,color.G,color.B);
        }
        if(tint is GradientBrush gradient)foreach(var stop in gradient.GradientStops)stop.Color=Adjust(stop.Color);
        else if(tint is SolidColorBrush solid)solid.Color=Adjust(solid.Color);
        tint.Freeze();Surface.Background=tint;
        DialogBackdrop.SetMatte(this,anchor.MatteDialogs);
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        // WM_ACTIVATE can arrive during Close(), before Closed fires.
        // Publish the state first so deactivation cannot re-enter Close().
        IsClosing=true;
        base.OnClosing(e);
        if(e.Cancel)IsClosing=false;
    }
    internal void DismissWhenInactive()
    {
        if(IsClosing||dismissQueued)return;
        dismissQueued=true;
        Dispatcher.BeginInvoke(() =>
        {
            dismissQueued=false;
            if(!IsClosing&&!IsActive&&IsVisible)Close();
        },System.Windows.Threading.DispatcherPriority.Background);
    }
    private void AnchorMoved(object? sender, EventArgs args) => Place();
    private void AnchorSized(object sender, SizeChangedEventArgs args) => Place();
    internal void Place()
    {
        if (anchor is null || !IsLoaded || repositioning || IsClosing || !FollowAnchor) return;
        repositioning=true;
        try
        {
            var area = ScreenPlacement.WorkArea(anchor);
            double height = ActualHeight > 0 ? ActualHeight : 200;
            double left = anchor.Left + anchor.Width/2 - Width/2;
            double top = anchor.Top - height - AnchorGap;
            if (anchor.Top-(PlacementHeight>0?PlacementHeight:height)-AnchorGap < area.Top) { top = anchor.Top; left = anchor.Left + anchor.Width + AnchorGap; }
            Left = Math.Clamp(left,area.Left,Math.Max(area.Left,area.Right-Width));
            Top = Math.Clamp(top,area.Top,Math.Max(area.Top,area.Bottom-height));
        }
        finally { repositioning=false; }
    }
}

internal static class ScreenPlacement
{
    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectNative Monitor,Work; public int Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    internal static Rect WorkArea(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        var info=new MonitorInfo { Size=Marshal.SizeOf<MonitorInfo>() };
        if (handle!=IntPtr.Zero && GetMonitorInfo(MonitorFromWindow(handle,2),ref info))
        {
            var dpi=VisualTreeHelper.GetDpi(window);
            return new Rect(info.Work.Left/dpi.DpiScaleX,info.Work.Top/dpi.DpiScaleY,(info.Work.Right-info.Work.Left)/dpi.DpiScaleX,(info.Work.Bottom-info.Work.Top)/dpi.DpiScaleY);
        }
        return SystemParameters.WorkArea;
    }
}
