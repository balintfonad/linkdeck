using System.Globalization;

namespace LinkDeck.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public interface ILog
{
    void Write(LogLevel level, string message, Exception? exception = null);
}

public static class LogExtensions
{
    public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);

    public static void Warn(this ILog log, string message, Exception? ex = null) => log.Write(LogLevel.Warning, message, ex);

    public static void Error(this ILog log, string message, Exception? ex = null) => log.Write(LogLevel.Error, message, ex);
}

public sealed class NullLog : ILog
{
    public static NullLog Instance { get; } = new();

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
    }
}

/// <summary>
/// Daily rolling log files (<c>linkdeck-yyyyMMdd.log</c>), kept for 7 days.
/// Never pass keystrokes or secrets to this logger.
/// </summary>
public sealed class FileLog : ILog
{
    public const int RetentionDays = 7;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly TimeProvider _time;

    public FileLog(string directory, TimeProvider? time = null)
    {
        _directory = directory;
        _time = time ?? TimeProvider.System;
        try
        {
            Directory.CreateDirectory(directory);
            Prune();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public string CurrentFile => Path.Combine(_directory, $"linkdeck-{_time.GetLocalNow():yyyyMMdd}.log");

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        var now = _time.GetLocalNow();
        var line = string.Create(CultureInfo.InvariantCulture, $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}");
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_gate)
        {
            try
            {
                File.AppendAllText(CurrentFile, line + Environment.NewLine);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Deletes log files older than <see cref="RetentionDays"/> days.</summary>
    public void Prune()
    {
        var cutoff = _time.GetLocalNow().Date.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "linkdeck-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["linkdeck-".Length..];
            if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < cutoff)
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
