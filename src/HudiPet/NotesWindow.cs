using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HudiPet;

internal sealed class NotesWindow : GlassWindow
{
    private readonly NotesStore store;
    private readonly PetWindow pet;
    private readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly TextBlock status;
    private readonly Button add;
    private readonly Button undo;
    private readonly List<Card> entries = [];
    private PetNote? deleted;

    internal NotesWindow(PetWindow owner, NotesStore? store = null) : base(owner, "Заметки", 153, "\uE70B")
    {
        pet = owner;
        this.store = store ?? new NotesStore();
        UseSquareFrame();
        Width = 153;
        SizeToContent = SizeToContent.Height;
        Height = double.NaN;
        ((StackPanel)Surface.Child).Margin = new Thickness(5);
        Header.Height = 24;
        Header.Margin = new Thickness(0);
        Heading.FontSize = 13;
        if (Header.Children[1] is TextBlock icon)
        {
            icon.FontSize = 14;
            icon.Margin = new Thickness(3, 0, 5, 0);
        }

        CloseButton.Width = 24;
        CloseButton.Height = 24;
        CloseButton.Padding = new Thickness(0);
        CloseButton.FontSize = 16;
        AutomationProperties.SetName(CloseButton, "Закрыть все заметки");
        add = Ui.Button("Новая заметка", AddNote, quiet: true);
        add.Content = "+";
        add.Width = add.Height = 24;
        add.Padding = new Thickness(0);
        add.FontSize = 18;
        DockPanel.SetDock(add, Dock.Right);
        Header.Children.Insert(1, add);

        status = Ui.Text("", 11);
        status.Visibility = Visibility.Collapsed;
        Body.Children.Add(status);
        undo = Ui.Button("Отменить удаление", UndoDelete, quiet: true);
        undo.Visibility = Visibility.Collapsed;
        Body.Children.Add(undo);

        autosave.Tick += (_, _) => Flush();
        Closed += (_, _) => autosave.Stop();
        try
        {
            foreach (var note in this.store.Read()) entries.Add(new Card(this, note));
            if (this.store.RecoveredBackup) Message("Заметки восстановлены из резервной копии.");
        }
        catch (Exception error) when (IsStorageError(error))
        {
            add.IsEnabled = false;
            Message("Не удалось прочитать заметки. Файлы сохранены.");
        }
        Loaded += (_, _) => Dispatcher.BeginInvoke(ShowAll, DispatcherPriority.Loaded);
    }

    internal void ShowAll()
    {
        if (IsClosing) return;
        var area = ScreenPlacement.WorkArea(pet);
        int index = 0;
        foreach (var card in entries)
        {
            if (!card.IsVisible) ShowCard(card, area, index);
            index++;
        }

        if (entries.LastOrDefault() is { } last) last.Activate();
        else Activate();
    }

    private void ShowCard(Card card, Rect area, int index)
    {
        if (!card.IsLoaded)
        {
            const double gutter = 8;
            var saved = card.Note.WindowSize;
            card.Width = Math.Min(saved is { IsValid: true } ? saved.Width : 248, area.Width);
            card.Height = Math.Min(saved is { IsValid: true } ? saved.Height : 132, area.Height);
            double stepX = card.Width + gutter;
            double stepY = card.Height + gutter;
            int columns = Math.Max(1, (int)((area.Width - 16) / stepX));
            int rows = Math.Max(1, (int)((area.Height - 54) / stepY));
            int slot = index % (columns * rows);
            double shift = (index / (columns * rows) % 7) * 9;
            double startX = Math.Clamp(Left - stepX, area.Left + gutter, Math.Max(area.Left + gutter, area.Right - columns * stepX));
            double startY = Math.Clamp(Top + 43, area.Top + 43, Math.Max(area.Top + 43, area.Bottom - rows * stepY));
            card.Left = Math.Clamp(startX + slot % columns * stepX + shift, area.Left, Math.Max(area.Left, area.Right - card.Width));
            card.Top = Math.Clamp(startY + slot / columns * stepY + shift, area.Top, Math.Max(area.Top, area.Bottom - card.Height));
        }
        card.Owner = this;
        card.Reveal(TimeSpan.FromMilliseconds(Math.Min(index, 6) * 25));
        card.Show();
    }

    private void AddNote()
    {
        var now = DateTimeOffset.Now;
        var note = new PetNote(Guid.NewGuid().ToString("N"), "", now, now);
        try
        {
            store.Put(note);
            var card = new Card(this, note);
            entries.Add(card);
            ShowCard(card, ScreenPlacement.WorkArea(pet), entries.Count - 1);
            card.Activate();
            card.Editor.Focus();
            Message("");
        }
        catch (Exception error) when (IsStorageError(error)) { Message("Не удалось создать заметку."); }
    }

    private void Delete(Card card)
    {
        if (!Flush()) return;
        try
        {
            store.Delete(card.Note.Id);
            deleted = card.Note;
            entries.Remove(card);
            card.Close();
            undo.Visibility = Visibility.Visible;
        }
        catch (Exception error) when (IsStorageError(error)) { Message("Не удалось удалить заметку."); }
    }

    private void UndoDelete()
    {
        if (deleted is not { } note) return;
        try
        {
            store.Put(note);
            var card = new Card(this, note);
            entries.Add(card);
            ShowCard(card, ScreenPlacement.WorkArea(pet), entries.Count - 1);
            deleted = null;
            undo.Visibility = Visibility.Collapsed;
        }
        catch (Exception error) when (IsStorageError(error)) { Message("Не удалось восстановить заметку."); }
    }

    private void ScheduleSave()
    {
        autosave.Stop();
        autosave.Start();
    }

    internal bool Flush()
    {
        autosave.Stop();
        try
        {
            bool changed = false;
            foreach (var card in entries.Where(card => card.Dirty))
            {
                card.Save(store);
                changed = true;
            }
            if (changed) Message("");
            return true;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            Message("Не удалось сохранить заметку. Изменения остаются в окне.");
            return false;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Flush()) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnPetThemeRefreshed()
    {
        foreach (var card in entries) card.ApplyTheme(pet.Appearance);
    }

    private void Message(string message)
    {
        status.Text = message;
        status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsStorageError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException;

    private sealed class Card : Window
    {
        private readonly NotesWindow board;
        private readonly Border header = new();
        private readonly Border footer = new();
        private readonly System.Windows.Controls.Primitives.Thumb resizeGrip;
        private readonly DockPanel commands = new();
        private readonly List<Button> buttons = [];
        private readonly Border divider;
        private Brush ink = Brushes.Black;
        private Brush selected = Brushes.Transparent;

        private Border Frame { get; }
        internal RichTextBox Editor { get; }
        internal PetNote Note { get; private set; }
        internal bool Dirty { get; private set; }

        internal Card(NotesWindow board, PetNote note)
        {
            this.board = board;
            Note = note;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = board.Topmost;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.Transparent;
            Title = "Заметка";
            UseLayoutRounding = true;

            var body = new DockPanel();
            var layers = new Grid();
            layers.Children.Add(body);
            resizeGrip = new System.Windows.Controls.Primitives.Thumb
            {
                Width = 16, Height = 16, Focusable = false, Cursor = Cursors.SizeNWSE,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom
            };
            AutomationProperties.SetName(resizeGrip, "Изменить размер заметки");
            var gripFace = new FrameworkElementFactory(typeof(Border));
            gripFace.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var gripLines = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            gripLines.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 3,12 L 12,3 M 7,12 L 12,7"));
            gripLines.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            gripLines.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1d);
            gripFace.AppendChild(gripLines);
            resizeGrip.Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = gripFace };
            resizeGrip.DragDelta += (_, args) =>
            {
                var area = ScreenPlacement.WorkArea(this);
                Width = Math.Clamp(Width + args.HorizontalChange, NoteSize.MinWidth,
                    Math.Max(NoteSize.MinWidth, Math.Min(NoteSize.MaxWidth, area.Right - Left)));
                Height = Math.Clamp(Height + args.VerticalChange, NoteSize.MinHeight,
                    Math.Max(NoteSize.MinHeight, Math.Min(NoteSize.MaxHeight, area.Bottom - Top)));
                Dirty = true;
                board.ScheduleSave();
            };
            layers.Children.Add(resizeGrip);
            Frame = new Border { Child = layers, BorderThickness = new Thickness(1) };
            Content = Frame;
            var dismiss = ToolButton("×", "Скрыть заметку", HideNote, includeInFormatting: false);
            DockPanel.SetDock(dismiss, Dock.Right);
            commands.Children.Add(dismiss);
            var more = ToolButton("⋯", "Действия с заметкой", () => { }, includeInFormatting: false);
            var menu = new ContextMenu();
            var delete = new MenuItem { Header = "Удалить заметку" };
            delete.Click += (_, _) => board.Delete(this);
            menu.Items.Add(delete);
            more.ContextMenu = menu;
            more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; };
            DockPanel.SetDock(more, Dock.Right);
            commands.Children.Add(more);
            var create = ToolButton("+", "Новая заметка", board.AddNote, includeInFormatting: false);
            DockPanel.SetDock(create, Dock.Left);
            commands.Children.Add(create);
            var handle = new Border { Background = Brushes.Transparent };
            handle.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
            commands.Children.Add(handle);
            header.Child = commands;
            DockPanel.SetDock(header, Dock.Top);
            body.Children.Add(header);

            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 0, 3, 0) };
            tools.Children.Add(Command("B", "Жирный", EditingCommands.ToggleBold, FontWeights.Bold));
            tools.Children.Add(Command("I", "Курсив", EditingCommands.ToggleItalic, italic: true));
            tools.Children.Add(ToolButton("U", "Подчёркивание", () => ToggleDecoration(TextDecorationLocation.Underline), TextDecorations.Underline));
            tools.Children.Add(ToolButton("abc", "Зачёркивание", () => ToggleDecoration(TextDecorationLocation.Strikethrough), TextDecorations.Strikethrough));
            divider = new Border { Width = 1, Margin = new Thickness(4, 4, 4, 4) };
            tools.Children.Add(divider);
            tools.Children.Add(Command("list", "Маркированный список", EditingCommands.ToggleBullets, icon: true));
            tools.Children.Add(ToolButton("image", "Вставить изображение", InsertImage, icon: true));
            footer.Child = tools;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            DockPanel.SetDock(footer, Dock.Bottom);
            body.Children.Add(footer);

            Editor = new RichTextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(9, 5, 8, 3),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FontSize = 13,
                AcceptsTab = false
            };
            AutomationProperties.SetName(Editor, "Текст заметки");
            Editor.Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph))
            {
                Setters = { new Setter(Block.MarginProperty, new Thickness(0, 0, 0, 3)) }
            });
            body.Children.Add(Editor);
            Load();
            Editor.TextChanged += (_, _) => { Dirty = true; board.ScheduleSave(); };
            Editor.SelectionChanged += (_, _) => UpdateTools();
            Activated += (_, _) => ShowTools(true);
            Deactivated += (_, _) => ShowTools(false);
            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                HideNote();
                e.Handled = true;
            };
            ApplyTheme(board.pet.Appearance);
            ShowTools(false);
        }

        private Button Command(string glyph, string label, RoutedCommand command, FontWeight? weight = null,
            bool italic = false, bool icon = false)
        {
            var button = ToolButton(glyph, label, () =>
            {
                Editor.Focus();
                if (command.CanExecute(null, Editor)) command.Execute(null, Editor);
                UpdateTools();
            }, icon: icon);
            if (weight is { } fontWeight) button.FontWeight = fontWeight;
            if (italic) button.FontStyle = FontStyles.Italic;
            return button;
        }

        private Button ToolButton(string glyph, string label, Action invoke, TextDecorationCollection? decoration = null, bool icon = false, bool includeInFormatting = true)
        {
            var button = Ui.Button(label, invoke, quiet: true);
            button.Content = icon ? new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(glyph == "list"
                    ? "M 0,2 L 1,2 M 4,2 H 14 M 0,6 L 1,6 M 4,6 H 14 M 0,10 L 1,10 M 4,10 H 14"
                    : "M 0,0 H 14 V 10 H 0 Z M 1,8 L 5,4 L 8,7 L 10,5 L 13,8 M 10.5,2.5 H 11.5"),
                Width = 15,
                Height = 12,
                StrokeThickness = 1.1,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            } : new TextBlock { Text = glyph, TextDecorations = decoration, VerticalAlignment = VerticalAlignment.Center };
            button.Width = glyph == "abc" ? 32 : 27;
            button.Height = 24;
            button.Padding = new Thickness(0);
            button.FontSize = 13;
            button.Focusable = !includeInFormatting;
            button.ToolTip = glyph == "×" ? null : label;
            if (includeInFormatting) buttons.Add(button);
            return button;
        }

        internal void ShowTools(bool visible)
        {
            if (commands.Visibility == (visible ? Visibility.Visible : Visibility.Collapsed)) return;
            header.Height = visible ? 24 : 5;
            commands.Visibility = footer.Visibility = resizeGrip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) return;
            if (SystemParameters.ClientAreaAnimation)
            {
                foreach (var control in new UIElement[] { commands, footer, resizeGrip })
                    control.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(135))
                    { FillBehavior = FillBehavior.Stop, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }
            UpdateTools();
        }

        internal void ApplyTheme(PetAppearance theme)
        {
            Color paper = theme.SurfaceColor;
            Color text = theme.TextColor;
            if (theme.Name == "Кудряшка")
            {
                paper = PetAppearance.Mix(theme.AccentColor, Colors.White, .73);
                text = PetAppearance.Mix(theme.SurfaceColor, Colors.Black, .2);
            }
            Color accent = theme.AccentColor;
            Color headerColor = PetAppearance.Mix(paper, accent, .72);
            Color headerInk = headerColor.R * .2126 + headerColor.G * .7152 + headerColor.B * .0722 > 140
                ? Color.FromRgb(37, 27, 33) : Colors.WhiteSmoke;
            ink = new SolidColorBrush(text);
            resizeGrip.Foreground = ink;
            selected = new SolidColorBrush(PetAppearance.Mix(paper, accent, .37));
            Frame.Background = new SolidColorBrush(paper);
            Frame.BorderBrush = new SolidColorBrush(PetAppearance.Mix(paper, accent, .25));
            header.Background = new SolidColorBrush(headerColor);
            footer.Background = new SolidColorBrush(PetAppearance.Mix(paper, accent, .09));
            footer.BorderBrush = divider.Background = Frame.BorderBrush;
            Editor.Foreground = Editor.CaretBrush = ink;
            // RTF may have materialized the old inherited text color.
            bool dirty = Dirty;
            var document = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            document.ApplyPropertyValue(TextElement.ForegroundProperty, ink);
            Dirty = dirty;
            foreach (var button in buttons.Concat(commands.Children.OfType<Button>()))
            {
                button.Foreground = buttons.Contains(button) ? ink : new SolidColorBrush(headerInk);
                if (button.Content is TextBlock label) label.Foreground = button.Foreground;
                if (button.Content is System.Windows.Shapes.Path icon) icon.Stroke = button.Foreground;
            }
            UpdateTools();
        }

        private TextDecorationCollection Decorations() =>
            Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection ?? [];

        private void ToggleDecoration(TextDecorationLocation location)
        {
            Editor.Focus();
            var current = Decorations();
            var next = new TextDecorationCollection(current.Where(decoration => decoration.Location != location));
            if (next.Count == current.Count)
                next.Add((location == TextDecorationLocation.Underline ? TextDecorations.Underline : TextDecorations.Strikethrough)[0]);
            Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
            UpdateTools();
        }

        private void UpdateTools()
        {
            var decorations = Decorations();
            var selection = Editor.Selection;
            var paragraph = selection.Start.Paragraph;
            var markedList = paragraph?.Parent is ListItem item && item.Parent is System.Windows.Documents.List;
            bool[] states =
            [
                Equals(selection.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold),
                Equals(selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic),
                decorations.Any(decoration => decoration.Location == TextDecorationLocation.Underline),
                decorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough),
                markedList,
                false
            ];
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].Background = states[i] ? selected : Brushes.Transparent;
        }

        private void Load()
        {
            if (Note.RichText is { Length: > 0 } rich)
            {
                try
                {
                    using var input = new MemoryStream(rich, writable: false);
                    new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Load(input, DataFormats.Rtf);
                    return;
                }
                catch (ArgumentException) { }
            }
            Editor.Document.Blocks.Clear();
            foreach (string line in Note.Text.Replace("\r\n", "\n").Split('\n'))
                Editor.Document.Blocks.Add(new Paragraph(new Run(line)));
        }

        internal void Save(NotesStore store)
        {
            var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            using var buffer = new MemoryStream();
            range.Save(buffer, DataFormats.Rtf);
            var updated = Note with
            {
                Text = range.Text.EndsWith("\r\n", StringComparison.Ordinal) ? range.Text[..^2] : range.Text,
                RichText = buffer.ToArray(),
                WindowSize = new NoteSize(Width, Height) is { IsValid: true } size ? size : Note.WindowSize,
                Updated = DateTimeOffset.Now
            };
            store.Put(updated);
            Note = updated;
            Dirty = false;
        }

        private void HideNote()
        {
            if (board.Flush()) Hide();
        }

        internal void Reveal(TimeSpan delay)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var rise = new TranslateTransform(0, 7);
            Frame.RenderTransform = rise;
            Frame.Opacity = 0;
            var duration = TimeSpan.FromMilliseconds(165);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var fade = new DoubleAnimation(0, 1, duration)
            {
                BeginTime = delay,
                EasingFunction = ease
            };
            fade.Completed += (_, _) => Frame.Opacity = 1;
            Frame.BeginAnimation(OpacityProperty, fade);
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, duration)
            {
                BeginTime = delay,
                EasingFunction = ease
            });
        }

        private void InsertImage()
        {
            var dialog = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "Вставить изображение" };
            if (dialog.ShowDialog(board) != true) return;
            try
            {
                using var file = File.OpenRead(dialog.FileName);
                var source = BitmapFrame.Create(file, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                double scale = Math.Min(1, Math.Min(208d / source.PixelWidth, 160d / source.PixelHeight));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = file;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = Math.Max(1, (int)(source.PixelWidth * scale));
                bitmap.DecodePixelHeight = Math.Max(1, (int)(source.PixelHeight * scale));
                file.Position = 0;
                bitmap.EndInit();
                bitmap.Freeze();
                Editor.Focus();
                Editor.Selection.Text = "";
                var inline = new InlineUIContainer(new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight }, Editor.Selection.Start);
                Editor.CaretPosition = inline.ElementEnd;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                board.Message("Не удалось открыть изображение.");
            }
        }
    }
}
