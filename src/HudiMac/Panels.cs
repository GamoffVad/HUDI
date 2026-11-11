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

    private Window? menuWindow;
    private readonly List<MacPanel> interactivePanels = [];

    private void OpenMenu()
    {
        if (menuWindow is { IsVisible: true }) { menuWindow.Close(); return; }
        var body = Column();
        body.Spacing = 3;
        var menu = Panel(selected.Name, Scroll(body), 270, 355, preferAbove: true);
        menuWindow = menu;
        void Add(string label, Action action, bool enabled = true)
        {
            var button = ButtonFor(label, action);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Background = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.Padding = new Thickness(8, 5);
            button.IsEnabled = enabled;
            body.Children.Add(button);
        }
        void Run(Action action) { menu.Close(); action(); }
        void Main()
        {
            menu.Title = selected.Name;
            body.Children.Clear();
            Add("Открыть чат", () => Run(() => OpenChat()));
            if (!string.IsNullOrWhiteSpace(chat.Answer)) Add("Последний ответ", () => Run(() => OpenChat(focus: false)));
            Add("Заметки", () => Run(OpenNotes));
            Add("Сайты и программы…", () => Run(OpenFavorites));
            if (chat.Busy) Add("Отменить запрос", () => Run(chat.Cancel));
            Add("Движения  ›", Movements);
            Add("Модель и API", () => Run(OpenModelSettings));
            Add("Управление", () => Run(OpenSettings));
            Add("Питомцы", () => Run(OpenCollection));
            Add("Внешний вид", () => Run(OpenAppearance));
            body.Children.Add(new Separator());
            Add("Закрыть питомца", () => Run(Close));
        }
        void Movements()
        {
            menu.Title = "Движения";
            body.Children.Clear();
            Add("‹  Назад", Main);
            Add("Случайное движение", () => Run(() => behavior.TriggerRandomAction()), !chat.Busy);
            foreach (var action in Enum.GetValues<PetAction>().Where(behavior.Supports))
            {
                bool edge = PetBehavior.IsEdgeAction(action);
                Add(ActionLabels.GetValueOrDefault(action, action.ToString()), () => Run(() =>
                {
                    if (edge) PlayDesktopEdge(action);
                    else behavior.Trigger(action);
                }), !chat.Busy && (!edge || !behavior.IsPlaying && CanPlaceDesktopEdge(action, out _)));
            }
        }
        menu.Closed += (_, _) => menuWindow = null;
        menu.Deactivated += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (menu.IsVisible && !menu.IsActive) menu.Close();
        });
        Main();
        menu.Show(this);
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
        [PetAction.IndignantLean] = "Наклониться с возмущением", [PetAction.SurpriseClassic] = "Удивление",
        [PetAction.QuietIdle] = "Тихо ждать", [PetAction.HairBrush] = "Убрать прядь с лица",
        [PetAction.Doze] = "Клевать носом", [PetAction.Cheer] = "Тихо радоваться",
        [PetAction.EdgeLean] = "Опереться на край", [PetAction.EdgePeek] = "Выглянуть из-за края",
        [PetAction.Gymnastics] = "Сделать гимнастику", [PetAction.Peek] = "Подглядеть"
    };

    private MacPanel Panel(string title, Control body, double width = 400, double height = 460, bool preferAbove = false)
    {
        CloseOrbit();
        var screen = Screens.ScreenFromPoint(BodyBottomCenter()) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var panel = new MacPanel(title, body, width, height) { Topmost = preferTopmost };
        panel.ApplyTheme(appearance, dialogTransparency, matteDialogs);
        panel.Opened += (_, _) =>
        {
            interactivePanels.Add(panel);
            RefreshPetLayer();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (panel.ShowActivated && panel.IsVisible && interactivePanels.LastOrDefault() == panel) panel.Activate();
            }, Avalonia.Threading.DispatcherPriority.Input);
        };
        panel.Closed += (_, _) =>
        {
            interactivePanels.Remove(panel);
            RefreshPetLayer();
        };
        if (screen is null)
        {
            panel.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return panel;
        }

        var work = screen.WorkingArea;
        var scale = OperatingSystem.IsMacOS() ? 1 : screen.Scaling;
        panel.Width = Math.Min(width, work.Width / scale);
        panel.Height = Math.Min(height, work.Height / scale);
        var petBounds = new PixelRect(Position,
            new PixelSize((int)Math.Ceiling(Width * DesktopUnitScale), (int)Math.Ceiling(Height * DesktopUnitScale)));
        var panelSize = new PixelSize(Math.Min(work.Width, (int)Math.Ceiling(panel.Width * scale)),
            Math.Min(work.Height, (int)Math.Ceiling(panel.Height * scale)));
        panel.Position = PlacePanel(work, petBounds, panelSize, (int)Math.Ceiling(12 * scale), preferAbove);
        return panel;
    }

    private void RefreshPetLayer()
    {
        if (closed) return;
        Topmost = preferTopmost && interactivePanels.Count == 0;
        foreach (var panel in interactivePanels) panel.Topmost = preferTopmost;
    }

    private void RefreshPanelTheme()
    {
        foreach (var panel in interactivePanels) panel.ApplyTheme(appearance, dialogTransparency, matteDialogs);
        foreach (var update in noteThemeUpdates) update();
    }

    private void OpenAppearance()
    {
        var size = new Slider { Minimum = 100, Maximum = 720, Value = BodyHeight };
        size.ValueChanged += (_, _) =>
        {
            var feet = BodyBottomCenter();
            ResizeBody(size.Value);
            AlignFeet(feet);
            ClampToScreen();
            SavePlacement();
        };
        var transparency = new Slider { Minimum = 0, Maximum = 90, Value = dialogTransparency };
        transparency.ValueChanged += (_, _) =>
        {
            dialogTransparency = transparency.Value;
            RefreshPanelTheme();
            SavePlacement();
        };
        var matte = new CheckBox { Content = "Матовый фон", IsChecked = matteDialogs };
        matte.Click += (_, _) => { matteDialogs = matte.IsChecked == true; RefreshPanelTheme(); SavePlacement(); };
        var top = new CheckBox { Content = "Поверх окон", IsChecked = preferTopmost };
        top.Click += (_, _) => { preferTopmost = top.IsChecked == true; RefreshPetLayer(); SavePlacement(); };
        var pace = new ComboBox { ItemsSource = new[] { "Спокойный", "Обычный", "Бодрый" },
            SelectedIndex = playbackSpeed < 1 ? 0 : playbackSpeed > 1 ? 2 : 1 };
        pace.SelectionChanged += (_, _) =>
        {
            playbackSpeed = pace.SelectedIndex switch { 0 => .75, 2 => 1.3, _ => 1 };
            SavePlacement();
        };
        Panel("Внешний вид", Column(Field("Размер питомца", size), Field("Прозрачность", transparency), matte,
            new Separator(), Field("Темп движения", pace), top), 350, 264).Show(this);
    }

    internal static PixelPoint PlacePanel(PixelRect work, PixelRect pet, PixelSize size, int gap, bool preferAbove = false)
    {
        int centerX = pet.X + (pet.Width - size.Width) / 2;
        int centerY = pet.Y + (pet.Height - size.Height) / 2;
        int x = Math.Clamp(centerX, work.X, Math.Max(work.X, work.Right - size.Width));
        int y = Math.Clamp(centerY, work.Y, Math.Max(work.Y, work.Bottom - size.Height));

        if (preferAbove && pet.Y - gap - size.Height >= work.Y) return new(x, Math.Min(work.Bottom - size.Height, pet.Y - gap - size.Height));
        if (pet.Right + gap + size.Width <= work.Right) return new(Math.Max(work.X, pet.Right + gap), y);
        if (pet.X - gap - size.Width >= work.X) return new(Math.Min(work.Right - size.Width, pet.X - gap - size.Width), y);
        if (pet.Y - gap - size.Height >= work.Y) return new(x, Math.Min(work.Bottom - size.Height, pet.Y - gap - size.Height));
        if (pet.Bottom + gap + size.Height <= work.Bottom) return new(x, Math.Max(work.Y, pet.Bottom + gap));
        return new(work.X + Math.Max(0, work.Width - size.Width) / 2,
            work.Y + Math.Max(0, work.Height - size.Height) / 2);
    }

    private static StackPanel Column(params Control[] children) =>
        new StackPanel { Spacing = 8 }.WithChildren(children);

    private static Grid Field(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,164") };
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0) });
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

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
        var entries = pets.List();
        var list = new ListBox { ItemsSource = entries, SelectedItem = entries.FirstOrDefault(p => p.Id == selected.Id), Height = 185 };
        var version = Text("");
        var status = Text("");
        Window? dialog = null;
        var use = ButtonFor("Выбрать", async () =>
        {
            if (list.SelectedItem is not LocalPet pet) return;
            if (chat.Busy) { status.Text = "Дождитесь ответа модели."; return; }
            dialog!.Close();
            await SelectPet(pet);
        });
        void ShowRevision()
        {
            version.Text = "";
            if (list.SelectedItem is not LocalPet pet) return;
            bool update = pet.Id == selected.Id && !pet.SameRevision(selected);
            use.Content = update ? "Применить обновление" : "Выбрать";
            try
            {
                var current = pet.SameRevision(selected);
                var sprites = current ? library : pets.Open(pet);
                try { version.Text = $"Версия питомца: {pet.VersionLabel(sprites)}" + (update ? $"\nСейчас запущена: {selected.VersionLabel(library)}" : ""); }
                finally { if (!current && !pet.BuiltIn) sprites.Dispose(); }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { version.Text = "Не удалось прочитать версию пакета."; }
        }
        list.SelectionChanged += (_, _) => ShowRevision();
        ShowRevision();
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
        dialog = Panel("Питомцы", Column(list, version, Row(use, import, remove), export, status), height: 420);
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
        var body = Column(activity);
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
            var label = (command ? "⌘ + " : "") + (count switch
            {
                1 => "один клик", 2 => "двойной клик", _ => "тройной клик"
            });
            body.Children.Add(Field(label, selector));
        }
        body.Children.Add(ButtonFor("Модель API…", OpenModelSettings));
        body.Children.Add(status);
        await Panel("Управление кликами", Scroll(body), width: 380, height: 380).ShowDialog(this);
    }

    private async void OpenModelSettings()
    {
        var (settings, key) = modelSettings.Load();
        var provider = new ComboBox { ItemsSource = ProviderPresets.All.Select(preset => preset.Label).ToArray(),
            SelectedIndex = ProviderPresets.Index(settings.Provider) };
        var url = new TextBox { Text = settings.BaseUrl };
        var model = new TextBox { Text = settings.Model };
        var protocol = new ComboBox { ItemsSource = new[] { "Responses API", "Chat Completions" }, SelectedIndex = settings.Protocol == "chat" ? 1 : 0 };
        var secret = new TextBox { Text = key, PasswordChar = '●' };
        var useKey = new CheckBox { Content = "Использовать API-ключ", IsChecked = settings.UseApiKey };
        var stream = new CheckBox { Content = "Потоковый ответ", IsChecked = settings.Stream };
        var keepHistory = new CheckBox { Content = "Помнить диалог до закрытия", IsChecked = settings.KeepHistory };
        var instructions = new TextBox { Text = settings.SystemPrompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 64 };
        var limit = new NumericUpDown { Value = settings.MaxOutputTokens, Minimum = 128, Maximum = 32768, Increment = 128 };
        var timeout = new NumericUpDown { Value = settings.TimeoutSeconds, Minimum = 10, Maximum = 1800, Increment = 10 };
        var status = Text(modelSettings.LoadNotice ?? "");
        Window? window = null;
        using var lifetime = new CancellationTokenSource();
        string originalOrigin;
        try { originalOrigin = settings.Origin; } catch (ModelException) { originalOrigin = ""; }
        void ReadForm()
        {
            settings.BaseUrl = url.Text?.Trim() ?? "";
            settings.Model = model.Text?.Trim() ?? "";
            settings.Protocol = protocol.SelectedIndex == 1 ? "chat" : "responses";
            settings.UseApiKey = useKey.IsChecked == true;
            settings.Stream = stream.IsChecked == true;
            settings.KeepHistory = keepHistory.IsChecked == true;
            settings.SystemPrompt = instructions.Text ?? "";
            settings.MaxOutputTokens = (int)(limit.Value ?? 2048);
            settings.TimeoutSeconds = (int)(timeout.Value ?? 120);
        }
        string KeyForCurrentServer()
        {
            if (!settings.UseApiKey) return "";
            string current = secret.Text ?? "";
            if (current.Length > 0 && current == key && settings.Origin != originalOrigin)
                throw new ModelException("Введите ключ для нового адреса сервера.");
            return current;
        }
        var save = ButtonFor("Сохранить", () =>
        {
            try { ReadForm(); modelSettings.Save(settings, KeyForCurrentServer()); window!.Close(); }
            catch (Exception error) { status.Text = error.Message; }
        });
        provider.SelectionChanged += (_, _) =>
        {
            if (provider.SelectedIndex < 0) return;
            var chosen = ProviderPresets.All[provider.SelectedIndex];
            settings.Provider = chosen.Id;
            if (chosen.Id == "compatible") return;
            url.Text = chosen.BaseUrl;
            model.Text = chosen.Model;
            protocol.SelectedIndex = chosen.Protocol == "chat" ? 1 : 0;
            useKey.IsChecked = chosen.UseApiKey;
            settings.SendTemperature = chosen.SendTemperature;
            settings.Temperature = chosen.Temperature;
            settings.ChatTokenParameter = chosen.TokenParameter;
            limit.Value = chosen.Limit;
            timeout.Value = chosen.Timeout;
            secret.Text = "";
        };
        var available = new ComboBox { IsVisible = false };
        available.SelectionChanged += (_, _) => { if (available.SelectedItem is string choice) model.Text = choice; };
        var test = ButtonFor("Проверить связь", async () =>
        {
            try
            {
                ReadForm();
                var models = await modelClient.ListModelsAsync(settings.Copy(), KeyForCurrentServer(), lifetime.Token);
                if (!window!.IsVisible) return;
                available.ItemsSource = models;
                available.IsVisible = models.Length > 0;
                status.Text = models.Length > 0 ? $"Доступно моделей: {models.Length}" : "Сервер не вернул список моделей.";
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { status.Text = error.Message; }
        });
        var form = Column(Text($"Версия HUDI: {AppRelease.Version}"), Field("Провайдер", provider), Field("Адрес сервера", url),
            Field("Модель", model), available, Field("Формат API", protocol), Field("API-ключ", secret), useKey, stream,
            keepHistory, Field("Лимит ответа", limit), Field("Тайм-аут, с", timeout), Text("Системная инструкция"),
            instructions, Row(test, save), status);
        window = Panel("Модель и API", Scroll(form), height: 550);
        window.Closed += (_, _) => lifetime.Cancel();
        var owner = (Window?)interactivePanels.LastOrDefault(candidate => candidate.IsVisible && candidate.IsActive) ?? this;
        await window.ShowDialog(owner);
    }

    private void OpenChat(bool focus = true)
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
        chatWindow = Panel("Чат с моделью", layout, height: 405, preferAbove: true);
        chatWindow.Closed += (_, _) => { chat.Changed -= Refresh; chatWindow = null; };
        Refresh();
        chatWindow.Show(this);
        if (focus) input.Focus();
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
