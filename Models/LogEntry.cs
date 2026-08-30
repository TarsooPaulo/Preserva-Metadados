using System;

namespace PreservaMetadados.Models;

/// <summary>
/// Níveis de severidade do log
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Representa uma entrada de log do sistema
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? Context { get; set; }
    public Exception? Exception { get; set; }
    
    public LogEntry()
    {
        Timestamp = DateTime.UtcNow;
    }
    
    public LogEntry(LogLevel level, string message, string? source = null, string? context = null)
        : this()
    {
        Level = level;
        Message = message;
        Source = source;
        Context = context;
    }
    
    public string FormattedLevel => Level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT",
        _ => Level.ToString().ToUpperInvariant()
    };
    
    public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
    
    public string FullMessage
    {
        get
        {
            var msg = $"[{FormattedTimestamp}] [{FormattedLevel}]";
            if (!string.IsNullOrEmpty(Source))
                msg += $" [{Source}]";
            if (!string.IsNullOrEmpty(Context))
                msg += $" [{Context}]";
            msg += $" {Message}";
            if (Exception != null)
                msg += $"\nException: {Exception.GetType().Name}: {Exception.Message}\n{Exception.StackTrace}";
            return msg;
        }
    }
    
    public string ToCsvString()
    {
        var csv = $"{FormattedTimestamp},{FormattedLevel},{EscapeCsv(Source)},{EscapeCsv(Context)},{EscapeCsv(Message)}";
        if (Exception != null)
        {
            csv += $",{EscapeCsv(Exception.GetType().Name)},{EscapeCsv(Exception.Message)}";
        }
        return csv;
    }
    
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        
        // Escapa aspas duplas e pontos-e-vírgulas
        var escaped = value.Replace("\"", "\"\"");
        if (escaped.Contains(";"))
            escaped = $"\"{escaped}\"";
        
        return escaped;
    }
}