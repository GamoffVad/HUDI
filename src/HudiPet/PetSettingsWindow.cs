using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace HudiPet;

internal sealed class PetSettingsWindow:GlassWindow
{
    private readonly ComboBox choices;
    private readonly Image preview;
    private readonly TextBlock details,status;
    private readonly Button add,apply;
    private readonly PetWindow owner;
    private bool busy,selectionValid;
    internal PetSettingsWindow(PetWindow owner):base(owner,"Питомцы",400,"\uE77B")
    {
        this.owner=owner;SettingsAppearance.Apply(this);
        var intro=Ui.Text("Ваши персонажи — в одной коллекции. Выберите питомца или добавьте нового.",13,Ui.Muted);intro.Margin=new Thickness(0,0,0,16);Body.Children.Add(intro);
        choices=new ComboBox {Style=(Style)FindResource("SettingsSelect")};System.Windows.Automation.AutomationProperties.SetName(choices,"Выбор питомца");Body.Children.Add(choices);
        preview=new Image {Height=182,Margin=new Thickness(0,18,0,12),Stretch=Stretch.Uniform};Body.Children.Add(preview);
        details=Ui.Text("",12,Ui.Muted);details.HorizontalAlignment=HorizontalAlignment.Center;Body.Children.Add(details);
        status=Ui.Text("",12,Ui.Error);status.Margin=new Thickness(0,12,0,0);status.Visibility=Visibility.Collapsed;Body.Children.Add(status);
        add=Ui.Button("+ Добавить питомца…",async()=>await Import());add.Style=(Style)FindResource("SettingsAction");add.Margin=new Thickness(0,20,0,10);Body.Children.Add(add);
        apply=Ui.Button("Выбрать питомца",async()=>await Choose(),true);apply.Style=(Style)FindResource("SettingsSave");Body.Children.Add(apply);
        var note=Ui.Text("Добавление из пакета .pet или .zip с готовыми анимациями.",11,Ui.Muted);note.Margin=new Thickness(0,12,0,0);Body.Children.Add(note);
        choices.SelectionChanged+=(_,_)=>Preview();Refresh(owner.SelectedPetId);
    }
    private void Refresh(string id){choices.ItemsSource=owner.Pets.List();choices.SelectedItem=((LocalPet[])choices.ItemsSource).FirstOrDefault(p=>p.Id==id)??PetCatalog.Hudi;}
    private void Message(string text){status.Text=text;status.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;}
    private void Preview()
    {
        if(choices.SelectedItem is not LocalPet pet)return;
        try
        {
            var library=owner.Pets.Open(pet);
            try{preview.Source=library.Get(library.NeutralFrame);int count=PetBehavior.AutomaticActions.Count(a=>library.Clips.ContainsKey(PetBehavior.ClipName(a)));details.Text=pet.Name+(count>0?$" · движений: {count}":" · спокойная поза");}
            finally{if(!pet.BuiltIn)library.Dispose();}
            selectionValid=true;apply.IsEnabled=!busy;Message("");
        }
        catch(Exception ex) when(IsPackError(ex)){selectionValid=false;preview.Source=null;apply.IsEnabled=false;Message("Не удалось открыть пакет. Добавьте исправленную версию питомца.");}
    }
    private void Busy(bool value){busy=value;choices.IsEnabled=add.IsEnabled=!value;apply.IsEnabled=!value&&selectionValid;}
    private static bool IsPackError(Exception ex)=>ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or NotSupportedException or FormatException;
    private async Task Import()
    {
        var file=new OpenFileDialog {Title="Добавить питомца",Filter="Пакет питомца (*.pet;*.zip)|*.pet;*.zip"};if(file.ShowDialog(this)!=true)return;
        Busy(true);Message("Проверяю кадры и добавляю питомца…");
        try{var pet=await owner.Pets.ImportAsync(file.FileName);if(!IsClosing){Refresh(pet.Id);Message("Питомец добавлен. Нажмите «Выбрать питомца», чтобы показать его на рабочем столе.");}}
        catch(Exception ex) when(IsPackError(ex)){if(!IsClosing)Message(ex is InvalidDataException?ex.Message:"Не удалось добавить пакет. Нужен архив с manifest.json и PNG-кадрами.");}
        finally{if(!IsClosing)Busy(false);}
    }
    private async Task Choose()
    {
        if(choices.SelectedItem is not LocalPet pet)return;
        Busy(true);Message("Меняю питомца…");
        try{await owner.SelectPetAsync(pet);if(!IsClosing)Close();}
        catch(Exception ex) when(IsPackError(ex)||ex is ModelException){if(!IsClosing)Message(ex is ModelException?ex.Message:"Не удалось выбрать питомца. Текущий персонаж сохранён.");}
        finally{if(!IsClosing)Busy(false);}
    }
}
