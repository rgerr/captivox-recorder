using System.Text;
using Captivox.Recorder.Services;

namespace Captivox.Recorder;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launchMinimized = args.Any(x => string.Equals(x, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (TryHandleCommandLine(args.Where(x => !string.Equals(x, "--minimized", StringComparison.OrdinalIgnoreCase)).ToArray()))
        {
            return;
        }

        // Maar een recorder tegelijk. Een tweede start vraagt het draaiende exemplaar om zijn venster te tonen.
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
            AppRuntime.LogException("Controle op een tweede exemplaar niet beschikbaar; de recorder start zonder", ex);
        }

        using var ownedInstanceGuard = instanceGuard;

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            AppRuntime.LogException("Fout in het scherm", e.Exception);
            MessageBox.Show($"Er ging iets onverwachts mis. Het logbestand staat in:{Environment.NewLine}{AppRuntime.GetLogDirectory()}", Branding.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            AppRuntime.LogException("Onverwerkte fout", exception);
            AppRuntime.TryMarkActiveRecordingInterrupted("Onverwerkte fout", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppRuntime.LogException("Onverwerkte taakfout", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => AppRuntime.TryMarkActiveRecordingInterrupted("Proces beeindigd tijdens een opname");
        Application.ApplicationExit += (_, _) => AppRuntime.TryMarkActiveRecordingInterrupted("Programma afgesloten tijdens een opname");

        ApplicationConfiguration.Initialize();
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
            AppRuntime.LogException("Starten mislukt", ex);
            MessageBox.Show($"Captivox Recorder kon niet starten. Het logbestand staat in:{Environment.NewLine}{AppRuntime.GetLogDirectory()}{Environment.NewLine}{Environment.NewLine}{ex.Message}", Branding.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static SingleInstanceGuard? HandleSecondInstance(bool launchMinimized)
    {
        if (launchMinimized)
        {
            return null;
        }

        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        var result = SingleInstanceGuard.RequestShowFromExistingInstance(SingleInstanceGuard.ShowRequestTimeout);
        if (result == SingleInstanceGuard.ShowRequestResult.Acknowledged)
        {
            return null;
        }

        // Het eerste exemplaar kan intussen gestopt zijn.
        if (SingleInstanceGuard.TryAcquire() is { } guard)
        {
            return guard;
        }

        ApplicationConfiguration.Initialize();
        MessageBox.Show("Captivox Recorder draait al, maar reageerde niet binnen 10 seconden. Probeer het over een minuut opnieuw. Stop het bestaande proces pas als je zeker weet dat er geen opname loopt.", Branding.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return null;
    }

    // Hulpopdrachten voor de transcriptieworker; die roept de recorder aan voor het token, imports en herstel.
    private static bool TryHandleCommandLine(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        var command = args[0];
        if (string.Equals(command, "--get-hf-token", StringComparison.OrdinalIgnoreCase))
        {
            if (SettingsStore.TryLoadToken(out var token, out var error) && !string.IsNullOrWhiteSpace(token))
            {
                Console.Out.Write(token);
                Console.Out.Flush();
                Environment.Exit(0);
            }

            Console.Error.WriteLine(error ?? "Geen opgeslagen Hugging Face-token gevonden.");
            Environment.Exit(2);
        }

        if (string.Equals(command, "--export-hf-token", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                {
                    Console.Error.WriteLine("Pad voor het token ontbreekt.");
                    Environment.Exit(1);
                }

                if (!SettingsStore.TryLoadToken(out var token, out var error) || string.IsNullOrWhiteSpace(token))
                {
                    Console.Error.WriteLine(error ?? "Geen opgeslagen Hugging Face-token gevonden.");
                    Environment.Exit(2);
                }

                var directory = Path.GetDirectoryName(args[1]);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(args[1], token, Encoding.UTF8);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("Token doorgeven mislukt", ex);
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(3);
            }
        }

        if (string.Equals(command, "--recover-interrupted", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var staleSeconds = 120;
                for (var i = 1; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "--stale-seconds", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var parsed) && parsed > 0)
                    {
                        staleSeconds = parsed;
                    }
                }

                var recovered = SessionRecoveryService.RecoverInterruptedSessions(SettingsStore.Load(), TimeSpan.FromSeconds(staleSeconds));
                Console.Out.WriteLine(recovered.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("Herstel van onderbroken opnames mislukt", ex);
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
                    Console.Error.WriteLine("Het bronbestand voor de import ontbreekt.");
                    Environment.Exit(1);
                }

                var fromWatchFolder = args.Any(x => string.Equals(x, "--watch-folder-source", StringComparison.OrdinalIgnoreCase));
                var consumeSource = args.Any(x => string.Equals(x, "--consume-source", StringComparison.OrdinalIgnoreCase));
                var manifest = ImportSessionService.ImportAudio(args[1], SettingsStore.Load(), fromWatchFolder, consumeSource);
                Console.Out.WriteLine(manifest.OutputDirectory);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                AppRuntime.LogException("Audio-import mislukt", ex);
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(4);
            }
        }

        return false;
    }
}
