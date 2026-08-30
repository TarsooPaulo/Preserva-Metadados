using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PreservaMetadados.Models;

/// <summary>
/// Representa um item individual de transferência de arquivo.
/// </summary>
public class TransferItem : INotifyPropertyChanged
{
    private string _sourcePath = string.Empty;
    private string _destinationPath = string.Empty;
    private long _fileSize;
    private long _transferredBytes;
    private TransferStatus _status = TransferStatus.Pending;
    private string? _errorMessage;
    private DateTime _startTime;
    private DateTime? _endTime;
    private FileMetadata? _sourceMetadata;
    private FileMetadata? _destinationMetadata;
    private ConflictResolution _conflictResolution = ConflictResolution.Ask;
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            _sourcePath = value;
            OnPropertyChanged();
        }
    }
    
    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            _destinationPath = value;
            OnPropertyChanged();
        }
    }
    
    public long FileSize
    {
        get => _fileSize;
        set
        {
            _fileSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressPercentage));
            OnPropertyChanged(nameof(FormattedFileSize));
        }
    }
    
    public long TransferredBytes
    {
        get => _transferredBytes;
        set
        {
            _transferredBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressPercentage));
            OnPropertyChanged(nameof(FormattedTransferred));
            OnPropertyChanged(nameof(TransferSpeed));
            OnPropertyChanged(nameof(EstimatedTimeRemaining));
        }
    }
    
    public TransferStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsInProgress));
            OnPropertyChanged(nameof(IsFailed));
            if (value == TransferStatus.Completed || value == TransferStatus.Failed)
            {
                _endTime = DateTime.UtcNow;
                OnPropertyChanged(nameof(TransferDuration));
            }
        }
    }
    
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }
    
    public DateTime StartTime
    {
        get => _startTime;
        set
        {
            _startTime = value;
            OnPropertyChanged();
        }
    }
    
    public DateTime? EndTime
    {
        get => _endTime;
        set
    {
        _endTime = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(TransferDuration));
    }
    }
    
    public FileMetadata? SourceMetadata
    {
        get => _sourceMetadata;
        set
        {
            _sourceMetadata = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMetadata));
        }
    }
    
    public FileMetadata? DestinationMetadata
    {
        get => _destinationMetadata;
        set
        {
            _destinationMetadata = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMetadata));
        }
    }
    
    public ConflictResolution ConflictResolution
    {
        get => _conflictResolution;
        set
        {
            _conflictResolution = value;
            OnPropertyChanged();
        }
    }
    
    // Propriedades calculadas
    public double ProgressPercentage => FileSize > 0 ? (double)TransferredBytes / FileSize * 100 : 0;
    
    public string FormattedFileSize => FormatFileSize(FileSize);
    
    public string FormattedTransferred => FormatFileSize(TransferredBytes);
    
    public string StatusText => Status switch
    {
        TransferStatus.Pending => "Aguardando",
        TransferStatus.InProgress => "Transferindo",
        TransferStatus.Completed => "Concluído",
        TransferStatus.Failed => "Falhou",
        TransferStatus.Skipped => "Ignorado",
        TransferStatus.Conflict => "Conflito",
        _ => "Desconhecido"
    };
    
    public bool IsCompleted => Status == TransferStatus.Completed;
    
    public bool IsInProgress => Status == TransferStatus.InProgress;
    
    public bool IsFailed => Status == TransferStatus.Failed;
    
    public bool HasMetadata => SourceMetadata != null || DestinationMetadata != null;
    
    public TimeSpan? TransferDuration => EndTime.HasValue ? EndTime.Value - StartTime : null;
    
    public double TransferSpeed
    {
        get
        {
            if (Status != TransferStatus.InProgress || !EndTime.HasValue)
                return 0;
            
            var elapsed = DateTime.UtcNow - StartTime;
            if (elapsed.TotalSeconds > 0)
                return TransferredBytes / elapsed.TotalSeconds;
            return 0;
        }
    }
    
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (Status != TransferStatus.InProgress || TransferSpeed <= 0)
                return null;
            
            var remainingBytes = FileSize - TransferredBytes;
            var estimatedSeconds = remainingBytes / TransferSpeed;
            return TimeSpan.FromSeconds(estimatedSeconds);
        }
    }
    
    public TransferItem()
    {
        _startTime = DateTime.UtcNow;
    }
    
    public TransferItem(string sourcePath, string destinationPath, long fileSize) : this()
    {
        _sourcePath = sourcePath;
        _destinationPath = destinationPath;
        _fileSize = fileSize;
    }
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public enum TransferStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped,
    Conflict
}