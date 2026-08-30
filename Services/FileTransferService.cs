using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PreservaMetadados.Models;

namespace PreservaMetadados.Services;

/// <summary>
/// Serviço principal de transferência de arquivos com preservação de metadados.
/// </summary>
public class FileTransferService
{
    private readonly FileMetadataService _metadataService;
    private readonly LogService _logService;
    
    public event EventHandler<TransferItem>? TransferStarted;
    public event EventHandler<(TransferItem item, long bytesTransferred)>? TransferProgressChanged;
    public event EventHandler<TransferItem>? TransferCompleted;
    public event EventHandler<(TransferItem item, Exception exception)>? TransferFailed;
    
    public FileTransferService(FileMetadataService metadataService, LogService logService)
    {
        _metadataService = metadataService;
        _logService = logService;
    }
    
    public async Task TransferFilesAsync(
        List<TransferItem> items,
        string destinationPath,
        TransferOptions options,
        IProgress<(TransferItem item, long bytesTransferred)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
            throw new ArgumentException("Nenhum arquivo para transferir", nameof(items));
        
        if (string.IsNullOrEmpty(destinationPath))
            throw new ArgumentException("Caminho de destino não definido", nameof(destinationPath));
        
        // Cria diretório de destino se não existir
        if (!Directory.Exists(destinationPath))
        {
            Directory.CreateDirectory(destinationPath);
        }
        
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(options.MaxParallelTransfers, options.MaxParallelTransfers);
        
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await semaphore.WaitAsync(cancellationToken);
            
            var task = Task.Run(async () =>
            {
                try
                {
                    await TransferSingleFileAsync(item, destinationPath, options, progress, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);
            
            tasks.Add(task);
        }
        
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            _logService.LogWarning("Transferência cancelada", "TransferService");
            throw;
        }
        catch (Exception ex)
        {
            _logService.LogError("Erro durante transferência", "TransferService", ex);
            throw;
        }
    }
    
    private async Task TransferSingleFileAsync(
        TransferItem item,
        string destinationPath,
        TransferOptions options,
        IProgress<(TransferItem item, long bytesTransferred)>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            // Verifica conflitos
            var destPath = Path.Combine(destinationPath, Path.GetFileName(item.SourcePath));
            item.DestinationPath = destPath;
            
            if (File.Exists(destPath))
            {
                var resolution = await ResolveConflictAsync(item, destPath, options.ConflictResolution);
                if (resolution == ConflictResolution.Ignore)
                {
                    item.Status = TransferStatus.Skipped;
                    _logService.LogInfo($"Arquivo ignorado: {item.SourcePath}", "Transfer");
                    return;
                }
                else if (resolution == ConflictResolution.Rename)
                {
                    destPath = GetUniqueFilename(destPath, options.ConflictResolution.RenameSuffix);
                    item.DestinationPath = destPath;
                }
            }
            
            // Notifica início
            item.Status = TransferStatus.InProgress;
            item.StartTime = DateTime.UtcNow;
            TransferStarted?.Invoke(this, item);
            
            // Transfere o arquivo
            await CopyFileAsync(item.SourcePath, destPath, item, progress, cancellationToken);
            
            // Preserva metadados se necessário
            if (options.PreserveMetadata)
            {
                await _metadataService.PreserveAllMetadataAsync(item.SourcePath, destPath, cancellationToken);
                
                // Verifica se metadados foram preservados
                if (options.VerifyTransfer)
                {
                    var verified = await _metadataService.VerifyMetadataPreservationAsync(item.SourcePath, destPath, cancellationToken);
                    if (!verified)
                    {
                        _logService.LogWarning($"Falha ao verificar metadados: {item.SourcePath}", "Metadata");
                    }
                }
            }
            
            // Notifica conclusão
            item.Status = TransferStatus.Completed;
            item.EndTime = DateTime.UtcNow;
            TransferCompleted?.Invoke(this, item);
        }
        catch (OperationCanceledException)
        {
            item.Status = TransferStatus.Failed;
            item.ErrorMessage = "Transferência cancelada";
            TransferFailed?.Invoke(this, (item, new OperationCanceledException()));
            throw;
        }
        catch (Exception ex)
        {
            item.Status = TransferStatus.Failed;
            item.ErrorMessage = ex.Message;
            item.EndTime = DateTime.UtcNow;
            TransferFailed?.Invoke(this, (item, ex));
            throw;
        }
    }
    
    private async Task CopyFileAsync(
        string sourcePath,
        string destPath,
        TransferItem item,
        IProgress<(TransferItem item, long bytesTransferred)>? progress,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 81920; // 80KB
        var buffer = new byte[bufferSize];
        
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        
        long totalTransferred = 0;
        int bytesRead;
        
        while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await destStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            
            totalTransferred += bytesRead;
            item.TransferredBytes = totalTransferred;
            
            progress?.Report((item, totalTransferred));
            TransferProgressChanged?.Invoke(this, (item, totalTransferred));
        }
    }
    
    private async Task<ConflictResolution> ResolveConflictAsync(TransferItem item, string destPath, ConflictResolutionSettings settings)
    {
        if (settings.DefaultResolution != ConflictResolution.Ask)
        {
            return settings.DefaultResolution;
        }
        
        // Implementação simplificada - na versão completa, mostraria um diálogo
        // para o usuário escolher a resolução
        _logService.LogInfo($"Conflito detectado: {item.SourcePath} já existe no destino", "Conflict");
        
        // Padrão: sobrescrever se mais recente
        return ConflictResolution.OverwriteIfOlder;
    }
    
    private string GetUniqueFilename(string path, string suffix)
    {
        var directory = Path.GetDirectoryName(path);
        var filename = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        
        var counter = 1;
        string newPath;
        do
        {
            newPath = Path.Combine(directory ?? string.Empty, $"{filename}{suffix}{counter}{extension}");
            counter++;
        } while (File.Exists(newPath));
        
        return newPath;
    }
}

/// <summary>
/// Opções para transferência de arquivos
/// </summary>
public class TransferOptions
{
    public TransferDirection Direction { get; set; }
    public bool PreserveMetadata { get; set; } = true;
    public bool VerifyTransfer { get; set; } = true;
    public ConflictResolutionSettings ConflictResolution { get; set; } = new();
    public int MaxParallelTransfers { get; set; } = 3;
    public CancellationToken CancellationToken { get; set; }
}