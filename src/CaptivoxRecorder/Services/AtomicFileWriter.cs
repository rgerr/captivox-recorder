using System.Text;

namespace LocalCaptureAgent.Services;

internal static class AtomicFileWriter
{
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Pad voor atomische write ontbreekt.", nameof(path)); // geen vertaling
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var tempPath = BuildTempPath(path);
        try
        {
            File.WriteAllText(tempPath, contents, encoding);
            // Een lezer (bijvoorbeeld een virusscanner) die het doelbestand net open heeft, laat de vervanging even mislukken.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tempPath, path, overwrite: true);
                    break;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 10)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    public static async Task WriteAllTextAsync(string path, string contents, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Pad voor atomische write ontbreekt.", nameof(path)); // geen vertaling
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var tempPath = BuildTempPath(path);
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, encoding, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    private static string BuildTempPath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        var tempFileName = $".{fileName}.{Guid.NewGuid():N}.tmp";
        return string.IsNullOrWhiteSpace(directory)
            ? tempFileName
            : Path.Combine(directory, tempFileName);
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
        }
    }
}
