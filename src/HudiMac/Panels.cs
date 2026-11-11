using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace HudiPet;

internal sealed partial class MacPetWindow
{
    private readonly NotesStore notes = new();
    private readonly ModelSettingsStore modelSettings = new();
    private Window? notesWindow, chatWindow;

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Питомцы", OpenCollection));
        menu.Items.Add(Menu("Настройки", OpenSettings));
        menu.Items.Add(Menu("Заметки", OpenNotes));
        menu.Items.Add(Menu("Чат с моделью", OpenChat));
        menu.Items.Add(Menu("Сайты и программы", OpenFavorites));
        var actions = new MenuItem { Header = "Движения" };
        actions.Items.Add(Menu("Случайное движение", () => behavior.TriggerRandomAction()));
        foreach (var action in Enum.GetValues<PetAction>().Where(behavior.Supports))
            actions.Items.Add(Menu(ActionLabels.GetValueOrDefault(action, action.ToString()), () => behavior.Trigger(action)));
        menu.Items.Add(actions);
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("Выйти", Close));
        return menu;
    }

    private static MenuItem Menu(string title, Action run)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => run();
        return item;
    }

    private static readonly Dictionary<PetAction, string> ActionLabels = new()
    {
        [PetAction.Wink] = "Подмигнуть", [PetAction.Think] = "Задуматься",
        [PetAction.Crouch] = "Присесть", [PetAction.Stretch] = "Потянуться",
        [PetAction.Angry] = "Разозлиться", [PetAction.Sad] = "Грустить",
        [PetAction.Surprise] = "Удивиться", [PetAction.LookAround] = "Осмотреться",
        [PetAction.Dance] = "Танцевать", [PetAction.Hood] = "Поправить капюшон",
        [PetAction.Pockets] = "Руки в карманы", [PetAction.Combat] = "Боевая стойка",
        [PetAction.Salute] = "Воинское приветствие", [PetAction.March] = "Строевой шаг",
        [PetAction.Notebook] = "Достать блокнот", [PetAction.Fire] = "Потушить огонь",
        [PetAction.FireCalm] = "Развести огонь", [PetAction.FireTrick] = "Щелчок и огонь",
        [PetAction.Impatient] = "Нетерпеливо ждать", [PetAction.Temple] = "Покрутить у виска",
        [PetAction.Skeptical] = "Скрестить руки", [PetAction.Shrug] = "Пожать плечами",
        [PetAction.Indignation] = "Возмутиться", [PetAction.AdjustGlasses] = "Поправить очки",
        [PetAction.Groove] = "Танец", [PetAction.Stroll] = "Прогуляться",
        [PetAction.Ironic] = "Ухмыльнуться", [PetAction.Giggle] = "Хихикнуть",
        [PetAction.HeadTap] = "Постучать по голове", [PetAction.PeekAround] = "Обернуться",
        [PetAction.WalkAround] = "Пройтись по сторонам", [PetAction.Reproach] = "Поругаться",
        [PetAction.IndignantLean] = "Наклониться с возмущением", [PetAction.SurpriseClassic] = "Удивление"
    };

    private Window Panel(string title, Control body, double width = 400, double height = 460)
    {
        CloseOrbit();
        return new Window
        {
            Title = title, Width = width, Height = height,
            MinWidth = 300, MinHeight = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse(appearance.Surface), Foreground = Brush.Parse(appearance.Text),
            Content = new Border { Padding = new Thickness(16), Child = body }
        };
    }

    private static StackPanel Column(params Control[] children) =>
        new StackPanel { Spacing = 8 }.WithChildren(children);

    private static Button ButtonFor(string title, Action action)
    {
        var button = new Button { Content = title, CornerRadius = new(0) };
        button.Click += (_, _) => action();
        return button;
    }

    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    private static ScrollViewer Scroll(Control content) => new() { Content = content };
    private static StackPanel Row(params Control[] children) =>
        new StackPanel { Spacing = 6, Orientation = Orientation.Horizontal }.WithChildren(children);

    private async Task ShowError(string message)
    {
        if (closed) return;
        Window? dialog = null;
        dialog = Panel("HUDI", Column(Text(message), ButtonFor("Закрыть", () => dialog!.Close())), height: 210);
        await dialog.ShowDialog(this);
    }

    private async void OpenCollection()
    {
        var list = new ListBox { ItemsSource = pets.List(), SelectedItem = selected, Height = 185 };
        var status = Text("");
        Window? dialog = null;
        var use = ButtonFor("Выбрать", async () =>
        {
            if (list.SelectedItem is not LocalPet pet) return;
            if (chat.Busy) { status.Text = "Дождитесь ответа модели."; return; }
            dialog!.Close();
            await SelectPet(pet);
        });
        var import = ButtonFor("Добавить…", async () =>
        {
            if (chat.Busy) { status.Text = "Дождитесь ответа модели."; return; }
            try
            {
                var files = await dialog!.StorageProvider.OpenFilePickerAsync(new()
                {
                    Title = "Добавить питомца",
                    FileTypeFilter = [new FilePickerFileType("Пакет питомца") { Patterns = ["*.pet", "*.zip"] }]
                });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                dialog.IsEnabled = false;
                status.Text = "Проверяю пакет…";
                var pet = await pets.ImportAsync(path);
                dialog.Close();
                await SelectPet(pet);
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { if (dialog is { IsVisible: true }) dialog.IsEnabled = true; }
        });
        var remove = ButtonFor("Удалить", async () =>
        {
            if (list.SelectedItem is not LocalPet pet || pet.BuiltIn || chat.Busy) return;
            try
            {
                if (selected.Id == pet.Id) await SelectPet(PetCatalog.Hudi);
                if (selected.Id == pet.Id) return;
                pets.Remove(pet);
                list.ItemsSource = pets.List();
                list.SelectedItem = selected;
            }
            catch (Exception error) { status.Text = error.Message; }
        });
        var export = ButtonFor("Экспорт…", async () =>
        {
            if (list.SelectedItem is not LocalPet pet) return;
            try
            {
                var file = await dialog!.StorageProvider.SaveFilePickerAsync(new()
                {
                    Title = "Экспорт питомца", SuggestedFileName = pet.Name,
                    DefaultExtension = "pet",
                    FileTypeChoices = [new FilePickerFileType("Пакет питомца") { Patterns = ["*.pet"] }]
                });
                if (file?.TryGetLocalPath() is not { } path) return;
                status.Text = "Сохраняю пакет…";
                await Task.Run(() => pets.ExportWithAppearance(pet, path));
                status.Text = "Пакет сохранён.";
            }
            catch (Exception error) { status.Text = error.Message; }
        });
        dialog = Panel("Питомцы", Column(list, Row(use, import, remove), export, status), height: 375);
        await dialog.ShowDialog(this);
    }

    private async void OpenSettings()
    {
        var status = Text("");
        var activity = new CheckBox { Content = "Только при действиях пользователя", IsChecked = userActionsOnly };
        activity.Click += (_, _) =>
        {
            try
            {
                pets.SetUserActionsOnly(selected, activity.IsChecked == true);
                userActionsOnly = activity.IsChecked == true;
                if (userActionsOnly) reactions.DiscardIdle();
            }
            catch (Exception error) { activity.IsChecked = userActionsOnly; status.Text = error.Message; }
        };
        var topmost = new CheckBox { Content = "Поверх окон", IsChecked = Topmost };
        topmost.Click += (_, _) => { Topmost = topmost.IsChecked == true; SavePlacement(); };
        var size = new Slider { Minimum = 100, Maximum = 720, Value = BodyHeight };
        size.ValueChanged += (_, _) =>
        {
            var feet = BodyBottomCenter();
            ResizeBody(size.Value);
            AlignFeet(feet);
            ClampToScreen();
            SavePlacement();
        };
        var body = Column(activity, topmost, Text("Размер питомца"), size, new Separator());
        if (OperatingSystem.IsMacOS() && PetCatalog.SupportsDesktopReactions(selected))
        {
            var desktop = new CheckBox { Content = "Реагировать на мои действия", IsChecked = pets.DesktopReactionsEnabled(selected) };
            desktop.Click += (_, _) =>
            {
                try { pets.SetDesktopReactions(selected, desktop.IsChecked == true); RefreshActivity(); }
                catch (Exception error) { desktop.IsChecked = pets.DesktopReactionsEnabled(selected); status.Text = error.Message; }
            };
            body.Children.Add(desktop);
        }
        foreach (bool command in new[] { false, true })
        for (int clicks = 1; clicks <= 3; clicks++)
        {
            int count = clicks;
            var selector = new ComboBox
            {
                ItemsSource = new[] { "Ничего", "Сайты и программы", "Заметки", "Чат с моделью" },
                SelectedIndex = (int)bindings.Get(count, command), HorizontalAlignment = HorizontalAlignment.Stretch
            };
            selector.SelectionChanged += (_, _) =>
            {
                if (selector.SelectedIndex < 0) return;
                try
                {
                    var updated = bindings.With(count, command, (PetClickAction)selector.SelectedIndex);
                    controls.Save(updated);
                    bindings = updated;
                }
                catch (Exception error) { status.Text = error.Message; }
            };
            body.Children.Add(Text((command ? "⌘ + " : "") + (count switch
            {
                1 => "один клик", 2 => "двойной клик", _ => "тройной клик"
            })));
            body.Children.Add(selector);
        }
        body.Children.Add(ButtonFor("Модель API…", OpenModelSettings));
        body.Children.Add(status);
        await Panel("Настройки", Scroll(body), height: 540).ShowDialog(this);
    }

    private async void OpenModelSettings()
    {
        var (settings, key) = modelSettings.Load();
        var url = new TextBox { Text = settings.BaseUrl };
        var model = new TextBox { Text = settings.Model };
        var protocol = new ComboBox { ItemsSource = new[] { "Responses API", "Chat Completions" }, SelectedIndex = settings.Protocol == "chat" ? 1 : 0 };
        var secret = new TextBox { Text = key, PasswordChar = '●' };
        var useKey = new CheckBox { Content = "Использовать API-ключ", IsChecked = settings.UseApiKey };
        var status = Text(modelSettings.LoadNotice ?? "");
        Window? window = null;
        var save = ButtonFor("Сохранить", () =>
        {
            settings.BaseUrl = url.Text?.Trim() ?? "";
            settings.Model = model.Text?.Trim() ?? "";
            settings.Protocol = protocol.SelectedIndex == 1 ? "chat" : "responses";
            settings.UseApiKey = useKey.IsChecked == true;
            try { modelSettings.Save(settings, settings.UseApiKey ? secret.Text ?? "" : ""); window!.Close(); }
            catch (Exception error) { status.Text = error.Message; }
        });
        window = Panel("Модель API", Column(Text("Адрес API"), url, Text("Модель"), model,
            Text("Протокол"), protocol, useKey, secret, save, status), height: 410);
        var owner = OwnedWindows.LastOrDefault(candidate => candidate.IsVisible) ?? this;
        await window.ShowDialog(owner);
    }

    private void OpenNotes()
    {
        if (notesWindow is { IsVisible: true }) { notesWindow.Activate(); return; }
        var list = new ListBox { Height = 130 };
        var edit = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 155, MaxLength = NotesStore.MaxTextLength };
        var status = Text("");
        PetNote? current = null;
        bool loading = false;
        void Refresh(string? id = null)
        {
            var entries = notes.Read();
            list.ItemsSource = entries.Select(note => new NoteItem(note)).ToArray();
            list.SelectedItem = list.ItemsSource.Cast<NoteItem>().FirstOrDefault(item => item.Note.Id == id);
        }
        list.SelectionChanged += (_, _) =>
        {
            loading = true;
            current = (list.SelectedItem as NoteItem)?.Note;
            edit.Text = current?.Text ?? "";
            edit.IsEnabled = current is not null;
            loading = false;
        };
        edit.TextChanged += (_, _) =>
        {
            if (loading || current is not { } note || note.Text == edit.Text) return;
            try { current = note with { Text = edit.Text ?? "", Updated = DateTimeOffset.UtcNow }; notes.Put(current); status.Text = ""; }
            catch (Exception error) { status.Text = error.Message; }
        };
        var add = ButtonFor("+", () =>
        {
            var now = DateTimeOffset.UtcNow;
            var note = new PetNote(Guid.NewGuid().ToString("N"), "", now, now);
            try { notes.Put(note); Refresh(note.Id); edit.Focus(); }
            catch (Exception error) { status.Text = error.Message; }
        });
        var remove = ButtonFor("Удалить", () =>
        {
            if (current is not { } note) return;
            try { notes.Delete(note.Id); Refresh(); }
            catch (Exception error) { status.Text = error.Message; }
        });
        try { Refresh(); }
        catch (Exception error) { status.Text = error.Message; }
        edit.IsEnabled = current is not null;
        notesWindow = Panel("Заметки", Column(Row(add, remove), list, edit, status), height: 405);
        notesWindow.Closed += (_, _) => notesWindow = null;
        notesWindow.Show(this);
    }

    private sealed record NoteItem(PetNote Note)
    {
        public override string ToString() => Note.Title;
    }

    private void OpenChat()
    {
        if (chatWindow is { IsVisible: true }) { chatWindow.Activate(); return; }
        var transcript = Column();
        var scroll = Scroll(transcript);
        var input = new TextBox { Watermark = "Написать сообщение", TextWrapping = TextWrapping.Wrap, MaxHeight = 155 };
        var status = Text("");
        var send = ButtonFor("Отправить", () => _ = Send());
        var cancel = ButtonFor("Отменить", chat.Cancel);
        var clear = ButtonFor("Очистить чат", chat.ClearHistory);
        async Task Send()
        {
            if (chat.Busy || string.IsNullOrWhiteSpace(input.Text)) return;
            string question = input.Text;
            var (settings, key) = modelSettings.Load();
            try { settings.Validate(key); }
            catch (ModelException error) { status.Text = error.Message; return; }
            input.Text = "";
            await chat.SendAsync(question, settings, key);
        }
        void Refresh()
        {
            transcript.Children.Clear();
            void Add(string role, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                bool user = role == "user";
                transcript.Children.Add(new Border
                {
                    Padding = new Thickness(9), BorderThickness = new Thickness(1),
                    BorderBrush = Brush.Parse(appearance.Accent),
                    Background = new SolidColorBrush(Color.Parse(appearance.Text), user ? .08 : .02),
                    Child = Column(new TextBlock { Text = user ? "Вы" : selected.Name, FontWeight = FontWeight.SemiBold },
                        new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap })
                });
            }
            foreach (var message in chat.Messages) Add(message.Role, message.Content);
            if (chat.Busy || chat.State == ChatState.Answered && chat.Messages.Count == 0)
            {
                Add("user", chat.LastQuestion);
                Add("assistant", chat.PartialAnswer);
            }
            status.Text = chat.State switch { ChatState.Thinking => "Питомец думает…", ChatState.Error => chat.Error, _ => "" };
            send.IsEnabled = clear.IsEnabled = !chat.Busy;
            cancel.IsVisible = chat.Busy;
            Avalonia.Threading.Dispatcher.UIThread.Post(scroll.ScrollToEnd);
        }
        chat.Changed += Refresh;
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { e.Handled = true; _ = Send(); }
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto") };
        layout.Children.Add(scroll);
        var composer = Column(new Separator(), input, status);
        Grid.SetRow(composer, 1);
        layout.Children.Add(composer);
        var commands = Row(send, cancel, clear);
        Grid.SetRow(commands, 2);
        layout.Children.Add(commands);
        var settingsButton = ButtonFor("Модель API…", OpenModelSettings);
        Grid.SetRow(settingsButton, 3);
        layout.Children.Add(settingsButton);
        chatWindow = Panel("Чат с моделью", layout, height: 510);
        chatWindow.Closed += (_, _) => { chat.Changed -= Refresh; chatWindow = null; };
        Refresh();
        chatWindow.Show(this);
    }
}

internal static class PanelChildren
{
    internal static StackPanel WithChildren(this StackPanel panel, IEnumerable<Control> children)
    {
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
}
