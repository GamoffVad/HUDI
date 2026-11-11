using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace HudiPet;

internal sealed partial class MacPetWindow
{
    private readonly Dictionary<string, MacPanel> notePages = [];
    private readonly List<Action> noteThemeUpdates = [];
    private PetNote? deletedNote;
    private Button? notesUndo;

    private void OpenNotes()
    {
        if (notesWindow is not { IsVisible: true })
        {
            var status = Text("");
            status.IsVisible = false;
            var undo = ButtonFor("Отменить удаление", () =>
            {
                if (deletedNote is not { } deleted) return;
                try { notes.Put(deleted); deletedNote = null; if (notesUndo is not null) notesUndo.IsVisible = false; OpenNotes(); }
                catch (Exception error) { status.Text = error.Message; status.IsVisible = true; }
            });
            undo.IsVisible = deletedNote is not null;
            notesUndo = undo;
            var add = ButtonFor("+", () =>
            {
                var now = DateTimeOffset.Now;
                var note = new PetNote(Guid.NewGuid().ToString("N"), "", now, now);
                try { notes.Put(note); OpenNotePage(note, notePages.Count, true); }
                catch (Exception error) { status.Text = error.Message; status.IsVisible = true; }
            });
            AutomationProperties.SetName(add, "Новая заметка");
            var strip = Panel("Заметки", Column(Row(add, undo), status), 185, 104, preferAbove: true);
            notesWindow = strip;
            strip.Closed += (_, _) => { notesWindow = null; notesUndo = null; };
            strip.Show(this);
        }
        try
        {
            int index = 0;
            foreach (var note in notes.Read()) OpenNotePage(note, index++, false);
        }
        catch (Exception error) { _ = ShowError(error.Message); }
    }

    private void OpenNotePage(PetNote note, int index, bool activate)
    {
        if (notesWindow is not { IsVisible: true } strip || notePages.ContainsKey(note.Id)) return;
        bool formatted = note.RichText is { Length: > 0 };
        var editor = new TextBox
        {
            Text = note.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = NotesStore.MaxTextLength,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(9, 6),
            IsReadOnly = formatted
        };
        AutomationProperties.SetName(editor, "Текст заметки");
        var status = Text("");
        status.IsVisible = false;
        var accent = new Border { Height = 4 };
        var grip = new Thumb
        {
            Width = 22, Height = 24, Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            Background = Brushes.Transparent
        };
        grip.Template = new FuncControlTemplate<Thumb>((_, _) =>
            new Border { Background = Brushes.Transparent, Child = new TextBlock
            { Text = "◢", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        AutomationProperties.SetName(grip, "Изменить размер заметки");
        var bar = new DockPanel { Height = 25, IsVisible = false };
        var remove = ButtonFor("Удалить", () => { });
        remove.Padding = new Thickness(7, 1);
        remove.Background = Brushes.Transparent;
        remove.BorderThickness = new Thickness(0);
        DockPanel.SetDock(grip, Dock.Right);
        DockPanel.SetDock(remove, Dock.Left);
        bar.Children.Add(grip);
        bar.Children.Add(remove);
        if (formatted)
        {
            var copy = ButtonFor("Редактировать копию", () =>
            {
                var now = DateTimeOffset.Now;
                var plain = new PetNote(Guid.NewGuid().ToString("N"), note.Text, now, now) { WindowSize = note.WindowSize };
                try { notes.Put(plain); OpenNotePage(plain, notePages.Count, true); }
                catch (Exception error) { status.Text = error.Message; status.IsVisible = true; }
            });
            copy.Padding = new Thickness(4, 1);
            copy.Background = Brushes.Transparent;
            copy.BorderThickness = new Thickness(0);
            DockPanel.SetDock(copy, Dock.Left);
            bar.Children.Add(copy);
        }
        var body = new DockPanel();
        DockPanel.SetDock(accent, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        DockPanel.SetDock(status, Dock.Bottom);
        body.Children.Add(accent);
        body.Children.Add(bar);
        body.Children.Add(status);
        body.Children.Add(editor);
        var size = note.WindowSize is { IsValid: true } saved ? saved : new NoteSize(248, 132);
        var page = Panel("Заметка", body, size.Width, size.Height);
        page.SetNoteChrome(false);
        page.ShowActivated = activate;
        void Theme()
        {
            accent.Background = Brush.Parse(appearance.Accent);
            editor.Foreground = Brush.Parse(appearance.Text);
        }
        Theme();
        noteThemeUpdates.Add(Theme);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        bool dirty = false;
        bool Flush()
        {
            timer.Stop();
            if (!dirty) return true;
            try
            {
                var size = new NoteSize(page.Width, page.Height);
                string value = editor.Text ?? "";
                var updated = note with
                {
                    Text = formatted ? note.Text : value,
                    WindowSize = size.IsValid ? size : note.WindowSize, Updated = DateTimeOffset.Now
                };
                notes.Put(updated);
                note = updated;
                dirty = false;
                status.IsVisible = false;
                return true;
            }
            catch (Exception error)
            {
                status.Text = error.Message;
                status.IsVisible = true;
                return false;
            }
        }
        void Changed() { dirty = true; timer.Stop(); timer.Start(); }
        editor.TextChanged += (_, _) => { if (editor.Text != note.Text) Changed(); };
        timer.Tick += (_, _) => Flush();
        grip.DragDelta += (_, args) =>
        {
            var screen = Screens.ScreenFromWindow(page) ?? Screens.Primary;
            if (screen is null) return;
            double scale = OperatingSystem.IsMacOS() ? 1 : screen.Scaling;
            page.Width = Math.Clamp(page.Width + args.Vector.X, NoteSize.MinWidth,
                Math.Max(NoteSize.MinWidth, Math.Min(NoteSize.MaxWidth, (screen.WorkingArea.Right - page.Position.X) / scale)));
            page.Height = Math.Clamp(page.Height + args.Vector.Y, NoteSize.MinHeight,
                Math.Max(NoteSize.MinHeight, Math.Min(NoteSize.MaxHeight, (screen.WorkingArea.Bottom - page.Position.Y) / scale)));
            Changed();
        };
        remove.Click += (_, _) =>
        {
            if (!Flush()) return;
            try
            {
                notes.Delete(note.Id);
                deletedNote = note;
                page.Close();
                if (notesUndo is not null) notesUndo.IsVisible = true;
            }
            catch (Exception error) { status.Text = error.Message; status.IsVisible = true; }
        };
        page.Activated += async (_, _) =>
        {
            page.SetNoteChrome(true);
            accent.IsVisible = false;
            bar.IsVisible = true;
            await MacPanel.FadeIn(bar);
        };
        page.Deactivated += (_, _) =>
        {
            page.SetNoteChrome(false);
            accent.IsVisible = true;
            bar.IsVisible = false;
        };
        page.Closing += (_, args) => { if (!Flush()) args.Cancel = true; };
        page.Closed += (_, _) =>
        {
            timer.Stop();
            notePages.Remove(note.Id);
            noteThemeUpdates.Remove(Theme);
        };
        notePages.Add(note.Id, page);
        var monitor = Screens.ScreenFromWindow(strip) ?? Screens.Primary;
        if (monitor is not null)
        {
            var work = monitor.WorkingArea;
            double scale = OperatingSystem.IsMacOS() ? 1 : monitor.Scaling;
            int step = (int)Math.Ceiling((page.Width + 9) * scale);
            int stride = (int)Math.Ceiling((page.Height + 9) * scale);
            int columns = Math.Max(1, work.Width / step);
            int rows = Math.Max(1, (work.Height - 120) / stride);
            int slot = index % (columns * rows);
            int offset = index / (columns * rows) % 7 * 9;
            page.Position = new(work.X + slot % columns * step + offset,
                Math.Min(work.Bottom - (int)(page.Height * scale), work.Y + 90 + slot / columns * stride + offset));
        }
        page.Show(strip);
        _ = page.Reveal();
        if (activate) editor.Focus();
    }
}
