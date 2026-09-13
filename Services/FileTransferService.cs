using MediaDevices;
using PreservaMetadados.Models;
using System.Diagnostics;
using System.IO;

namespace PreservaMetadados.Services;

public class FileTransferService
{
    private const int BufferSize = 1024 * 1024; // 1 MB buffer para máxima performance

    public async Task TransferItemsAsync(
        IReadOnlyList<FileItem> items,
        string destinationDirectory,
        bool destIsMtp,
        string? destMtpDeviceId,
        IProgress<TransferProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        var progressInfo = new TransferProgressInfo
        {
            IsTransferring = true,
            StatusMessage = "Calculando total de arquivos e tamanho..."
        };
        progress.Report(progressInfo);

        // 1. Mapear todos os arquivos a serem copiados (incluindo subpastas)
        var fileEntries = new List<TransferQueueItem>();
        long totalBytes = 0;

        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.IsDirectory)
                {
                    CollectDirectoryItems(item, destinationDirectory, fileEntries, ref totalBytes, cancellationToken);
                }
                else
                {
                    var destFile = Path.Combine(destinationDirectory, item.Name);
                    fileEntries.Add(new TransferQueueItem
                    {
                        SourceItem = item,
                        DestinationPath = destFile,
                        Length = item.Length
                    });
                    totalBytes += item.Length;
                }
            }
        }, cancellationToken);

        progressInfo.TotalFiles = fileEntries.Count;
        progressInfo.TotalBytes = totalBytes;
        progressInfo.StatusMessage = $"Iniciando transferência de {fileEntries.Count} arquivo(s)...";
        progress.Report(progressInfo);

        if (fileEntries.Count == 0)
        {
            progressInfo.IsTransferring = false;
            progressInfo.StatusMessage = "Nenhum arquivo para transferir.";
            progress.Report(progressInfo);
            return;
        }

        // 2. Executar cópia com medição de velocidade e preservação de datas
        var stopwatch = Stopwatch.StartNew();
        long totalBytesCopied = 0;
        long lastSpeedCalcBytes = 0;
        var speedStopwatch = Stopwatch.StartNew();
        double currentSpeed = 0;

        for (int i = 0; i < fileEntries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = fileEntries[i];
            progressInfo.CurrentFileName = entry.SourceItem.Name;
            progressInfo.CurrentFileIndex = i + 1;
            progressInfo.CurrentFileTotalBytes = entry.Length;
            progressInfo.CurrentFileBytes = 0;
            progressInfo.FilePercentage = 0;
            progressInfo.StatusMessage = $"Copiando: {entry.SourceItem.Name} ({i + 1}/{fileEntries.Count})";
            progress.Report(progressInfo);

            try
            {
                if (entry.SourceItem.IsMtp && destIsMtp)
                {
                    // MTP -> MTP (ex: Armazenamento Interno -> Cartão SD) via Buffer Temporário no PC
                    await CopyMtpToMtpWithBufferAsync(entry, destMtpDeviceId!, (bytesChunk) =>
                    {
                        totalBytesCopied += bytesChunk;
                        progressInfo.BytesTransferred = totalBytesCopied;
                        progressInfo.CurrentFileBytes += bytesChunk;

                        UpdateMetrics(progressInfo, speedStopwatch, ref lastSpeedCalcBytes, ref currentSpeed, totalBytesCopied, totalBytes);
                        progress.Report(progressInfo);
                    }, cancellationToken);
                }
                else if (entry.SourceItem.IsMtp)
                {
                    // MTP -> PC Local
                    await CopyMtpToLocalPathAsync(entry.SourceItem, entry.DestinationPath, (bytesChunk) =>
                    {
                        totalBytesCopied += bytesChunk;
                        progressInfo.BytesTransferred = totalBytesCopied;
                        progressInfo.CurrentFileBytes += bytesChunk;

                        UpdateMetrics(progressInfo, speedStopwatch, ref lastSpeedCalcBytes, ref currentSpeed, totalBytesCopied, totalBytes);
                        progress.Report(progressInfo);
                    }, cancellationToken);
                }
                else if (destIsMtp)
                {
                    // PC Local -> MTP
                    await CopyLocalToMtpAsync(entry, destMtpDeviceId!, cancellationToken);
                    totalBytesCopied += entry.Length;
                    progressInfo.BytesTransferred = totalBytesCopied;
                    progressInfo.CurrentFileBytes = entry.Length;
                    progressInfo.FilePercentage = 100;
                    progress.Report(progressInfo);
                }
                else
                {
                    // PC Local -> PC Local (HD, SSD, Pendrive, Rede)
                    await CopyLocalToLocalWithMetadataAsync(entry, (bytesChunk) =>
                    {
                        totalBytesCopied += bytesChunk;
                        progressInfo.BytesTransferred = totalBytesCopied;
                        progressInfo.CurrentFileBytes += bytesChunk;

                        UpdateMetrics(progressInfo, speedStopwatch, ref lastSpeedCalcBytes, ref currentSpeed, totalBytesCopied, totalBytes);
                        progress.Report(progressInfo);
                    }, cancellationToken);
                }
            }
            catch (UnauthorizedAccessException uex)
            {
                var errMsg = $"Acesso Negado ao transferir '{entry.SourceItem.Name}': {uex.Message}";
                progressInfo.StatusMessage = errMsg;
                progress.Report(progressInfo);
                throw new UnauthorizedAccessException(errMsg, uex);
            }
            catch (DirectoryNotFoundException dex)
            {
                var errMsg = $"Diretório Não Encontrado ao transferir '{entry.SourceItem.Name}': {dex.Message}";
                progressInfo.StatusMessage = errMsg;
                progress.Report(progressInfo);
                throw new DirectoryNotFoundException(errMsg, dex);
            }
            catch (IOException ioex)
            {
                var errMsg = $"Erro de I/O (E/S de Arquivo) ao transferir '{entry.SourceItem.Name}': {ioex.Message}";
                progressInfo.StatusMessage = errMsg;
                progress.Report(progressInfo);
                throw new IOException(errMsg, ioex);
            }
            catch (Exception ex)
            {
                var errMsg = $"Erro na API MTP/Dispositivo ao transferir '{entry.SourceItem.Name}' [{ex.GetType().Name}]: {ex.Message}";
                progressInfo.StatusMessage = errMsg;
                progress.Report(progressInfo);
                throw new InvalidOperationException(errMsg, ex);
            }

            // Atualiza progresso geral após o arquivo concluído
            progressInfo.OverallPercentage = totalBytes > 0 ? ((double)totalBytesCopied / totalBytes) * 100 : 100;
            progress.Report(progressInfo);
        }

        stopwatch.Stop();
        progressInfo.IsTransferring = false;
        progressInfo.StatusMessage = $"Concluído com sucesso! {fileEntries.Count} arquivo(s) transferidos com metadados preservados.";
        progressInfo.OverallPercentage = 100;
        progressInfo.FilePercentage = 100;
        progressInfo.SpeedFormatted = "0 KB/s";
        progressInfo.EtaFormatted = "00:00";
        progress.Report(progressInfo);
    }

    private static void UpdateMetrics(
        TransferProgressInfo info,
        Stopwatch speedWatch,
        ref long lastBytes,
        ref double currentSpeed,
        long currentTotalBytes,
        long grandTotalBytes)
    {
        if (speedWatch.ElapsedMilliseconds >= 350)
        {
            var elapsedSec = speedWatch.Elapsed.TotalSeconds;
            var deltaBytes = currentTotalBytes - lastBytes;
            if (elapsedSec > 0)
            {
                currentSpeed = deltaBytes / elapsedSec;
            }

            speedWatch.Restart();
            lastBytes = currentTotalBytes;

            var remaining = grandTotalBytes - currentTotalBytes;
            info.UpdateSpeedAndEta(currentSpeed, remaining);
        }

        if (info.CurrentFileTotalBytes > 0)
        {
            info.FilePercentage = Math.Min(100.0, ((double)info.CurrentFileBytes / info.CurrentFileTotalBytes) * 100.0);
        }
        else
        {
            info.FilePercentage = 100.0;
        }

        if (grandTotalBytes > 0)
        {
            info.OverallPercentage = Math.Min(100.0, ((double)currentTotalBytes / grandTotalBytes) * 100.0);
        }
    }

    /// <summary>
    /// Transferência MTP -> MTP (ex: Armazenamento Interno -> Cartão SD) em 3 Passos via Buffer Temporário
    /// </summary>
    private async Task CopyMtpToMtpWithBufferAsync(
        TransferQueueItem entry,
        string destMtpDeviceId,
        Action<int> onBytesRead,
        CancellationToken cancellationToken)
    {
        var tempFileName = $"preserva_tmp_{Guid.NewGuid():N}_{entry.SourceItem.Name}";
        var tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);

        try
        {
            // Passo 1: Download do MTP Origem para Pasta Temporária do PC
            await CopyMtpToLocalPathAsync(entry.SourceItem, tempFilePath, onBytesRead, cancellationToken);

            // Passo 2: Upload do Arquivo Temporário para o MTP Destino (Cartão SD ou MTP)
            var destMtpPath = entry.DestinationPath;
            var destMtpDir = Path.GetDirectoryName(destMtpPath) ?? @"\";

            await Task.Run(() =>
            {
                var devices = MediaDeviceManager.Instance?.GetDevices();
                var destDevice = devices?.FirstOrDefault(d => d.DeviceId == destMtpDeviceId);
                if (destDevice == null)
                {
                    throw new InvalidOperationException($"Dispositivo MTP de destino (ID: {destMtpDeviceId}) não encontrado ou desconectado.");
                }

                using (destDevice)
                {
                    destDevice.Connect();

                    // Garantir que a árvore de diretórios de destino exista no dispositivo MTP
                    EnsureMtpDirectoryExists(destDevice, destMtpDir);

                    // Enviar o arquivo temporário para o destino MTP
                    destDevice.UploadFile(tempFilePath, destMtpDir);

                    // Preservar metadados no arquivo MTP final se suportado pela API MediaDevices
                    try
                    {
                        var uploadedMtpFilePath = Path.Combine(destMtpDir, entry.SourceItem.Name);
                        if (destDevice.FileExists(uploadedMtpFilePath))
                        {
                            var fileInfo = destDevice.GetFileInfo(uploadedMtpFilePath);
                            if (entry.SourceItem.LastWriteTimeUtc.HasValue && fileInfo != null)
                            {
                                fileInfo.LastWriteTime = entry.SourceItem.LastWriteTimeUtc.Value.ToLocalTime();
                            }
                            if (entry.SourceItem.CreationTimeUtc.HasValue && fileInfo != null)
                            {
                                fileInfo.CreationTime = entry.SourceItem.CreationTimeUtc.Value.ToLocalTime();
                            }
                        }
                    }
                    catch (Exception metaEx)
                    {
                        Debug.WriteLine($"Aviso ao restaurar timestamps MTP no destino: {metaEx.Message}");
                    }

                    destDevice.Disconnect();
                }
            }, cancellationToken);
        }
        finally
        {
            // Passo 3: Limpeza do arquivo temporário no PC
            if (File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch (Exception delEx)
                {
                    Debug.WriteLine($"Aviso ao excluir arquivo de buffer temporário '{tempFilePath}': {delEx.Message}");
                }
            }
        }
    }

    private async Task CopyMtpToLocalPathAsync(
        FileItem sourceItem,
        string localDestPath,
        Action<int> onBytesRead,
        CancellationToken cancellationToken)
    {
        var localDir = Path.GetDirectoryName(localDestPath);
        if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
        {
            Directory.CreateDirectory(localDir);
        }

        var deviceId = sourceItem.MtpDeviceId;

        await Task.Run(() =>
        {
            var devices = MediaDeviceManager.Instance?.GetDevices();
            var device = devices?.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device == null)
            {
                throw new InvalidOperationException($"Dispositivo MTP de origem (ID: {deviceId}) não encontrado ou desconectado.");
            }

            using (device)
            {
                device.Connect();

                using (var memoryStream = new MemoryStream())
                {
                    device.DownloadFile(sourceItem.FullPath, memoryStream);
                    memoryStream.Position = 0;

                    using (var destStream = new FileStream(localDestPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[BufferSize];
                        int read;
                        while ((read = memoryStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            destStream.Write(buffer, 0, read);
                            onBytesRead(read);
                        }
                    }
                }

                device.Disconnect();
            }

            // Preservar metadados do MTP no arquivo local
            try
            {
                if (sourceItem.CreationTimeUtc.HasValue)
                    File.SetCreationTimeUtc(localDestPath, sourceItem.CreationTimeUtc.Value);
                if (sourceItem.LastWriteTimeUtc.HasValue)
                    File.SetLastWriteTimeUtc(localDestPath, sourceItem.LastWriteTimeUtc.Value);
            }
            catch (Exception metaEx)
            {
                Debug.WriteLine($"Aviso ao aplicar metadados no arquivo local '{localDestPath}': {metaEx.Message}");
            }
        }, cancellationToken);
    }

    private async Task CopyLocalToLocalWithMetadataAsync(
        TransferQueueItem entry,
        Action<int> onBytesRead,
        CancellationToken cancellationToken)
    {
        var sourcePath = entry.SourceItem.FullPath;
        var destPath = entry.DestinationPath;

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // 1. Obter timestamps originais antes da cópia
        var sourceInfo = new FileInfo(sourcePath);
        var creationTimeUtc = sourceInfo.CreationTimeUtc;
        var lastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
        var lastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;

        // 2. Transferência de buffer de alto desempenho
        byte[] buffer = new byte[BufferSize];

        await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
        {
            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                onBytesRead(bytesRead);
            }
            await destStream.FlushAsync(cancellationToken);
        }

        // 3. PRESERVAÇÃO ESTRITA DE METADADOS: Imediatamente após fechamento do stream
        try
        {
            File.SetCreationTimeUtc(destPath, creationTimeUtc);
            File.SetLastWriteTimeUtc(destPath, lastWriteTimeUtc);
            File.SetLastAccessTimeUtc(destPath, lastAccessTimeUtc);
        }
        catch (Exception metaEx)
        {
            Debug.WriteLine($"Aviso ao restaurar metadados em '{destPath}': {metaEx.Message}");
        }
    }

    private async Task CopyLocalToMtpAsync(
        TransferQueueItem entry,
        string mtpDeviceId,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var devices = MediaDeviceManager.Instance?.GetDevices();
            var device = devices?.FirstOrDefault(d => d.DeviceId == mtpDeviceId);
            if (device == null)
            {
                throw new InvalidOperationException($"Dispositivo MTP de destino (ID: {mtpDeviceId}) não encontrado ou desconectado.");
            }

            using (device)
            {
                device.Connect();
                var destDir = Path.GetDirectoryName(entry.DestinationPath) ?? @"\";

                EnsureMtpDirectoryExists(device, destDir);

                device.UploadFile(entry.SourceItem.FullPath, destDir);

                // Preservar metadados no arquivo MTP final se suportado
                try
                {
                    var uploadedMtpFilePath = Path.Combine(destDir, entry.SourceItem.Name);
                    if (device.FileExists(uploadedMtpFilePath))
                    {
                        var fileInfo = device.GetFileInfo(uploadedMtpFilePath);
                        if (entry.SourceItem.LastWriteTimeUtc.HasValue && fileInfo != null)
                        {
                            fileInfo.LastWriteTime = entry.SourceItem.LastWriteTimeUtc.Value.ToLocalTime();
                        }
                        if (entry.SourceItem.CreationTimeUtc.HasValue && fileInfo != null)
                        {
                            fileInfo.CreationTime = entry.SourceItem.CreationTimeUtc.Value.ToLocalTime();
                        }
                    }
                }
                catch (Exception metaEx)
                {
                    Debug.WriteLine($"Aviso ao restaurar timestamps MTP no destino: {metaEx.Message}");
                }

                device.Disconnect();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Garante que o diretório MTP exista, criando todos os níveis pai se necessário.
    /// </summary>
    private static void EnsureMtpDirectoryExists(MediaDevice device, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == @"\" || path == "/")
            return;

        if (device.DirectoryExists(path))
            return;

        var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var current = "";

        foreach (var part in parts)
        {
            current = string.IsNullOrEmpty(current) ? @"\" + part : Path.Combine(current, part);
            if (!device.DirectoryExists(current))
            {
                try
                {
                    device.CreateDirectory(current);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Aviso ao criar pasta MTP '{current}': {ex.Message}");
                }
            }
        }
    }

    private void CollectDirectoryItems(
        FileItem dirItem,
        string baseDestDir,
        List<TransferQueueItem> queue,
        ref long totalBytes,
        CancellationToken cancellationToken)
    {
        if (dirItem.IsMtp)
        {
            // MTP Directory
            CollectMtpDirectoryItems(dirItem, baseDestDir, queue, ref totalBytes, cancellationToken);
            return;
        }

        var sourceRoot = dirItem.FullPath;
        var dirInfo = new DirectoryInfo(sourceRoot);
        if (!dirInfo.Exists) return;

        var targetDir = Path.Combine(baseDestDir, dirItem.Name);

        foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceRoot, file.FullName);
            var destPath = Path.Combine(targetDir, relativePath);

            var item = new FileItem
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false,
                Length = file.Length,
                CreationTimeUtc = file.CreationTimeUtc,
                LastWriteTimeUtc = file.LastWriteTimeUtc,
                Extension = file.Extension
            };

            queue.Add(new TransferQueueItem
            {
                SourceItem = item,
                DestinationPath = destPath,
                Length = file.Length
            });

            totalBytes += file.Length;
        }
    }

    private void CollectMtpDirectoryItems(
        FileItem dirItem,
        string baseDestDir,
        List<TransferQueueItem> queue,
        ref long totalBytes,
        CancellationToken cancellationToken)
    {
        var devices = MediaDeviceManager.Instance?.GetDevices();
        var device = devices?.FirstOrDefault(d => d.DeviceId == dirItem.MtpDeviceId);
        if (device == null) return;

        using (device)
        {
            device.Connect();
            var targetDir = Path.Combine(baseDestDir, dirItem.Name);
            CollectMtpRecursive(device, dirItem.FullPath, dirItem.FullPath, targetDir, dirItem.MtpDeviceId, queue, ref totalBytes, cancellationToken);
            device.Disconnect();
        }
    }

    private void CollectMtpRecursive(
        MediaDevice device,
        string currentMtpDir,
        string rootMtpDir,
        string targetLocalBase,
        string deviceId,
        List<TransferQueueItem> queue,
        ref long totalBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var files = device.GetFiles(currentMtpDir);
        if (files != null)
        {
            foreach (var f in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relPath = f.Substring(rootMtpDir.Length).TrimStart('\\', '/');
                var destPath = Path.Combine(targetLocalBase, relPath);

                long len = 0;
                DateTime? cTime = null;
                DateTime? wTime = null;
                try
                {
                    var fi = device.GetFileInfo(f);
                    len = (long)fi.Length;
                    cTime = fi.CreationTime?.ToUniversalTime();
                    wTime = fi.LastWriteTime?.ToUniversalTime();
                }
                catch { }

                var item = new FileItem
                {
                    Name = Path.GetFileName(f),
                    FullPath = f,
                    IsDirectory = false,
                    Length = len,
                    CreationTimeUtc = cTime,
                    LastWriteTimeUtc = wTime,
                    Extension = Path.GetExtension(f),
                    IsMtp = true,
                    MtpDeviceId = deviceId
                };

                queue.Add(new TransferQueueItem
                {
                    SourceItem = item,
                    DestinationPath = destPath,
                    Length = len
                });

                totalBytes += len;
            }
        }

        var dirs = device.GetDirectories(currentMtpDir);
        if (dirs != null)
        {
            foreach (var d in dirs)
            {
                CollectMtpRecursive(device, d, rootMtpDir, targetLocalBase, deviceId, queue, ref totalBytes, cancellationToken);
            }
        }
    }

    private sealed class TransferQueueItem
    {
        public required FileItem SourceItem { get; set; }
        public required string DestinationPath { get; set; }
        public long Length { get; set; }
    }
}
