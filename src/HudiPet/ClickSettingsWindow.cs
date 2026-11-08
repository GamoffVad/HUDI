using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace HudiPet;

internal sealed class ClickSettingsWindow:GlassWindow
{
    internal ClickSettingsWindow(PetWindow pet):base(pet,"Управление кликами",380,"\uE713")
    {
        SettingsAppearance.Apply(this);((StackPanel)Surface.Child).Margin=new Thickness(14);Heading.FontSize=15;Header.Margin=new Thickness(0,0,0,8);
        var status=Ui.Text(pet.ClickBindingsError??"",12,Ui.Error);status.Margin=new Thickness(0,8,0,0);status.Visibility=pet.ClickBindingsError is null?Visibility.Collapsed:Visibility.Visible;
        foreach(bool control in new[]{false,true})
        {
            if(control)Body.Children.Add(Ui.Separator());
            foreach(int count in new[]{1,2,3})
            {
                string label=(control?"Ctrl + ":"")+(count switch{1=>"Один клик",2=>"Двойной клик",_=>"Тройной клик"});
                var row=new Grid{Margin=new Thickness(0,5,0,5)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(164)});
                var caption=Ui.Text(label,12);caption.VerticalAlignment=VerticalAlignment.Center;caption.Margin=new Thickness(0,0,8,0);row.Children.Add(caption);
                var select=Ui.Select(["Ничего","Сайты и программы","Заметки","Чат с моделью"],(int)pet.ClickBindings.Get(count,control));select.Style=(Style)FindResource("SettingsSelect");select.IsEnabled=pet.ClickBindingsError is null;AutomationProperties.SetName(select,label);Grid.SetColumn(select,1);row.Children.Add(select);Body.Children.Add(row);
                bool syncing=false;
                select.SelectionChanged+=(_,_)=>
                {
                    if(syncing||select.SelectedIndex<0||select.SelectedIndex==(int)pet.ClickBindings.Get(count,control))return;
                    try{pet.SetClickBindings(pet.ClickBindings.With(count,control,(PetClickAction)select.SelectedIndex));status.Visibility=Visibility.Collapsed;}
                    catch(Exception error) when(ClickBindingsStore.IsDataError(error))
                    {
                        syncing=true;select.SelectedIndex=(int)pet.ClickBindings.Get(count,control);syncing=false;status.Text="Не удалось сохранить назначение. Попробуйте ещё раз.";status.Visibility=Visibility.Visible;
                    }
                };
            }
        }
        Body.Children.Add(status);
    }
}

internal sealed partial class PetWindow
{
    private ClickBindingsStore clickBindingsStore=null!;
    private ClickSettingsWindow? clickSettingsWindow;
    internal ClickBindings ClickBindings {get;private set;}=new();
    internal string? ClickBindingsError {get;private set;}
    private void InitializeClickBindings(ClickBindingsStore? store)
    {
        clickBindingsStore=store??new ClickBindingsStore(uiTest||smokeTest?System.IO.Path.Combine(SettingsDirectory,"ui-test"):null);
        try{ClickBindings=clickBindingsStore.Read();}catch(Exception error) when(ClickBindingsStore.IsDataError(error)){ClickBindingsError=error.Message;}
    }
    internal void SetClickBindings(ClickBindings bindings)
    {
        clickBindingsStore.Save(bindings);CancelPetClicks();ClickBindings=bindings;ClickBindingsError=null;
    }
    internal void OpenClickSettings()
    {
        CancelPetClicks();CloseFavoriteSites();menuWindow?.Close();menuWindow=null;
        if(clickSettingsWindow is {IsClosing:false}){clickSettingsWindow.Activate();return;}
        clickSettingsWindow=new ClickSettingsWindow(this);clickSettingsWindow.Show();
    }
}
