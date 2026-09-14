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

            if (entry.SourceItem.IsMtp && destIsMtp)
            {
                // MTP -> MTP (ex: Memória Interna do Celular -> Cartão SD no mesmo celular ou entre dispositivos MTP)
                await CopyMtpToMtpAsync(entry, destMtpDeviceId!, (bytesChunk) =>
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
                await CopyMtpToLocalAsync(entry, (bytesChunk) =>
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
                await CopyLocalToMtpAsync(entry, destMtpDeviceId!, (bytesChunk) =>
                {
                    totalBytesCopied += bytesChunk;
                    progressInfo.BytesTransferred = totalBytesCopied;
                    progressInfo.CurrentFileBytes += bytesChunk;

                    UpdateMetrics(progressInfo, speedStopwatch, ref lastSpeedCalcBytes, ref currentSpeed, totalBytesCopied, totalBytes);
                    progress.Report(progressInfo);
                }, cancellationToken);
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
        catch { }
    }

    private async Task CopyMtpToLocalAsync(
        TransferQueueItem entry,
        Action<int> onBytesRead,
        CancellationToken cancellationToken)
    {
        var destPath = entry.DestinationPath;
        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var item = entry.SourceItem;
        var deviceId = item.MtpDeviceId;

        await Task.Run(() =>
        {
            var devices = MediaDeviceManager.Instance?.GetDevices();
            var device = devices?.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device == null)
                throw new InvalidOperationException($"Dispositivo MTP com ID '{deviceId}' não foi encontrado.");

            using (device)
            {
                device.Connect();

                using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                {
                    device.DownloadFile(item.FullPath, destStream);
                }

                device.Disconnect();
            }

            // Preservar metadados do MTP
            try
            {
                if (item.CreationTimeUtc.HasValue)
                    File.SetCreationTimeUtc(destPath, item.CreationTimeUtc.Value);
                if (item.LastWriteTimeUtc.HasValue)
                    File.SetLastWriteTimeUtc(destPath, item.LastWriteTimeUtc.Value);
            }
            catch { }

            onBytesRead((int)entry.Length);
        }, cancellationToken);
    }

    private async Task CopyLocalToMtpAsync(
        TransferQueueItem entry,
        string mtpDeviceId,
        Action<int> onBytesRead,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var devices = MediaDeviceManager.Instance?.GetDevices();
            var device = devices?.FirstOrDefault(d => d.DeviceId == mtpDeviceId);
            if (device == null)
                throw new InvalidOperationException($"Dispositivo MTP com ID '{mtpDeviceId}' não foi encontrado.");

            using (device)
            {
                device.Connect();

                var sourcePath = entry.SourceItem.FullPath;
                var destPath = entry.DestinationPath;
                var destDir = Path.GetDirectoryName(destPath);

                // Garante que a pasta pai exista no dispositivo MTP
                if (!string.IsNullOrEmpty(destDir) && !device.DirectoryExists(destDir))
                {
                    device.CreateDirectory(destDir);
                }

                // Se o arquivo de destino já existir no MTP, remove-o previamente para evitar colisão/exceção
                if (device.FileExists(destPath))
                {
                    device.DeleteFile(destPath);
                }

                // UploadFile requer o caminho do ARQUIVO de destino completo (não apenas o diretório)
                device.UploadFile(sourcePath, destPath);

                device.Disconnect();
            }

            onBytesRead((int)entry.Length);
        }, cancellationToken);
    }

    private async Task CopyMtpToMtpAsync(
        TransferQueueItem entry,
        string destMtpDeviceId,
        Action<int> onBytesRead,
        CancellationToken cancellationToken)
    {
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"preserva_mtp_{Guid.NewGuid():N}.tmp");

        try
        {
            // 1. Baixar da origem MTP para o arquivo temporário local no PC
            var sourceDeviceId = entry.SourceItem.MtpDeviceId;
            await Task.Run(() =>
            {
                var devices = MediaDeviceManager.Instance?.GetDevices();
                var sourceDevice = devices?.FirstOrDefault(d => d.DeviceId == sourceDeviceId);
                if (sourceDevice == null)
                    throw new InvalidOperationException($"Dispositivo MTP de origem com ID '{sourceDeviceId}' não foi encontrado.");

                using (sourceDevice)
                {
                    sourceDevice.Connect();

                    using (var tempStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                    {
                        sourceDevice.DownloadFile(entry.SourceItem.FullPath, tempStream);
                    }

                    sourceDevice.Disconnect();
                }
            }, cancellationToken);

            // Preservar metadados no buffer temporário
            try
            {
                if (entry.SourceItem.CreationTimeUtc.HasValue)
                    File.SetCreationTimeUtc(tempFilePath, entry.SourceItem.CreationTimeUtc.Value);
                if (entry.SourceItem.LastWriteTimeUtc.HasValue)
                    File.SetLastWriteTimeUtc(tempFilePath, entry.SourceItem.LastWriteTimeUtc.Value);
            }
            catch { }

            // 2. Upload do arquivo temporário para o destino MTP
            await Task.Run(() =>
            {
                var devices = MediaDeviceManager.Instance?.GetDevices();
                var destDevice = devices?.FirstOrDefault(d => d.DeviceId == destMtpDeviceId);
                if (destDevice == null)
                    throw new InvalidOperationException($"Dispositivo MTP de destino com ID '{destMtpDeviceId}' não foi encontrado.");

                using (destDevice)
                {
                    destDevice.Connect();

                    var destPath = entry.DestinationPath;
                    var destDir = Path.GetDirectoryName(destPath);

                    if (!string.IsNullOrEmpty(destDir) && !destDevice.DirectoryExists(destDir))
                    {
                        destDevice.CreateDirectory(destDir);
                    }

                    if (destDevice.FileExists(destPath))
                    {
                        destDevice.DeleteFile(destPath);
                    }

                    destDevice.UploadFile(tempFilePath, destPath);

                    destDevice.Disconnect();
                }

                onBytesRead((int)entry.Length);
            }, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch { }
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

    public static string CombineMtpPath(string basePath, string relativePath)
    {
        if (string.IsNullOrEmpty(basePath)) return relativePath.TrimStart('\\');
        if (string.IsNullOrEmpty(relativePath)) return basePath;

        var cleanBase = basePath.TrimEnd('\\');
        var cleanRel = relativePath.TrimStart('\\');

        return $"{cleanBase}\\{cleanRel}";
    }

    public static string GetMtpParentDirectory(string mtpPath)
    {
        if (string.IsNullOrEmpty(mtpPath) || mtpPath == @"\") return @"\";

        var cleanPath = mtpPath.TrimEnd('\\');
        var lastSlash = cleanPath.LastIndexOf('\\');

        if (lastSlash < 0) return @"\";
        if (lastSlash == 0) return @"\";

        return cleanPath.Substring(0, lastSlash);
    }

    public sealed class TransferQueueItem
    {
        public required FileItem SourceItem { get; set; }
        public required string DestinationPath { get; set; }
        public long Length { get; set; }
    }
}
