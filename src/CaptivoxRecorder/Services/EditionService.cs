using LocalCaptureAgent.Models;

namespace LocalCaptureAgent.Services;

internal sealed class EditionService
{
    public EditionStateSnapshot GetEditionState(bool attemptRevalidation = true)
    {
        var deviceId = LicenseCacheStore.GetOrCreateDeviceId();
        return BuildPublicSnapshot(deviceId);
    }

    public bool TryActivate(ApplicationSettings settings, string email, string licenseCode, out EditionStateSnapshot snapshot, out string error)
    {
        snapshot = GetEditionState(attemptRevalidation: false);
        error = "Captivox Recorder gebruikt een publieke productlijn. Er is geen activatie nodig om de huidige functies te gebruiken.";
        ApplyEditionPolicy(settings, snapshot);
        return false;
    }

    public EditionStateSnapshot ClearActivation(ApplicationSettings settings)
    {
        LicenseCacheStore.Clear();
        var snapshot = GetEditionState(attemptRevalidation: false);
        ApplyEditionPolicy(settings, snapshot);
        return snapshot;
    }

    public EditionStateSnapshot ForceRevalidate(ApplicationSettings settings, out string message)
    {
        LicenseCacheStore.Clear();
        var snapshot = GetEditionState(attemptRevalidation: false);
        ApplyEditionPolicy(settings, snapshot);
        message = "Publieke productlijn gecontroleerd. De huidige functies blijven beschikbaar op basis van lokale instellingen en prerequisites.";
        return snapshot;
    }

    public bool ApplyEditionPolicy(ApplicationSettings settings, EditionStateSnapshot snapshot)
    {
        settings.Worker.Import ??= new ImportSettings();
        if (!settings.Worker.Import.RequestedWatchFolderEnabled && settings.Worker.Import.WatchFolderEnabled)
        {
            settings.Worker.Import.RequestedWatchFolderEnabled = true;
        }

        var effectiveWatchFolderEnabled = settings.Worker.Import.RequestedWatchFolderEnabled
            && snapshot.IsFeatureEnabled(EditionFeatureCatalog.WatchFolders);

        var changed = settings.Worker.Import.WatchFolderEnabled != effectiveWatchFolderEnabled;
        settings.Worker.Import.WatchFolderEnabled = effectiveWatchFolderEnabled;
        return changed;
    }

    private static EditionStateSnapshot BuildPublicSnapshot(string deviceId)
    {
        return new EditionStateSnapshot
        {
            State = "community",
            DisplayName = "Publiek",
            Summary = "Publieke productlijn actief.",
            Detail = "Captivox Recorder is een publieke technical preview voor lokaal opnemen, transcriberen en sessies terugvinden. De basisfuncties blijven beschikbaar zolang de lokale prerequisites goed staan. Donaties kunnen de verdere ontwikkeling ondersteunen, maar zijn geen voorwaarde voor gebruik.",
            DeviceId = deviceId,
            PremiumActive = false,
            EnabledFeatures = EditionFeatureCatalog.PremiumFeatures.ToArray(),
        };
    }
}
