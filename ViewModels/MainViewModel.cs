using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreservaMetadados.Models;
using PreservaMetadados.Services;
using System.IO;
using System.Windows;

namespace PreservaMetadados.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileService _fileService;
    private readonly FileTransferService _transferService;
    private CancellationTokenSource? _transferCts;

    public FilePaneViewModel SourcePane { get; }
    public FilePaneViewModel DestinationPane { get; }

    [ObservableProperty]
    private TransferProgressInfo transferProgress = new();

    [ObservableProperty]
    private bool isTransferring;

    public MainViewModel()
    {
        _fileService = new FileService();
        _transferService = new FileTransferService();

        SourcePane = new FilePaneViewModel("Origem", _fileService);
        DestinationPane = new FilePaneViewModel("Destino", _fileService);

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await Task.WhenAll(
            SourcePane.InitializeAsync(),
            DestinationPane.InitializeAsync()
        );

        // Se houver mais de um drive, coloca o segundo drive no Destino para conveniência
        if (DestinationPane.Devices.Count > 1)
        {
            DestinationPane.SelectedDevice = DestinationPane.Devices[1];
        }
    }

    [RelayCommand]
    public async Task TransferSourceToDestAsync()
    {
        await ExecuteTransferAsync(sourcePane: SourcePane, destPane: DestinationPane);
    }

    [RelayCommand]
    public async Task TransferDestToSourceAsync()
    {
        await ExecuteTransferAsync(sourcePane: DestinationPane, destPane: SourcePane);
    }

    [RelayCommand]
    public void CancelTransfer()
    {
        if (_transferCts != null && !_transferCts.IsCancellationRequested)
        {
            _transferCts.Cancel();
            TransferProgress.StatusMessage = "Cancelando transferência...";
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        if (IsTransferring)
            return;

        var itemsToDelete = new List<(FilePaneViewModel Pane, FileItem Item)>();

        var sourceSelected = SourcePane.Items.Where(i => i.IsSelected).ToList();
        if (sourceSelected.Count > 0)
        {
            foreach (var item in sourceSelected)
                itemsToDelete.Add((SourcePane, item));
        }

        var destSelected = DestinationPane.Items.Where(i => i.IsSelected).ToList();
        if (destSelected.Count > 0)
        {
            foreach (var item in destSelected)
                itemsToDelete.Add((DestinationPane, item));
        }

        if (itemsToDelete.Count == 0)
        {
            if (SourcePane.SelectedItem != null)
            {
                itemsToDelete.Add((SourcePane, SourcePane.SelectedItem));
            }
            else if (DestinationPane.SelectedItem != null)
            {
                itemsToDelete.Add((DestinationPane, DestinationPane.SelectedItem));
            }
        }

        if (itemsToDelete.Count == 0)
        {
            MessageBox.Show("Nenhum arquivo ou pasta selecionado para exclusão.\nPor favor, selecione os itens que deseja remover.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Tem certeza que deseja excluir permanentemente {itemsToDelete.Count} item(ns) selecionado(s)?",
            "Confirmar Exclusão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        IsTransferring = true;
        var errors = new List<string>();

        try
        {
            foreach (var (pane, item) in itemsToDelete)
            {
                try
                {
                    await _fileService.DeleteItemAsync(item);
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.Name}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    $"Ocorreram erros ao excluir alguns itens:\n\n{string.Join("\n", errors)}",
                    "Erro de Exclusão",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            IsTransferring = false;
            var affectedPanes = itemsToDelete.Select(x => x.Pane).Distinct();
            foreach (var pane in affectedPanes)
            {
                await pane.RefreshAsync();
            }
        }
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        await Task.WhenAll(
            SourcePane.LoadDevicesAsync(),
            DestinationPane.LoadDevicesAsync()
        );
        await Task.WhenAll(
            SourcePane.RefreshAsync(),
            DestinationPane.RefreshAsync()
        );
    }

    private async Task ExecuteTransferAsync(FilePaneViewModel sourcePane, FilePaneViewModel destPane)
    {
        if (IsTransferring)
            return;

        var selectedItems = sourcePane.GetSelectedOrFocusedItems();
        if (selectedItems.Count == 0)
        {
            MessageBox.Show("Nenhum arquivo ou pasta selecionado para transferir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var destPath = destPane.CurrentPath;
        var destIsMtp = destPane.SelectedDevice?.DeviceType == DeviceItemType.MtpDevice;
        var destMtpDeviceId = destIsMtp ? destPane.SelectedDevice?.MtpDeviceId : null;

        if (string.IsNullOrWhiteSpace(destPath))
        {
            MessageBox.Show("Selecione uma pasta de destino válida no painel de Destino.", "Destino Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Prevenir cópia sobre o mesmo diretório
        if (!destIsMtp && string.Equals(sourcePane.CurrentPath?.TrimEnd('\\'), destPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("A pasta de origem e a pasta de destino são a mesma!", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _transferCts = new CancellationTokenSource();
        IsTransferring = true;
        TransferProgress.Reset();
        TransferProgress.IsTransferring = true;

        var progressHandler = new Progress<TransferProgressInfo>(info =>
        {
            TransferProgress.CurrentFileName = info.CurrentFileName;
            TransferProgress.CurrentFileIndex = info.CurrentFileIndex;
            TransferProgress.TotalFiles = info.TotalFiles;
            TransferProgress.BytesTransferred = info.BytesTransferred;
            TransferProgress.TotalBytes = info.TotalBytes;
            TransferProgress.CurrentFileBytes = info.CurrentFileBytes;
            TransferProgress.CurrentFileTotalBytes = info.CurrentFileTotalBytes;
            TransferProgress.SpeedFormatted = info.SpeedFormatted;
            TransferProgress.EtaFormatted = info.EtaFormatted;
            TransferProgress.OverallPercentage = info.OverallPercentage;
            TransferProgress.FilePercentage = info.FilePercentage;
            TransferProgress.StatusMessage = info.StatusMessage;
            TransferProgress.IsTransferring = info.IsTransferring;
        });

        try
        {
            await _transferService.TransferItemsAsync(
                selectedItems,
                destPath,
                destIsMtp,
                destMtpDeviceId,
                progressHandler,
                _transferCts.Token
            );

            // Atualiza o painel de destino para mostrar os novos arquivos com as datas preservadas
            await destPane.RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            TransferProgress.StatusMessage = "Transferência cancelada pelo usuário.";
            TransferProgress.IsTransferring = false;
        }
        catch (Exception ex)
        {
            TransferProgress.StatusMessage = $"Erro durante transferência: {ex.Message}";
            TransferProgress.IsTransferring = false;
            MessageBox.Show($"Ocorreu um erro durante a transferência:\n{ex.Message}", "Erro de Transferência", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsTransferring = false;
            _transferCts.Dispose();
            _transferCts = null;
        }
    }
}
