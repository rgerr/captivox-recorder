using System.Diagnostics;
using System.Text;
using Microsoft.Toolkit.Uwp.Notifications;

namespace LocalCaptureAgent.Services;

internal static class WindowsToastNotificationService
{
    private static readonly object SyncRoot = new();
    private static bool _initialized;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                try
                {
                    var args = ToastArguments.Parse(toastArgs.Argument);
                    if (!args.TryGetValue("action", out var action))
                    {
                        return;
                    }

                    if (!args.TryGetValue("target", out var encodedTarget))
                    {
                        return;
                    }

                    var target = DecodeValue(encodedTarget);
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        return;
                    }

                    var caption = string.Equals(action, "openFolder", StringComparison.OrdinalIgnoreCase)
                        ? "Sessiemap openen mislukt"
                        : "Transcript openen mislukt";

                    OpenPathInShell(target, caption);
                }
                catch (Exception ex)
                {
                    AppRuntime.LogException("Toast activation handling failed", ex);
                }
            };

            _initialized = true;
        }
    }


    public static bool TryShowTranscriptReady(string meetingId, string? transcriptPath, string? sessionDirectory)
    {
        try
        {
            var transcriptExists = !string.IsNullOrWhiteSpace(transcriptPath) && File.Exists(transcriptPath);
            var folderExists = !string.IsNullOrWhiteSpace(sessionDirectory) && Directory.Exists(sessionDirectory);
            var defaultAction = transcriptExists ? "openTranscript" : "openFolder";
            var defaultTarget = transcriptExists ? transcriptPath : sessionDirectory;

            if (string.IsNullOrWhiteSpace(defaultTarget))
            {
                return false;
            }

            var builder = new ToastContentBuilder()
                .AddArgument("action", defaultAction)
                .AddArgument("target", EncodeValue(defaultTarget!))
                .AddText(UiText.T("Transcript gereed"))
                .AddText(UiText.F("Sessie {0} is afgerond. Open direct het transcript of de sessiemap.", meetingId));

            if (transcriptExists)
            {
                builder.AddButton(new ToastButton()
                    .SetContent(UiText.T("Open transcript"))
                    .AddArgument("action", "openTranscript")
                    .AddArgument("target", EncodeValue(transcriptPath!))
                    .SetBackgroundActivation());
            }

            if (folderExists)
            {
                builder.AddButton(new ToastButton()
                    .SetContent(UiText.T("Open map"))
                    .AddArgument("action", "openFolder")
                    .AddArgument("target", EncodeValue(sessionDirectory!))
                    .SetBackgroundActivation());
            }

            builder.Show();
            return true;
        }
        catch (Exception ex)
        {
            AppRuntime.LogException("Native Windows toast failed", ex);
            return false;
        }
    }

    private static string EncodeValue(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string? DecodeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch
        {
            return value;
        }
    }

    private static void OpenPathInShell(string path, string errorCaption)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppRuntime.LogException(errorCaption, ex);
        }
    }
}
