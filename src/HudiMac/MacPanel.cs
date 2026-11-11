using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace HudiPet;

internal sealed class MacPanel : Window
{
    private readonly Border frame;
    private readonly DockPanel header;
    private readonly TextBlock caption;
    private readonly Style textStyle;
    private readonly Style buttonStyle;
    private readonly Style inputStyle;
    private readonly Style selectStyle;

    internal MacPanel(string title, Control body, double width, double height)
    {
        Title = title;
        Width = width;
        Height = height;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        FontSize = 13;
        UseLayoutRounding = true;

        var close = new Button
        {
            Content = "×", Width = 27, Height = 27, Padding = new Thickness(0),
            FontSize = 17, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0)
        };
        Avalonia.Automation.AutomationProperties.SetName(close, "Закрыть");
        close.Click += (_, _) => Close();
        caption = new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(2, 0, 4, 0),
            Background = Brushes.Transparent
        };
        caption.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(args);
        };
        header = new DockPanel { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(caption);
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(body);
        frame = new Border { Padding = new Thickness(12, 8, 12, 11), BorderThickness = new Thickness(1), Child = layout };
        Content = frame;
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape || args.Handled) return;
            args.Handled = true;
            Close();
        };

        textStyle = new Style(selector => selector.OfType<TextBlock>());
        buttonStyle = new Style(selector => selector.OfType<Button>());
        inputStyle = new Style(selector => selector.OfType<TextBox>());
        selectStyle = new Style(selector => selector.OfType<ComboBox>());
        Styles.Add(textStyle);
        Styles.Add(buttonStyle);
        Styles.Add(inputStyle);
        Styles.Add(selectStyle);
        PropertyChanged += (_, args) => { if (args.Property == TitleProperty) caption.Text = Title; };
    }

    internal void SetNoteChrome(bool active)
    {
        frame.Padding = new Thickness(0);
        header.IsVisible = active;
        header.Margin = new Thickness(7, 0, 5, 0);
    }

    internal void ApplyTheme(PetAppearance theme, double transparency, bool matte)
    {
        Color paper = Color.Parse(theme.Surface);
        Color ink = Color.Parse(theme.Text);
        Color accent = Color.Parse(theme.Accent);
        Color edge = Mix(paper, accent, .34);
        Color field = Mix(paper, ink, .075);
        double opacity = 1 - transparency / (matte ? 300 : 100);
        frame.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(.25, 1, RelativeUnit.Relative),
            Opacity = opacity,
            GradientStops = [new(Mix(paper, Colors.White, .05), 0), new(paper, 1)]
        };
        frame.BorderBrush = new SolidColorBrush(edge);
        var text = new SolidColorBrush(ink);
        Foreground = caption.Foreground = text;
        SetStyle(textStyle, new Setter(TextBlock.ForegroundProperty, text));
        SetStyle(buttonStyle, new Setter(Button.ForegroundProperty, text), new Setter(Button.CornerRadiusProperty, new CornerRadius(0)),
            new Setter(Button.BackgroundProperty, new SolidColorBrush(field)), new Setter(Button.BorderBrushProperty, new SolidColorBrush(edge)));
        SetStyle(inputStyle, new Setter(TextBox.ForegroundProperty, text), new Setter(TextBox.CornerRadiusProperty, new CornerRadius(0)),
            new Setter(TextBox.BackgroundProperty, new SolidColorBrush(field)), new Setter(TextBox.BorderBrushProperty, new SolidColorBrush(edge)));
        SetStyle(selectStyle, new Setter(ComboBox.ForegroundProperty, text), new Setter(ComboBox.CornerRadiusProperty, new CornerRadius(0)),
            new Setter(ComboBox.BackgroundProperty, new SolidColorBrush(field)), new Setter(ComboBox.BorderBrushProperty, new SolidColorBrush(edge)));
        Resources["SystemAccentColor"] = accent;
        Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(accent);
        Resources["TextFillColorPrimaryBrush"] = text;
        Resources["SolidBackgroundFillColorBaseBrush"] = new SolidColorBrush(paper);
        Resources["ControlFillColorDefaultBrush"] = new SolidColorBrush(field);
    }

    private void SetStyle(Style style, params Setter[] setters)
    {
        Styles.Remove(style);
        style.Setters.Clear();
        foreach (var setter in setters) style.Setters.Add(setter);
        Styles.Add(style);
    }

    internal static async Task FadeIn(Control control)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } }
            }
        };
        await animation.RunAsync(control);
    }

    internal Task Reveal() => FadeIn(frame);

    internal static Color Mix(Color a, Color b, double weight) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * weight),
        (byte)(a.G + (b.G - a.G) * weight),
        (byte)(a.B + (b.B - a.B) * weight));
}
