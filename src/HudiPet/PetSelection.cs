using System.IO;
using System.Windows;

namespace HudiPet;

internal sealed partial class PetWindow
{
    internal PetCatalog Pets {get;private set;}=null!;
    private LocalPet selectedPet=PetCatalog.Hudi;
    private SpriteLibrary library=null!;
    private PetSettingsWindow? petSettings;
    internal string PetName=>selectedPet.Name;
    internal string SelectedPetId=>selectedPet.Id;
    private void InitializePet(PetCatalog? pets=null)
    {
        Pets=pets??new(uiTest||smokeTest?Path.Combine(SettingsDirectory,"ui-test","pets"):null);
        selectedPet=Pets.Selected();
        try{library=Pets.Open(selectedPet);library.Get(library.NeutralFrame);}
        catch{if(library is not null&&!selectedPet.BuiltIn)library.Dispose();selectedPet=PetCatalog.Hudi;library=SpriteLibrary.Shared.Value;}
        character=new(library);behavior=new(sprites:library);
    }
    internal void OpenPetSettings()
    {
        CloseFavoriteSites();
        menuWindow?.Close();menuWindow=null;
        if(petSettings is {IsClosing:false}){petSettings.Activate();return;}
        bool restore=settingsWindow is {IsVisible:true};if(restore)settingsWindow!.Hide();
        petSettings=new(this);petSettings.Closed+=(_,_)=>{if(restore&&settingsWindow is {IsClosing:false}&&!shuttingDown){settingsWindow.Show();settingsWindow.Activate();}};
        petSettings.Show();
    }
    internal async Task SelectPetAsync(LocalPet pet)
    {
        if(chat.Busy)throw new ModelException("Дождитесь ответа модели или отмените запрос перед сменой питомца.");
        if(pet.Id==selectedPet.Id)return;
        var next=await Task.Run(()=>{var pack=Pets.Open(pet);try{pack.Get(pack.NeutralFrame);return pack;}catch{if(!pet.BuiltIn)pack.Dispose();throw;}});
        if(shuttingDown){if(!pet.BuiltIn)next.Dispose();return;}
        try
        {
            if(chat.Busy)throw new ModelException("Дождитесь завершения ответа модели.");
            var visual=new PetVisual(next);var controller=new PetBehavior(sprites:next);
            Pets.Select(pet);
            var previous=library;bool dispose=!selectedPet.BuiltIn;
            var displayed=DisplayedBounds;
            CancelPetClicks();character.ReleaseMouseCapture();dragOrigin=null;dragging=false;
            CloseFavoriteSites();
            library=next;selectedPet=pet;character=visual;behavior=controller;Content=character;
            SetWindowBounds(WindowForDisplay(displayed,new(library.Width,library.Height),library.DisplayBounds));
            Title=PetName+" — живой питомец";chatWindow?.Close();chatWindow=null;chatDraft="";chat.ClearHistory();paused=false;previousTime=clock.Elapsed.TotalSeconds;
            ClampToScreen();SaveSettings();if(dispose)previous.Dispose();
        }
        catch{if(!pet.BuiltIn&&!ReferenceEquals(next,library))next.Dispose();throw;}
    }
}
