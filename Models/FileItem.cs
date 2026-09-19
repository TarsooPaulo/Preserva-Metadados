using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace PreservaMetadados.Models;

public partial class FileItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    private long length;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedLastWrite))]
    private DateTime? lastWriteTimeUtc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedCreation))]
    private DateTime? creationTimeUtc;

    public string Extension { get; set; } = string.Empty;
        public string DisplayName => Name;
        public override string ToString() => Name ?? string.Empty;
    public bool IsMtp { get; set; }
    public string MtpDeviceId { get; set; } = string.Empty;

    [ObservableProperty]
    private bool isSelected;

    public event Action? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }

    public string FormattedLastWrite => LastWriteTimeUtc.HasValue 
        ? LastWriteTimeUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") 
        : "-";

    public string FormattedCreation => CreationTimeUtc.HasValue 
        ? CreationTimeUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") 
        : "-";

    public string FormattedSize
    {
        get
        {
            if (IsDirectory) return "<Pasta>";
            return FormatBytes(Length);
        }
    }

    public string IconKind
    {
        get
        {
            if (IsDirectory) return "Folder";
            var ext = (Extension ?? Path.GetExtension(Name)).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".raw" or ".cr2" or ".nef" => "FileImage",
                ".mp4" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".flv" or ".webm" or ".m4v" => "FileVideo",
                ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" => "FileMusic",
                ".pdf" => "FilePdfBox",
                ".doc" or ".docx" => "FileWord",
                ".xls" or ".xlsx" or ".csv" => "FileExcel",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "FolderZip",
                ".txt" or ".log" or ".md" => "FileDocument",
                ".cs" or ".js" or ".ts" or ".html" or ".css" or ".json" or ".xml" or ".py" or ".cpp" or ".h" => "FileCode",
                ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => "Application",
                _ => "FileOutline"
            };
        }
    }

    public string IconColor
    {
        get
        {
            if (IsDirectory) return "#F6C344"; // Folder yellow
            var ext = (Extension ?? Path.GetExtension(Name)).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".raw" or ".cr2" or ".nef" => "#4CAF50", // Green
                ".mp4" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".flv" or ".webm" or ".m4v" => "#E91E63", // Pink/Red
                ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" => "#9C27B0", // Purple
                ".pdf" => "#F44336", // Red
                ".doc" or ".docx" => "#2196F3", // Blue
                ".xls" or ".xlsx" or ".csv" => "#4CAF50", // Green
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "#FF9800", // Orange
                _ => "#90CAF9" // Light blue
            };
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
