using System.IO;

namespace HudiPet;

internal sealed partial class PetWindow
{
    private FavoriteSitesStore favoriteSitesStore=null!;
    private FavoriteSitesWindow? favoriteSitesSettings;
    private FavoriteSitesOrbit? favoriteSitesOrbit;
    internal FavoriteSitesOrbit? SitesOrbit=>favoriteSitesOrbit;
    internal bool IgnoreGlobalSitesInput=>uiTest||smokeTest;
    private void InitializeFavoriteSites(FavoriteSitesStore? store)
    {
        favoriteSitesStore=store??new FavoriteSitesStore(uiTest||smokeTest?Path.Combine(SettingsDirectory,"ui-test"):null);
        Closing+=(_,e)=>{if(favoriteSitesSettings is {IsClosing:false}&&!favoriteSitesSettings.FlushSize())e.Cancel=true;};
        Closed+=(_,_)=>CloseFavoriteSites();
    }
    internal void CloseFavoriteSites()
    {
        var window=favoriteSitesOrbit;favoriteSitesOrbit=null;window?.Close();
    }
    internal void ToggleFavoriteSites()
    {
        if(favoriteSitesOrbit is {IsDismissing:false}){favoriteSitesOrbit.Dismiss();return;}
        CloseFavoriteSites();menuWindow?.Close();menuWindow=null;
        try
        {
            var sites=favoriteSitesStore.Read();
            if(sites.Length==0){OpenFavoriteSitesSettings();return;}
            var window=new FavoriteSitesOrbit(this,sites,favoriteSitesStore.MaximumSphereSize);favoriteSitesOrbit=window;
            window.Closed+=(_,_)=>{if(ReferenceEquals(favoriteSitesOrbit,window))favoriteSitesOrbit=null;};window.Show();
        }
        catch(Exception error) when(FavoriteSitesStore.IsDataError(error)){OpenFavoriteSitesSettings();}
    }
    internal void OpenFavoriteSitesSettings()
    {
        CancelPetClicks();CloseFavoriteSites();menuWindow?.Close();menuWindow=null;
        if(favoriteSitesSettings is {IsClosing:false}){favoriteSitesSettings.Activate();return;}
        favoriteSitesSettings=new FavoriteSitesWindow(this,favoriteSitesStore,CloseFavoriteSites);favoriteSitesSettings.Show();
    }
}
