namespace HudiPet;

internal static partial class PetIconLibrary
{
    // Vector masters are rendered at 128 px with transparent padding. They stay
    // sharp at small sizes and travel in the pet package as self-contained PNGs.
    private static readonly (string Id,string Name,string Path)[] symbols=[
        ("browser","Браузер","M 64,28 A 36,36 0 1 1 63.9,28 M 28,64 L 100,64 M 64,28 C 40,44 40,84 64,100 M 64,28 C 88,44 88,84 64,100"),
        ("messages","Сообщения","M 27,32 L 101,32 L 101,83 L 59,83 L 38,101 L 38,83 L 27,83 Z M 43,51 L 85,51 M 43,66 L 73,66"),
        ("mail","Почта","M 25,38 L 103,38 L 103,91 L 25,91 Z M 25,38 L 64,69 L 103,38"),
        ("folder","Папка","M 23,43 L 23,33 L 53,33 L 63,43 L 103,43 L 103,94 L 23,94 Z M 23,53 L 103,53"),
        ("music","Музыка","M 52,83 L 52,35 L 94,28 L 94,75 M 52,47 L 94,40 M 52,83 A 13,9 0 1 1 51.9,83 M 94,75 A 13,9 0 1 1 93.9,75"),
        ("video","Видео","M 29,32 L 99,32 L 99,96 L 29,96 Z M 55,48 L 79,64 L 55,80 Z"),
        ("games","Игры","M 41,44 C 20,44 15,94 32,94 L 50,79 L 78,79 L 96,94 C 113,94 108,44 87,44 Z M 40,54 L 40,74 M 30,64 L 50,64 M 82,57 L 82,59 M 92,69 L 92,71"),
        ("code","Код","M 44,42 L 22,64 L 44,86 M 84,42 L 106,64 L 84,86 M 73,31 L 55,97"),
        ("calendar","Календарь","M 28,36 L 100,36 L 100,100 L 28,100 Z M 28,52 L 100,52 M 45,26 L 45,43 M 83,26 L 83,43 M 45,69 L 51,69 M 72,69 L 78,69 M 45,84 L 51,84 M 72,84 L 78,84"),
        ("favorite","Избранное","M 64,25 L 75,49 L 102,53 L 82,73 L 87,101 L 64,87 L 41,101 L 46,73 L 26,53 L 53,49 Z")];
}
