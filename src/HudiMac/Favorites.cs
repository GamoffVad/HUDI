using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace HudiPet;

internal sealed partial class MacPetWindow
{
    private readonly FavoriteSitesStore favorites = new();
    private readonly List<(Window Window, Bitmap? Icon)> orbit = new();
    private DispatcherTimer? orbitAnimation;

    private void ToggleOrbit()
    {
        if (orbit.Count > 0) { CloseOrbit(); return; }
        try
        {
            var sites = favorites.Read();
            if (sites.Length == 0) { OpenFavorites(); return; }
            foreach (var site in sites)
            {
                string encoded = appearance.Icons.FirstOrDefault(icon => icon.Id == site.LibraryIcon)?.Image ?? site.Icon;
                Bitmap? image = null;
                if (encoded.Length > 0)
                {
                    using var bytes = new MemoryStream(Convert.FromBase64String(encoded));
                    image = new Bitmap(bytes);
                }
                var content = image is not null ? (Control)new Image { Source = image, Stretch = Stretch.UniformToFill }
                    : new TextBlock { Text = site.Text ?? site.Name, TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = Brush.Parse(appearance.Text), HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
                var button = ButtonFor("", async () =>
                {
                    CloseOrbit();
                    try { Process.Start(FavoriteSitesStore.CreateLaunchInfo(site)); }
                    catch (Exception error) { await ShowError(error.Message); }
                });
                button.Content = content;
                button.Background = Brush.Parse(appearance.Surface);
                button.BorderBrush = Brush.Parse(appearance.Accent);
                button.BorderThickness = new Thickness(1);
                button.Padding = new Thickness(1);
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.VerticalContentAlignment = VerticalAlignment.Stretch;
                ToolTip.SetTip(button, site.Name);
                var orb = new Window
                {
                    Width = 32, Height = 32, SystemDecorations = SystemDecorations.None, CanResize = false,
                    Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                    ShowActivated = false, ShowInTaskbar = false, Topmost = Topmost, Content = button
                };
                orb.KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseOrbit(); };
                orbit.Add((orb, image));
                orb.Show(this);
            }
            var started = Stopwatch.StartNew();
            orbitAnimation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            orbitAnimation.Tick += (_, _) =>
            {
                double t = Math.Min(1, started.Elapsed.TotalMilliseconds / 220);
                PlaceOrbit(t);
                if (t >= 1) { orbitAnimation?.Stop(); orbitAnimation = null; }
            };
            PlaceOrbit(0);
            orbitAnimation.Start();
        }
        catch (Exception error) { CloseOrbit(); _ = ShowError(error.Message); }
    }

    private void PlaceOrbit(double progress = 1)
    {
        if (orbit.Count == 0) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        double scale = DesktopUnitScale;
        var display = library.DisplayBounds;
        double spriteScale = Height / library.Height;
        var body = new Rect(Position.X / scale + display.X * spriteScale,
            Position.Y / scale + display.Y * spriteScale, display.Width * spriteScale, display.Height * spriteScale);
        var bounds = screen.WorkingArea;
        var work = new Rect(bounds.X / scale, bounds.Y / scale, bounds.Width / scale, bounds.Height / scale);
        var layout = SitesArc.Arrange(body, work, orbit.Count, favorites.MaximumSphereSize);
        double ease = 1 - Math.Pow(1 - progress, 3), diameter = Math.Max(1, layout.Diameter);
        for (int i = 0; i < orbit.Count; i++)
        {
            var window = orbit[i].Window;
            var center = layout.Origin + (layout.Centers[i] - layout.Origin) * ease;
            window.Width = window.Height = diameter;
            if (window.Content is Button button)
            {
                button.CornerRadius = new CornerRadius(diameter / 2);
                if (button.Content is Image image)
                {
                    double radius = Math.Max(0, (diameter - 4) / 2);
                    image.Clip = new EllipseGeometry(new Rect(0, 0, radius * 2, radius * 2));
                }
            }
            window.Opacity = progress;
            window.Position = new PixelPoint((int)Math.Round((center.X - diameter / 2) * scale),
                (int)Math.Round((center.Y - diameter / 2) * scale));
        }
    }

    private void CloseOrbit()
    {
        orbitAnimation?.Stop();
        orbitAnimation = null;
        foreach (var (window, icon) in orbit) { window.Close(); icon?.Dispose(); }
        orbit.Clear();
    }

    private async void OpenFavorites()
    {
        var list = new ListBox { Height = 140 };
        var name = new TextBox { Watermark = "Название" };
        var target = new TextBox { Watermark = "Адрес сайта или путь к .app" };
        var label = new TextBox { Watermark = "Текст на сфере", MaxLength = FavoriteSitesStore.MaximumTextLength };
        var kind = new ComboBox { ItemsSource = new[] { "Сайт", "Приложение" }, SelectedIndex = 0 };
        var custom = Text("");
        var status = Text("");
        var icons = new ComboBox
        {
            ItemsSource = new[] { "Свой значок или текст" }.Concat(appearance.Icons.Select(icon => icon.Name)).ToArray(),
            SelectedIndex = 0
        };
        Window? dialog = null;
        string? id = null;
        string encoded = "";
        void Refresh(string? selectedId = null)
        {
            var items = favorites.Read().Select(site => new FavoriteItem(site)).ToArray();
            list.ItemsSource = items;
            list.SelectedItem = items.FirstOrDefault(item => item.Site.Id == selectedId);
        }
        void Edit(FavoriteSite? site)
        {
            id = site?.Id;
            name.Text = site?.Name;
            target.Text = site?.Url;
            label.Text = site?.Text;
            encoded = site?.Icon ?? "";
            kind.SelectedIndex = site?.Kind == "program" ? 1 : 0;
            custom.Text = encoded.Length > 0 ? "Значок выбран" : "";
            icons.SelectedIndex = Array.FindIndex(appearance.Icons, icon => icon.Id == site?.LibraryIcon) + 1;
        }
        list.SelectionChanged += (_, _) => Edit((list.SelectedItem as FavoriteItem)?.Site);
        var pickIcon = ButtonFor("Значок…", async () =>
        {
            try
            {
                var files = await dialog!.StorageProvider.OpenFilePickerAsync(new()
                {
                    Title = "Выбрать значок",
                    FileTypeFilter = [new FilePickerFileType("Изображение") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.ico", "*.webp"] }]
                });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                encoded = FavoriteSitesStore.ImportIcon(path);
                icons.SelectedIndex = 0;
                label.Text = "";
                custom.Text = Path.GetFileName(path);
            }
            catch (Exception error) { status.Text = error.Message; }
        });
        var save = ButtonFor("Сохранить", () =>
        {
            try
            {
                var selectedIcon = icons.SelectedIndex > 0 ? appearance.Icons[icons.SelectedIndex - 1] : null;
                string text = label.Text?.Trim() ?? "";
                string title = name.Text?.Trim() ?? "";
                string image = selectedIcon?.Image ?? (text.Length > 0 ? "" : encoded);
                if (image.Length == 0 && text.Length == 0) text = title[..Math.Min(title.Length, FavoriteSitesStore.MaximumTextLength)];
                string siteId = id ?? Guid.NewGuid().ToString("N");
                favorites.Put(new FavoriteSite(siteId, title, target.Text ?? "", image, text,
                    kind.SelectedIndex == 1 ? "program" : "site", selectedIcon?.Id));
                Refresh(siteId);
                status.Text = "";
            }
            catch (Exception error) { status.Text = error.Message; }
        });
        var remove = ButtonFor("Удалить", () =>
        {
            if (id is null) return;
            try { favorites.Delete(id); Refresh(); Edit(null); }
            catch (Exception error) { status.Text = error.Message; }
        });
        var size = new NumericUpDown { Minimum = 24, Maximum = 128, Increment = 1 };
        try { Refresh(); size.Value = (decimal?)favorites.MaximumSphereSize ?? 54; }
        catch (Exception error) { status.Text = error.Message; }
        var saveSize = ButtonFor("Применить", () =>
        {
            try { favorites.SetMaximumSphereSize((double)(size.Value ?? 54)); }
            catch (Exception error) { status.Text = error.Message; }
        });
        var body = Column(list, ButtonFor("+", () => { list.SelectedIndex = -1; Edit(null); name.Focus(); }),
            name, kind, target, label, icons, Row(pickIcon, custom), Row(save, remove),
            Text("Максимальный размер сфер"), Row(size, saveSize), status);
        dialog = Panel("Сайты и программы", Scroll(body), height: 580);
        await dialog.ShowDialog(this);
    }

    private sealed record FavoriteItem(FavoriteSite Site)
    {
        public override string ToString() => Site.Name;
    }
}
