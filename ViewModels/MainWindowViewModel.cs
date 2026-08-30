using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PreservaMetadados.Models;
using PreservaMetadados.Services;
using PreservaMetadados.Views;

namespace PreservaMetadados.ViewModels;

/// <summary>
/// ViewModel principal da aplicação.
/// Gerencia transferências de arquivos, drag-and-drop, progresso e logs.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly FileTransferService _transferService;
    private readonly LogService _logService;
    private readonly MtpDeviceService _mtpService;
    private readonly WifiTransferService _wifiService;
    
    [ObservableProperty]
    private string _title = "Preserva-Metadados";
    
    [ObservableProperty]
    private bool _isBusy;
    
    [ObservableProperty]
    private string? _statusMessage;
    
    [ObservableProperty]
    private double _overallProgress;
    
    [ObservableProperty]
    private string? _deviceStatus = "Nenhum dispositivo conectado";
    
    [ObservableProperty]
    private bool _isConnected;
    
    [ObservableProperty]
    private bool _isDarkTheme = true;
    
    [ObservableProperty]
    private string? _sourcePath;
    
    [ObservableProperty]
    private string? _destinationPath;
    
    [ObservableProperty]
    private ConflictResolutionSettings _conflictSettings = new();
    
    [ObservableProperty]
    private ConnectionMode _connectionMode = ConnectionMode.UsbMtp;
    
    [ObservableProperty]
    private TransferDirection _transferDirection = TransferDirection.PcToPhone;
    
    [ObservableProperty]
    private bool _preserveMetadata = true;
    
    [ObservableProperty]
    private bool _verifyTransfer = true;
    
    [ObservableProperty]
    private int _totalFiles;
    
    [ObservableProperty]
    private long _totalBytes;
    
    [ObservableProperty]
    private int _completedFiles;
    
    [ObservableProperty]
    private long _transferredBytes;
    
    [ObservableProperty]
    private DateTime? _transferStartTime;
    
    [ObservableProperty]
    private DateTime? _transferEndTime;
    
    private CancellationTokenSource? _cancellationTokenSource;
    
    public ObservableCollection<TransferItem> TransferItems { get; } = new();
    
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    
    public MainWindowViewModel(
        FileTransferService transferService,
        LogService logService,
        MtpDeviceService mtpService,
        WifiTransferService wifiService)
    {
        _transferService = transferService;
        _logService = logService;
        _mtpService = mtpService;
        _wifiService = wifiService;
        
        // Assina eventos de log
        _logService.LogEntryAdded += OnLogEntryAdded;
        
        // Assina eventos de transferência
        _transferService.TransferStarted += OnTransferStarted;
        _transferService.TransferProgressChanged += OnTransferProgressChanged;
        _transferService.TransferCompleted += OnTransferCompleted;
        _transferService.TransferFailed += OnTransferFailed;
        
        // Assina mudanças na coleção de transferências
        TransferItems.CollectionChanged += OnTransferItemsCollectionChanged;
        
        // Log inicial
        _logService.LogInfo("Aplicação iniciada", "MainWindowViewModel");
        _ = CheckDeviceConnection();
    }
    
    private void OnLogEntryAdded(object? sender, LogEntry e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogEntries.Add(e);
            if (LogEntries.Count > 1000)
            {
                LogEntries.RemoveAt(0);
            }
        });
    }
    
    private void OnTransferStarted(object? sender, TransferItem item)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!TransferItems.Contains(item))
            {
                TransferItems.Add(item);
            }
            _logService.LogInfo($"Iniciando transferência: {item.SourcePath} → {item.DestinationPath}", "Transfer");
        });
    }
    
    private void OnTransferProgressChanged(object? sender, (TransferItem item, long bytesTransferred) e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            e.item.TransferredBytes = e.bytesTransferred;
            UpdateOverallProgress();
        });
    }
    
    private void OnTransferCompleted(object? sender, TransferItem item)
    {
        Dispatcher.UIThread.Post(() =>
        {
            item.Status = TransferStatus.Completed;
            item.EndTime = DateTime.UtcNow;
            CompletedFiles++;
            TransferredBytes += item.FileSize;
            UpdateOverallProgress();
            _logService.LogInfo($"Transferência concluída: {item.SourcePath}", "Transfer");
        });
    }
    
    private void OnTransferFailed(object? sender, (TransferItem item, Exception exception) e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            e.item.Status = TransferStatus.Failed;
            e.item.ErrorMessage = e.exception.Message;
            e.item.EndTime = DateTime.UtcNow;
            _logService.LogError($"Falha na transferência: {e.item.SourcePath}", "Transfer", e.exception);
        });
    }
    
    private void OnTransferItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TotalFiles = TransferItems.Count;
        TotalBytes = TransferItems.Sum(t => t.FileSize);
        UpdateOverallProgress();
        UpdateStatus();
    }
    
    private void UpdateOverallProgress()
    {
        if (TotalFiles > 0)
        {
            var completed = TransferItems.Count(t => t.IsCompleted);
            var failed = TransferItems.Count(t => t.IsFailed);
            var inProgress = TransferItems.Count(t => t.IsInProgress);
            
            OverallProgress = (double)completed / TotalFiles * 100;
            CompletedFiles = completed;
            
            if (completed + failed == TotalFiles && inProgress == 0 && TransferStartTime.HasValue)
            {
                TransferEndTime = DateTime.UtcNow;
                var duration = TransferEndTime - TransferStartTime;
                StatusMessage = $"Transferência concluída em {duration?.TotalSeconds:F1} segundos";
            }
        }
        else
        {
            OverallProgress = 0;
        }
    }
    
    [RelayCommand]
    private async Task CheckDeviceConnection()
    {
        IsBusy = true;
        StatusMessage = "Verificando conexão com dispositivo...";
        
        try
        {
            bool connected;
            if (ConnectionMode == ConnectionMode.UsbMtp)
            {
                connected = await _mtpService.CheckDeviceConnectionAsync();
                DeviceStatus = connected ? "Dispositivo MTP conectado" : "Nenhum dispositivo MTP encontrado";
            }
            else
            {
                connected = await _wifiService.CheckConnectionAsync();
                DeviceStatus = connected ? "Servidor Wi-Fi disponível" : "Servidor Wi-Fi não encontrado";
            }
            
            IsConnected = connected;
        }
        catch (Exception ex)
        {
            _logService.LogError("Erro ao verificar conexão", "DeviceCheck", ex);
            DeviceStatus = "Erro ao verificar conexão";
            IsConnected = false;
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }
    
    [RelayCommand]
    private async Task AddFiles(string[]? files)
    {
        if (files != null && files.Length > 0)
        {
            AddFilesInternal(files);
            return;
        }
        
        // Se nenhum arquivo fornecido via parâmetro, abre o FilePicker do Avalonia
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            if (topLevel != null)
            {
                var storageFiles = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Selecionar Arquivos para Transferência",
                    AllowMultiple = true
                });

                if (storageFiles != null && storageFiles.Count > 0)
                {
                    var paths = storageFiles.Select(f => f.Path.LocalPath).ToArray();
                    AddFilesInternal(paths);
                }
            }
        }
    }
    
    public void AddFilesFromPaths(string[] files)
    {
        AddFilesInternal(files);
    }
    
    private void AddFilesInternal(string[] files)
    {
        foreach (var file in files)
        {
            if (File.Exists(file))
            {
                var item = new TransferItem(file, string.Empty, new FileInfo(file).Length)
                {
                    Status = TransferStatus.Pending
                };
                TransferItems.Add(item);
                _logService.LogInfo($"Arquivo adicionado: {file}", "DragDrop");
            }
            else if (Directory.Exists(file))
            {
                AddDirectory(file);
            }
        }
        
        TotalFiles = TransferItems.Count;
        TotalBytes = TransferItems.Sum(t => t.FileSize);
        UpdateStatus();
    }
    
    private void AddDirectory(string directoryPath)
    {
        try
        {
            var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                AddFilesInternal(new[] { file });
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"Erro ao adicionar diretório: {directoryPath}", "DragDrop", ex);
        }
    }
    
    [RelayCommand]
    private void RemoveItem(TransferItem item)
    {
        TransferItems.Remove(item);
        TotalFiles = TransferItems.Count;
        TotalBytes = TransferItems.Sum(t => t.FileSize);
        UpdateStatus();
    }
    
    [RelayCommand]
    private void ClearItems()
    {
        TransferItems.Clear();
        TotalFiles = 0;
        TotalBytes = 0;
        TransferredBytes = 0;
        CompletedFiles = 0;
        OverallProgress = 0;
        UpdateStatus();
    }
    
    [RelayCommand]
    private async Task StartTransfer()
    {
        if (TransferItems.Count == 0)
        {
            StatusMessage = "Nenhum arquivo para transferir";
            return;
        }
        
        if (string.IsNullOrEmpty(DestinationPath))
        {
            // Sugere pasta padrão se vazia
            DestinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PreservaTransfer");
        }
        
        _cancellationTokenSource = new CancellationTokenSource();
        TransferStartTime = DateTime.UtcNow;
        TransferEndTime = null;
        IsBusy = true;
        StatusMessage = "Iniciando transferência...";
        
        try
        {
            var transferItems = TransferItems.ToList();
            var options = new TransferOptions
            {
                Direction = TransferDirection,
                PreserveMetadata = PreserveMetadata,
                VerifyTransfer = VerifyTransfer,
                ConflictResolution = ConflictSettings,
                CancellationToken = _cancellationTokenSource.Token
            };
            
            await _transferService.TransferFilesAsync(transferItems, DestinationPath, options, cancellationToken: _cancellationTokenSource.Token);
            
            _logService.LogInfo($"Transferência concluída para {transferItems.Count} arquivos", "Transfer");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Transferência cancelada pelo usuário";
            _logService.LogWarning("Transferência cancelada", "Transfer");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
            _logService.LogError("Erro durante a transferência", "Transfer", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    [RelayCommand]
    private void CancelTransfer()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Cancelando transferência...";
        _logService.LogWarning("Solicitado cancelamento de transferência", "Transfer");
    }
    
    [RelayCommand]
    private void OpenSettings()
    {
        var settingsViewModel = Program.AppHost?.Services.GetService<SettingsViewModel>() 
            ?? new SettingsViewModel(_logService);
            
        var settingsWindow = new SettingsWindow
        {
            DataContext = settingsViewModel
        };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            settingsWindow.ShowDialog(desktop.MainWindow);
        }
        else
        {
            settingsWindow.Show();
        }
    }
    
    [RelayCommand]
    private void ExportLogs()
    {
        var logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PreservaMetadados");
        if (!Directory.Exists(logFolder))
        {
            Directory.CreateDirectory(logFolder);
        }
        
        var logPath = Path.Combine(logFolder, $"logs-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        
        try
        {
            _logService.ExportLogs(logPath);
            StatusMessage = $"Logs exportados para: {logPath}";
        }
        catch (Exception ex)
        {
            _logService.LogError("Erro ao exportar logs", "LogExport", ex);
            StatusMessage = "Erro ao exportar logs";
        }
    }
    
    [RelayCommand]
    private void SwitchTheme(bool isDark)
    {
        IsDarkTheme = isDark;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
        _logService.LogInfo($"Tema alterado para {(isDark ? "escuro" : "claro")}", "Theme");
    }
    
    [RelayCommand]
    private async Task BrowseSource()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Selecionar Pasta de Origem"
                });

                if (folders != null && folders.Count > 0)
                {
                    SourcePath = folders[0].Path.LocalPath;
                }
            }
        }
    }
    
    [RelayCommand]
    private async Task BrowseDestination()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Selecionar Pasta de Destino"
                });

                if (folders != null && folders.Count > 0)
                {
                    DestinationPath = folders[0].Path.LocalPath;
                }
            }
        }
    }
    
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        
        if (e.PropertyName == nameof(IsConnected) || 
            e.PropertyName == nameof(DestinationPath) ||
            e.PropertyName == nameof(SourcePath))
        {
            UpdateStatus();
        }
    }
    
    private void UpdateStatus()
    {
        if (TransferItems.Count == 0)
            StatusMessage = "Adicione arquivos ou arraste pastas para iniciar";
        else if (string.IsNullOrEmpty(DestinationPath))
            StatusMessage = "Pronto para transferir (destino padrão será criado)";
        else
            StatusMessage = "Pronto para transferir";
    }
}