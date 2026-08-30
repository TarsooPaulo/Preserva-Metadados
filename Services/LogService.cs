using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using PreservaMetadados.Models;

namespace PreservaMetadados.Services;

/// <summary>
/// Serviço de logging para a aplicação.
/// </summary>
public class LogService
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = new();
    private readonly int _maxEntries = 1000;
    
    public event EventHandler<LogEntry>? LogEntryAdded;
    
    public void Log(LogLevel level, string message, string? source = null, string? context = null, Exception? exception = null)
    {
        var entry = new LogEntry(level, message, source, context)
        {
            Exception = exception,
            Timestamp = DateTime.UtcNow
        };
        
        AddEntry(entry);
    }
    
    public void LogDebug(string message, string? source = null, string? context = null)
    {
        Log(LogLevel.Debug, message, source, context);
    }
    
    public void LogInfo(string message, string? source = null, string? context = null)
    {
        Log(LogLevel.Info, message, source, context);
    }
    
    public void LogWarning(string message, string? source = null, string? context = null)
    {
        Log(LogLevel.Warning, message, source, context);
    }
    
    public void LogError(string message, string? source = null, Exception? exception = null)
    {
        Log(LogLevel.Error, message, source, null, exception);
    }
    
    public void LogCritical(string message, string? source = null, Exception? exception = null)
    {
        Log(LogLevel.Critical, message, source, null, exception);
    }
    
    private void AddEntry(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            
            // Remove entradas antigas se exceder o limite
            if (_entries.Count > _maxEntries)
            {
                _entries.RemoveAt(0);
            }
            
            // Notifica handlers
            LogEntryAdded?.Invoke(this, entry);
        }
    }
    
    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToList().AsReadOnly();
        }
    }
    
    public IReadOnlyList<LogEntry> GetEntries(LogLevel minLevel)
    {
        lock (_lock)
        {
            return _entries.Where(e => e.Level >= minLevel).ToList().AsReadOnly();
        }
    }
    
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
    
    public void ExportLogs(string filePath)
    {
        try
        {
            var entries = GetEntries();
            var csv = new StringBuilder();
            
            // Cabeçalho
            csv.AppendLine("Timestamp,Level,Source,Context,Message,ExceptionType,ExceptionMessage");
            
            // Entradas
            foreach (var entry in entries)
            {
                csv.AppendLine(entry.ToCsvString());
            }
            
            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LogError("Erro ao exportar logs", "LogService", ex);
            throw;
        }
    }
}