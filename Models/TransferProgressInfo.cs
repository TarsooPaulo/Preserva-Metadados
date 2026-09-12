using CommunityToolkit.Mvvm.ComponentModel;

namespace PreservaMetadados.Models;

public partial class TransferProgressInfo : ObservableObject
{
    [ObservableProperty]
    private string currentFileName = string.Empty;

    [ObservableProperty]
    private int currentFileIndex;

    [ObservableProperty]
    private int totalFiles;

    [ObservableProperty]
    private long bytesTransferred;

    [ObservableProperty]
    private long totalBytes;

    [ObservableProperty]
    private long currentFileBytes;

    [ObservableProperty]
    private long currentFileTotalBytes;

    [ObservableProperty]
    private double speedBytesPerSec;

    [ObservableProperty]
    private string speedFormatted = "0 KB/s";

    [ObservableProperty]
    private TimeSpan eta = TimeSpan.Zero;

    [ObservableProperty]
    private string etaFormatted = "--:--";

    [ObservableProperty]
    private double overallPercentage;

    [ObservableProperty]
    private double filePercentage;

    [ObservableProperty]
    private string statusMessage = "Pronto";

    [ObservableProperty]
    private bool isTransferring;

    public void UpdateSpeedAndEta(double bytesPerSec, long remainingBytes)
    {
        SpeedBytesPerSec = bytesPerSec;
        SpeedFormatted = $"{FileItem.FormatBytes((long)bytesPerSec)}/s";

        if (bytesPerSec > 1024 && remainingBytes > 0)
        {
            var seconds = remainingBytes / bytesPerSec;
            if (seconds < 3600 * 24)
            {
                Eta = TimeSpan.FromSeconds(seconds);
                EtaFormatted = Eta.ToString(Eta.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
                return;
            }
        }
        EtaFormatted = "--:--";
    }

    public void Reset()
    {
        CurrentFileName = string.Empty;
        CurrentFileIndex = 0;
        TotalFiles = 0;
        BytesTransferred = 0;
        TotalBytes = 0;
        CurrentFileBytes = 0;
        CurrentFileTotalBytes = 0;
        SpeedBytesPerSec = 0;
        SpeedFormatted = "0 KB/s";
        Eta = TimeSpan.Zero;
        EtaFormatted = "--:--";
        OverallPercentage = 0;
        FilePercentage = 0;
        StatusMessage = "Pronto";
        IsTransferring = false;
    }
}
