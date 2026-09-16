using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic.FileIO;
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

    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        var sourceItems = SourcePane.GetSelectedOrFocusedItems().Where(i => i.IsSelected).ToList();
        var destItems = DestinationPane.GetSelectedOrFocusedItems().Where(i => i.IsSelected).ToList();

        var allSelectedItems = new List<FileItem>();
        if (sourceItems.Count > 0)
            allSelectedItems.AddRange(sourceItems);
        if (destItems.Count > 0)
            allSelectedItems.AddRange(destItems);

        if (allSelectedItems.Count == 0)
        {
            MessageBox.Show("Nenhum arquivo ou pasta selecionado para excluir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmResult = MessageBox.Show(
            $"Tem certeza que deseja excluir {allSelectedItems.Count} item(ns) selecionado(s)?\n\nEsta ação enviará os itens para a Lixeira do Windows (quando possível).",
            "Confirmar Exclusão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmResult != MessageBoxResult.Yes)
            return;

        IsTransferring = true;
        TransferProgress.StatusMessage = "Excluindo itens...";

        try
        {
            await Task.Run(() =>
            {
                foreach (var item in allSelectedItems)
                {
                    if (item.IsMtp || !string.IsNullOrEmpty(item.MtpDeviceId))
                    {
                        // Exclusão para dispositivos MTP (definitiva)
                        DeleteMtpItem(item);
                    }
                    else
                    {
                        // Exclusão para sistema de arquivos local (envia para Lixeira)
                        if (item.IsDirectory)
                        {
                            FileSystem.DeleteDirectory(
                                item.FullPath,
                                UIOption.AllDialogs,
                                RecycleOption.SendToRecycleBin,
                                UICancelOption.ThrowException);
                        }
                        else
                        {
                            FileSystem.DeleteFile(
                                item.FullPath,
                                UIOption.AllDialogs,
                                RecycleOption.SendToRecycleBin,
                                UICancelOption.ThrowException);
                        }
                    }
                }
            });

            // Atualiza ambos os painéis após exclusão
            await SourcePane.RefreshAsync();
            await DestinationPane.RefreshAsync();

            TransferProgress.StatusMessage = "Exclusão concluída.";
        }
        catch (OperationCanceledException)
        {
            TransferProgress.StatusMessage = "Exclusão cancelada pelo usuário.";
        }
        catch (Exception ex)
        {
            TransferProgress.StatusMessage = $"Erro durante exclusão: {ex.Message}";
            MessageBox.Show($"Ocorreu um erro durante a exclusão:\n{ex.Message}", "Erro de Exclusão", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsTransferring = false;
        }
    }

    private void DeleteMtpItem(FileItem item)
    {
        // Para dispositivos MTP, a exclusão é definitiva
        // A biblioteca MediaDevices não oferece suporte direto à exclusão via API pública
        // Esta implementação lança uma exceção para informar que a funcionalidade não está disponível
        // Em uma versão futura, seria necessário implementar acesso direto ao dispositivo MTP
        throw new NotSupportedException("Exclusão de itens em dispositivos MTP (celulares/câmeras) ainda não é suportada nesta versão.");
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
