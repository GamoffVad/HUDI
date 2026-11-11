using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HudiPet;

internal sealed class FavoriteSitesWindow:GlassWindow
{
    private readonly FavoriteSitesStore store;
    private readonly Action changed;
    private readonly StackPanel list=new();
    private readonly TextBox address=Ui.Input("","Адрес сайта");
    private readonly ComboBox targetKind=Ui.Select(["Сайт","Программа"],0);
    private readonly TextBlock addressLabel=Ui.Text("Адрес сайта",12,Ui.Muted);
    private readonly Button browseProgram;
    private readonly TextBox sphereText=Ui.Input("","Текст на сфере");
    private readonly Border iconPreview=new(){Width=32,Height=32};
    private readonly TextBlock status=Ui.Text("",12,Ui.Error);
    private readonly Button save;
    private readonly Slider maximumSize;
    private readonly TextBlock sizeLabel=Ui.Text("",12,Ui.Accent);
    private readonly DispatcherTimer sizeSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(300)};
    private bool sizeDirty,syncingSize;
    private string? editingId;
    private string icon="";
    private string? libraryIcon;
    private readonly PetAppearance appearance;
    internal FavoriteSitesWindow(PetWindow pet,FavoriteSitesStore store,Action changed):base(pet,"Сайты и программы",350,"\uE71B")
    {
        this.store=store;this.changed=changed;appearance=pet.Appearance;SettingsAppearance.Apply(this);
        ((StackPanel)Surface.Child).Margin=new Thickness(14);Surface.CornerRadius=new CornerRadius(0);Heading.FontSize=15;Header.Margin=new Thickness(0,0,0,8);
        var sizeCaption=new DockPanel{Margin=new Thickness(0,3,0,0)};DockPanel.SetDock(sizeLabel,Dock.Right);sizeCaption.Children.Add(sizeLabel);sizeCaption.Children.Add(Ui.Text("Максимальный размер сферы",12,Ui.Muted));Body.Children.Add(sizeCaption);
        maximumSize=new Slider{Minimum=FavoriteSitesStore.MinimumSphereSize,Maximum=FavoriteSitesStore.MaximumAllowedSphereSize,Value=SitesArc.DefaultMaximumSize(pet.DisplayHeight),SmallChange=1,LargeChange=8,TickFrequency=1,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,Margin=new Thickness(0,8,0,14),Style=(Style)FindResource("PetSizeSlider")};
        System.Windows.Automation.AutomationProperties.SetName(maximumSize,"Максимальный размер сферы");sizeLabel.Text=Math.Round(maximumSize.Value).ToString();Body.Children.Add(maximumSize);
        maximumSize.ValueChanged+=(_,_)=>{sizeLabel.Text=Math.Round(maximumSize.Value).ToString();if(syncingSize)return;sizeDirty=true;sizeSaveTimer.Stop();sizeSaveTimer.Start();};
        sizeSaveTimer.Tick+=(_,_)=>SaveSize();Closed+=(_,_)=>sizeSaveTimer.Stop();
        Body.Children.Add(new ScrollViewer{Content=list,MaxHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        targetKind.Style=(Style)FindResource("SettingsSelect");Ui.Field(Body,"Добавить в избранное",targetKind);
        System.Windows.Automation.AutomationProperties.SetName(targetKind,"Тип избранного");
        address.Style=(Style)FindResource("SettingsInput");address.MaxLength=4096;
        addressLabel.Margin=new Thickness(0,10,0,6);Body.Children.Add(addressLabel);
        var addressRow=new DockPanel();browseProgram=Ui.Button("…",ChooseProgram,quiet:true);browseProgram.Visibility=Visibility.Collapsed;DockPanel.SetDock(browseProgram,Dock.Right);addressRow.Children.Add(browseProgram);addressRow.Children.Add(address);Body.Children.Add(addressRow);
        System.Windows.Automation.AutomationProperties.SetName(browseProgram,"Выбрать программу");
        targetKind.SelectionChanged+=(_,_)=>{bool program=targetKind.SelectedIndex==1;addressLabel.Text=program?"Путь к программе или ярлыку":"Адрес сайта";System.Windows.Automation.AutomationProperties.SetName(address,addressLabel.Text);browseProgram.Visibility=program?Visibility.Visible:Visibility.Collapsed;};
        sphereText.Style=(Style)FindResource("SettingsInput");sphereText.MaxLength=FavoriteSitesStore.MaximumTextLength;sphereText.TextChanged+=(_,_)=>UpdatePreview();Ui.Field(Body,"Текст на сфере (вместо значка)",sphereText);
        var iconRow=new DockPanel();var choose=Ui.Button("Выбрать значок",ChooseIcon,quiet:true);choose.Margin=new Thickness(12,0,0,0);DockPanel.SetDock(iconPreview,Dock.Left);iconRow.Children.Add(iconPreview);iconRow.Children.Add(choose);Ui.Field(Body,"Предпросмотр",iconRow);UpdatePreview();
        var library=Ui.Button("Значки питомца · 10",OpenIconLibrary,quiet:true);library.Margin=new Thickness(0,6,0,0);Body.Children.Add(library);
        save=Ui.Button("Добавить",Save,primary:true);var cancel=Ui.Button("+",()=>Edit(null),quiet:true);System.Windows.Automation.AutomationProperties.SetName(cancel,"Добавить в избранное");
        var actions=Ui.Row(save,cancel);actions.Margin=new Thickness(0,14,0,0);Body.Children.Add(actions);status.Margin=new Thickness(0,8,0,0);status.Visibility=Visibility.Collapsed;Body.Children.Add(status);Refresh();
    }
    private void Error(string message){status.Text=message;status.Visibility=Visibility.Visible;}
    private void Refresh()
    {
        list.Children.Clear();status.Visibility=Visibility.Collapsed;
        try
        {
            var sites=store.Read();save.IsEnabled=maximumSize.IsEnabled=true;
            if(!sizeDirty){syncingSize=true;try{maximumSize.Value=store.MaximumSphereSize??SitesArc.DefaultMaximumSize(((PetWindow)Owner).DisplayHeight);}finally{syncingSize=false;}}
            foreach(var site in sites)
            {
                var row=new Grid{Margin=new Thickness(0,2,0,2)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var content=new DockPanel();var symbol=SiteOrb.CreateFace(appearance.IconFor(site),site.Text,24);symbol.Margin=new Thickness(0,0,8,0);DockPanel.SetDock(symbol,Dock.Left);content.Children.Add(symbol);content.Children.Add(new TextBlock{Text=site.Name,Foreground=Ui.Accent,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});
                var edit=Ui.Button(site.Name,()=>Edit(site),quiet:true);edit.Content=content;edit.HorizontalContentAlignment=HorizontalAlignment.Stretch;edit.Padding=new Thickness(6);row.Children.Add(edit);
                var pencil=Ui.Button("✎",()=>Edit(site),quiet:true);pencil.Padding=new Thickness(7);System.Windows.Automation.AutomationProperties.SetName(pencil,"Изменить "+site.Name);Grid.SetColumn(pencil,1);row.Children.Add(pencil);
                var remove=Ui.Button("×",()=>Remove(site),quiet:true);remove.Padding=new Thickness(7);System.Windows.Automation.AutomationProperties.SetName(remove,"Удалить "+site.Name);Grid.SetColumn(remove,2);row.Children.Add(remove);list.Children.Add(row);
            }
            if(store.RecoveredBackup)Error("Список восстановлен из резервной копии.");
        }
        catch(Exception e) when(FavoriteSitesStore.IsDataError(e)){save.IsEnabled=maximumSize.IsEnabled=false;Error(e.Message);}
    }
    private bool SaveSize()
    {
        sizeSaveTimer.Stop();if(!sizeDirty)return true;
        try{store.SetMaximumSphereSize(maximumSize.Value);sizeDirty=false;changed();status.Visibility=Visibility.Collapsed;return true;}
        catch(Exception e) when(FavoriteSitesStore.IsDataError(e)){Error("Не удалось сохранить размер сферы. Попробуйте ещё раз.");return false;}
    }
    internal bool FlushSize()=>SaveSize();
    protected override void OnClosing(CancelEventArgs e){if(!SaveSize())e.Cancel=true;base.OnClosing(e);}
    private void Edit(FavoriteSite? site)
    {
        editingId=site?.Id;libraryIcon=site?.LibraryIcon;targetKind.SelectedIndex=site is not null&&FavoriteSitesStore.IsProgram(site)?1:0;address.Text=site?.Url??"";icon=site is null?"":appearance.IconFor(site);sphereText.Text=site?.Text??"";UpdatePreview();save.Content=site is null?"Добавить":"Сохранить";status.Visibility=Visibility.Collapsed;address.Focus();
    }
    private void ChooseProgram()
    {
        var picker=new OpenFileDialog{Title="Выберите программу или ярлык",Filter="Программы и ярлыки|*.exe;*.lnk",CheckFileExists=true,DereferenceLinks=false};
        if(picker.ShowDialog(this)!=true)return;
        address.Text=picker.FileName;
        if(icon.Length==0&&string.IsNullOrWhiteSpace(sphereText.Text))sphereText.Text=Path.GetFileNameWithoutExtension(picker.FileName)[..Math.Min(FavoriteSitesStore.MaximumTextLength,Path.GetFileNameWithoutExtension(picker.FileName).Length)];
    }
    private void UpdatePreview()=>iconPreview.Child=SiteOrb.CreateFace(icon,sphereText.Text,32);
    private void ChooseIcon()
    {
        var picker=new OpenFileDialog{Title="Значок сферы",Filter="Изображения|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif",CheckFileExists=true};
        if(picker.ShowDialog(this)!=true)return;
        try{ApplyIconFile(picker.FileName);}
        catch(Exception e) when(FavoriteSitesStore.IsDataError(e)){Error("Не удалось открыть изображение. Выберите PNG, JPG или ICO.");}
    }
    internal void ApplyIconFile(string file)
    {
        // Import first: a failed selection must preserve the current editor state.
        string selected=FavoriteSitesStore.ImportIcon(file);
        icon=selected;libraryIcon=null;sphereText.Text="";UpdatePreview();status.Visibility=Visibility.Collapsed;
    }
    internal void ApplyLibraryIcon(string id)
    {
        var selected=appearance.Icons.Single(i=>i.Id==id);
        icon=selected.Image;libraryIcon=selected.Id;sphereText.Text="";UpdatePreview();status.Visibility=Visibility.Collapsed;
    }
    internal void OpenIconLibrary()
    {
        var picker=new GlassWindow((PetWindow)Owner,"Значки · "+appearance.Name,340);SettingsAppearance.Apply(picker);
        var grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=5};
        foreach(var item in appearance.Icons)
        {
            var tile=new StackPanel();tile.Children.Add(SiteOrb.CreateFace(item.Image,null,44));
            var label=Ui.Text(item.Name,9);label.TextAlignment=TextAlignment.Center;label.Margin=new Thickness(0,5,0,0);tile.Children.Add(label);
            var button=Ui.Button(item.Name,()=>{ApplyLibraryIcon(item.Id);picker.Close();Activate();},quiet:true);
            button.Content=tile;button.Padding=new Thickness(4,7,4,7);grid.Children.Add(button);
        }
        picker.Body.Children.Add(grid);
        Closed+=ClosePicker;picker.Closed+=(_,_)=>Closed-=ClosePicker;
        void ClosePicker(object? sender,EventArgs e){if(!picker.IsClosing)picker.Close();}
        picker.Show();
    }
    private void Save()
    {
        if(!SaveSize())return;
        try
        {
            bool program=targetKind.SelectedIndex==1;
            string url=program?FavoriteSitesStore.NormalizeProgramPath(address.Text):FavoriteSitesStore.NormalizeUrl(address.Text);
            if(program&&!File.Exists(url))throw new InvalidDataException("Программа не найдена. Проверьте путь.");
            if(icon.Length==0&&string.IsNullOrWhiteSpace(sphereText.Text))throw new InvalidDataException("Введите текст на сфере или выберите значок.");
            if(editingId is null&&store.Read().Length>=FavoriteSitesStore.MaximumSites)throw new InvalidDataException("В избранном уже 100 элементов.");
            store.Put(new FavoriteSite(editingId??Guid.NewGuid().ToString("N"),program?Path.GetFileNameWithoutExtension(url):new Uri(url).Host,url,icon,sphereText.Text,program?"program":"site",libraryIcon));changed();Edit(null);Refresh();
        }
        catch(Exception e) when(FavoriteSitesStore.IsDataError(e)){Error(e.Message);}
    }
    private void Remove(FavoriteSite site)
    {
        if(!SaveSize())return;
        try{store.Delete(site.Id);if(editingId==site.Id)Edit(null);changed();Refresh();}
        catch(Exception e) when(FavoriteSitesStore.IsDataError(e)){Error(e.Message);}
    }
}
