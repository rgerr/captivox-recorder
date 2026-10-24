namespace LocalCaptureAgent;

// Alles over het aanbod van de betaalde versie op een plek: of hij al te krijgen is, waar de knop heen gaat en wat de
// knop en de stand zeggen. Komt Pro uit, dan wordt Available true en wijst Url naar de koop- of downloadpagina; de pagina
// in de app (MainForm.ProOffer) volgt vanzelf.
internal static class ProOffer
{
    public const bool Available = false;
    public const string Url = "https://github.com/rgerr/captivox-recorder#captivox-pro";

    public static string StatusText => Available ? "Captivox Pro is te krijgen." : "Captivox Pro komt binnenkort.";
    public static string ButtonText => Available ? "Captivox Pro bekijken" : "Houd me op de hoogte";
}
