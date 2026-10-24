namespace LocalCaptureAgent.Services;

// Er mag maar een recorder per Windows-sessie draaien. Zonder deze vergrendeling kan er een tweede exemplaar bij komen,
// bijvoorbeeld als Windows de recorder na het aanmelden opnieuw start (RegisterApplicationRestart) terwijl hij al draait.
// Twee recorders stellen allebei de opnamevraag, draaien allebei een worker en kunnen een melding dubbel tonen.
internal sealed class SingleInstanceGuard : IDisposable
{
    internal const string DefaultMutexName = @"Local\" + Branding.InstanceName + ".SingleInstance";
    internal const string DefaultShowName = @"Local\" + Branding.InstanceName + ".ShowWindow";
    internal const string DefaultAckName = @"Local\" + Branding.InstanceName + ".ShowWindowAck";

    // Hoe lang een tweede start op de bevestiging wacht. Het draaiende exemplaar bevestigt alleen binnen een kortere
    // termijn: een te late bevestiging zou in de semafoor blijven staan en een volgende start ten onrechte geruststellen.
    internal static readonly TimeSpan ShowRequestTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan ShowAcknowledgeWindow = TimeSpan.FromSeconds(8);

    internal enum ShowRequestResult
    {
        NotRunning,
        Acknowledged,
        NoResponse
    }

    private readonly Mutex _mutex;
    // Semaforen, geen events: een event voegt gelijktijdige verzoeken (een dubbelklik) samen tot een signaal,
    // zodat maar een van de wachtende starts een bevestiging kreeg. Een semafoor telt elk verzoek apart.
    private readonly Semaphore _showRequests;
    private readonly Semaphore _acknowledgements;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, Semaphore showRequests, Semaphore acknowledgements)
    {
        _mutex = mutex;
        _showRequests = showRequests;
        _acknowledgements = acknowledgements;
    }

    // Geeft de guard terug als dit het eerste exemplaar is; anders null. Het eerste exemplaar houdt de mutex
    // vast tot het proces stopt; Windows geeft hem ook vrij als het proces crasht.
    internal static SingleInstanceGuard? TryAcquire(string mutexName = DefaultMutexName, string showName = DefaultShowName, string ackName = DefaultAckName)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, mutexName);
        }
        catch (UnauthorizedAccessException)
        {
            // De mutex bestaat al maar is niet toegankelijk: er draait een exemplaar met andere rechten (verhoogd).
            return null;
        }

        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // Het vorige exemplaar is gecrasht zonder vrij te geven; de mutex is nu van ons.
            owned = true;
        }

        if (!owned)
        {
            mutex.Dispose();
            return null;
        }

        Semaphore? showRequests = null;
        try
        {
            showRequests = new Semaphore(0, int.MaxValue, showName);
            return new SingleInstanceGuard(mutex, showRequests, new Semaphore(0, int.MaxValue, ackName));
        }
        catch
        {
            // Zonder deze objecten geen bruikbare guard: de mutex niet vasthouden, anders blokkeert dit exemplaar
            // ongemerkt elke volgende start terwijl het log "zonder guard" meldt.
            showRequests?.Dispose();
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    // Vraagt het draaiende exemplaar zijn venster te tonen en wacht op de bevestiging, die dat exemplaar pas
    // geeft nadat zijn UI-thread het venster heeft getoond. Zo blijkt ook of het exemplaar nog reageert.
    internal static ShowRequestResult RequestShowFromExistingInstance(TimeSpan timeout, string showName = DefaultShowName, string ackName = DefaultAckName)
    {
        try
        {
            if (!Semaphore.TryOpenExisting(showName, out var showRequests)) return ShowRequestResult.NotRunning;
            using (showRequests)
            {
                if (!Semaphore.TryOpenExisting(ackName, out var acknowledgements)) return ShowRequestResult.NoResponse;
                using (acknowledgements)
                {
                    showRequests.Release();
                    return acknowledgements.WaitOne(timeout) ? ShowRequestResult.Acknowledged : ShowRequestResult.NoResponse;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException or SemaphoreFullException)
        {
            // Het exemplaar draait (de mutex bewijst het) maar is onbereikbaar, bijvoorbeeld verhoogd gestart.
            return ShowRequestResult.NoResponse;
        }
    }

    // Roept onShowRequested aan (op een threadpoolthread), een keer per verzoek van een tweede start.
    internal void ListenForShowRequests(Action onShowRequested)
    {
        if (_disposed || _registration is not null) return;
        _registration = ThreadPool.RegisterWaitForSingleObject(_showRequests, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    // Een bevestiging per afgehandeld verzoek; aanroepen nadat het venster getoond is.
    internal void AcknowledgeShowRequest()
    {
        if (_disposed) return;
        try
        {
            _acknowledgements.Release();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SemaphoreFullException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registration?.Unregister(null);
        // Eerst de mutex vrijgeven: een start die tijdens het afsluiten binnenkomt, krijgt hem dan meteen.
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Niet in bezit van deze thread; het proces stopt toch.
        }

        _mutex.Dispose();
        _showRequests.Dispose();
        _acknowledgements.Dispose();
    }
}
