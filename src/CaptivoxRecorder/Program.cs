using LocalCaptureAgent.Models;
using LocalCaptureAgent.Services;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace LocalCaptureAgent;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            MessageBox.Show("This recorder only supports Windows.", Branding.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error); // geen vertaling
            return;
        }

        var launchMinimized = args.Any(x => string.Equals(x, "--minimized", StringComparison.OrdinalIgnoreCase));
        args = WaitForPreviousInstance(args);
        if (TryHandleCommandLine(args.Where(x => !string.Equals(x, "--minimized", StringComparison.OrdinalIgnoreCase)).ToArray()))
        {
            return;
        }

        // Maar een recorder tegelijk. Een tweede exemplaar stopt hier, voordat het handlers registreert,
        // zich voor herstart aanmeldt of iets opstart. Zonder --minimized (handmatig gestart) vraagt het het draaiende
        // exemplaar om zijn venster te tonen; met --minimized (automatisch gestart, herstart door Windows) stopt het stil.
        SingleInstanceGuard? instanceGuard = null;
        try
        {
            instanceGuard = SingleInstanceGuard.TryAcquire() ?? HandleSecondInstance(launchMinimized);
            if (instanceGuard is null)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // Liever een dubbele recorder dan een recorder die niet start.
            AppRuntime.LogException("Single-instance-guard niet beschikbaar; recorder start zonder", ex);
        }

        using var ownedInstanceGuard = instanceGuard;

        // Pas na de guard en alleen voor de app zelf: hulpopdrachten via de commandoregel kwamen na een herstart
        // terug als venster zonder argumenten. Herstart door Windows gebeurt verborgen in het systeemvak.
        TryRegisterForRestart();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) =>
        {
            AppRuntime.LogException("UI thread exception", args.Exception);
            MessageBox.Show(UiText.F("Er is een onverwachte fout opgetreden. Zie logbestand in:{0}", Environment.NewLine + AppRuntime.GetLogDirectory()), UiText.T("Onverwachte fout"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            AppRuntime.LogException("AppDomain unhandled exception", exception ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown unhandled exception"));
            AppRuntime.TryMarkActiveRecordingInterrupted("Onverwerkte AppDomain exceptie", exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppRuntime.LogException("Unobserved task exception", args.Exception);
            args.SetObserved();
        };


        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            AppRuntime.LogMessage("ProcessExit ontvangen. Controle op actieve opname gestart.");
            AppRuntime.TryMarkActiveRecordingInterrupted("Proces beëindigd tijdens actieve opname"); // geen vertaling
        };

        Application.ApplicationExit += (_, _) =>
        {
            AppRuntime.LogMessage("ApplicationExit ontvangen. Controle op actieve opname gestart.");
            AppRuntime.TryMarkActiveRecordingInterrupted("Applicatie afgesloten tijdens actieve opname"); // geen vertaling
        };

        ApplicationConfiguration.Initialize();
        // De taal van de interface, een keer bij het starten (een andere keuze geldt vanaf de volgende start).
        try
        {
            UiText.SetLanguage(SettingsStore.Load().Ui.InterfaceLanguage);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Taal van de interface lezen mislukt; Engels gebruikt", ex);
            UiText.SetLanguage(UiText.DefaultLanguage);
        }

        Thread.CurrentThread.CurrentUICulture = UiText.Culture;
        Application.Idle += (_, _) => UiTextBinder.AttachOpenForms();
        WindowsToastNotificationService.Initialize();

        try
        {
            using var mainForm = new MainForm(launchMinimized);
            if (ownedInstanceGuard is { } guard)
            {
                guard.ListenForShowRequests(() => mainForm.ShowFromOtherInstance(guard.AcknowledgeShowRequest));
            }

            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("MainForm startup failed", ex);
            MessageBox.Show(UiText.F("{0} kon niet starten. Zie logbestand in:{1}", Branding.DisplayName, Environment.NewLine + AppRuntime.GetLogDirectory()) + Environment.NewLine + Environment.NewLine + ex.Message, Branding.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Opnieuw starten vanuit Captivox zelf (bijvoorbeeld na een andere taal): "--wait-for-pid <id>" wacht tot het vorige
    // exemplaar is afgesloten (hooguit 30 seconden), zodat dit exemplaar daarna gewoon de enige recorder wordt.
    public const string WaitForPidArgument = "--wait-for-pid";

    private static string[] WaitForPreviousInstance(string[] args)
    {
        var index = Array.FindIndex(args, x => string.Equals(x, WaitForPidArgument, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return args;
        if (index + 1 < args.Length && int.TryParse(args[index + 1], out var pid) && pid > 0 && pid != Environment.ProcessId)
        {
            try
            {
                using var previous = System.Diagnostics.Process.GetProcessById(pid);
                previous.WaitForExit(30000);
            }
            catch (ArgumentException)
            {
                // Al afgesloten.
            }
            catch (InvalidOperationException)
            {
            }
        }

        return args.Where((_, i) => i != index && i != index + 1).ToArray();
    }

    // Geeft alleen een guard terug als het eerste exemplaar intussen gestopt bleek; dan start dit exemplaar gewoon.
    private static SingleInstanceGuard? HandleSecondInstance(bool launchMinimized)
    {
        if (launchMinimized)
        {
            AppRuntime.LogMessage("Tweede recorderstart (--minimized) genegeerd: er draait al een exemplaar.");
            return null;
        }

        // Het draaiende exemplaar mag zijn venster naar de voorgrond halen; anders knippert het alleen in de taakbalk.
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        var result = SingleInstanceGuard.RequestShowFromExistingInstance(SingleInstanceGuard.ShowRequestTimeout);
        if (result == SingleInstanceGuard.ShowRequestResult.Acknowledged)
        {
            AppRuntime.LogMessage("Tweede recorderstart: bestaand venster naar voren gehaald; dit exemplaar stopt.");
            return null;
        }

        // Ook bij geen antwoord: het eerste exemplaar kan tijdens het wachten zijn afgesloten.
        if (SingleInstanceGuard.TryAcquire() is { } guard)
        {
            AppRuntime.LogMessage($"Tweede recorderstart: het eerste exemplaar was intussen gestopt ({result}); dit exemplaar start gewoon.");
            return guard;
        }

        // Geen advies om het proces meteen te stoppen: een recorder die even bezig is, kan midden in een opname zitten.
        AppRuntime.LogMessage($"Tweede recorderstart: het draaiende exemplaar reageerde niet binnen 10 s ({result}); dit exemplaar stopt.");
        ApplicationConfiguration.Initialize();
        try
        {
            UiText.SetLanguage(SettingsStore.Load().Ui.InterfaceLanguage);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Taal van de interface lezen mislukt", ex);
        }

        MessageBox.Show(
            UiText.F("{0} draait al, maar reageerde niet binnen 10 seconden. Mogelijk is hij nog aan het opstarten of druk bezig. Probeer het over een minuut opnieuw.", Branding.DisplayName)
                + Environment.NewLine + Environment.NewLine
                + UiText.T("Blijft dit gebeuren, controleer dan eerst of er een opname loopt voordat je het bestaande proces via Taakbeheer stopt."),
            Branding.DisplayName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return null;
    }

    private static void TryRegisterForRestart()
    {
        try
        {
            _ = NativeMethods.RegisterApplicationRestart("--minimized", 0);
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("RegisterApplicationRestart failed", ex);
        }
    }

    private static bool TryHandleCommandLine(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        var command = args[0];
        if (string.Equals(command, "--recover-interrupted", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var settings = SettingsStore.Load();
                var staleSeconds = 120;
                for (var i = 1; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--stale-seconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed) && parsed > 0)
                    {
                        staleSeconds = parsed;
                        i++;
                    }
                }

                var recovered = SessionRecoveryService.RecoverInterruptedSessions(settings, TimeSpan.FromSeconds(staleSeconds));
                Console.Out.WriteLine(recovered.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("CLI interrupted recovery failed", ex);
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(5);
            }
        }

        if (string.Equals(command, "--import-audio", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    Console.Error.WriteLine("Bronbestand voor audio-import ontbreekt.");
                    Environment.Exit(1);
                }

                var sourcePath = args[1];
                var fromWatchFolder = args.Any(x => string.Equals(x, "--watch-folder-source", StringComparison.OrdinalIgnoreCase));
                var consumeSource = args.Any(x => string.Equals(x, "--consume-source", StringComparison.OrdinalIgnoreCase));
                var settings = SettingsStore.Load();
                var manifest = ImportSessionService.ImportAudio(sourcePath, settings, fromWatchFolder, consumeSource);
                Console.Out.WriteLine(manifest.OutputDirectory);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("CLI import audio failed", ex);
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(4);
            }
        }

        return false;
    }
}
