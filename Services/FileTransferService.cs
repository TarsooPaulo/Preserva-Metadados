using MediaDevices;
using PreservaMetadados.Models;
using System.Diagnostics;
using System.IO;

namespace PreservaMetadados.Services;

public class FileTransferService
{
    private const int BufferSize = 1024 * 1024; // 1 MB buffer para máxima performance

    public static string CombineMtpPath(string basePath, string relativePath)
    {
        return PathSecurityHelper.NormalizeAndSanitizeMtpPath(basePath, relativePath);
    }

    public static string GetMtpParentDirectory(string mtpPath)
    {
        var sanitized = PathSecurityHelper.NormalizeAndSanitizeMtpPath(mtpPath, string.Empty);
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized == @"\")
            return @"\";

        var trimmed = sanitized.TrimEnd('\\');
        var lastSlash = trimmed.LastIndexOf('\\');

        if (lastSlash <= 0)
            return @"\";

        return trimmed.Substring(0, lastSlash);
    }

    public async Task TransferItemsAsync(
        IReadOnlyList<FileItem> items,
        string destinationDirectory,
        bool destIsMtp,
        string? destMtpDeviceId,
        IProgress<TransferProgressInfo> progress,
        CancellationToken cancellationToken,
        Func<FileConflictInfo, Task<ConflictResolutionResult>>? conflictResolver = null,
        Action<FileItem, bool>? onItemTransferred = null)
    {
        // Sanitizar caminho de destino
        destinationDirectory = destIsMtp
            ? PathSecurityHelper.NormalizeAndSanitizeMtpPath(destinationDirectory, string.Empty)
            : PathSecurityHelper.GetSanitizedLocalPath(destinationDirectory);

        var progressInfo = new TransferProgressInfo
        {
            IsTransferring = true,
            StatusMessage = "Calculando total de arquivos e tamanho..."
        };
        progress.Report(progressInfo);

        bool aplicarParaTodos = false;
        ConflictResolution acaoGlobal = ConflictResolution.Skip;

        async Task<ConflictResolution> ResolveConflictAsync(string itemName, string destPath, bool isDir)
        {
            if (aplicarParaTodos)
            {
                return acaoGlobal;
            }

            if (conflictResolver == null)
            {
                return ConflictResolution.Overwrite;
            }

            var conflictInfo = new FileConflictInfo
            {
                ItemName = itemName,
                DestinationPath = destPath,
                IsDirectory = isDir
            };

            var result = await conflictResolver(conflictInfo);
            if (result.ApplyToAll)
            {
                aplicarParaTodos = true;
                acaoGlobal = result.Resolution;
            }

            return result.Resolution;
        }

        // 1. Checagem prévia de existência nos itens selecionados no topo
        var itemsToTransfer = new List<FileItem>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string targetPath = destIsMtp
                ? PathSecurityHelper.NormalizeAndSanitizeMtpPath(destinationDirectory, item.Name)
                : PathSecurityHelper.GetSanitizedLocalPath(Path.Combine(destinationDirectory, item.Name), destinationDirectory);

            bool exists = false;

            if (destIsMtp && !string.IsNullOrEmpty(destMtpDeviceId))
            {
                exists = await Task.Run(() =>
                {
                    var devices = MediaDeviceManager.Instance?.GetDevices();
                    var device = devices?.FirstOrDefault(d => d.DeviceId == destMtpDeviceId);
                    if (device == null) return false;
                    using (device)
                    {
                        device.Connect();
                        bool ex = item.IsDirectory ? device.DirectoryExists(targetPath) : device.FileExists(targetPath);
                        device.Disconnect();
                        return ex;
                    }
                }, cancellationToken);
            }
            else
            {
                exists = item.IsDirectory ? Directory.Exists(targetPath) : File.Exists(targetPath);
            }

            if (exists)
            {
                var res = await ResolveConflictAsync(item.Name, targetPath, item.IsDirectory);
                if (res == ConflictResolution.Cancel)
                {
                    progressInfo.IsTransferring = false;
                    progressInfo.StatusMessage = "Transferência cancelada pelo usuário.";
                    progress.Report(progressInfo);
                    throw new OperationCanceledException("Transferência cancelada pelo usuário.");
                }
                else if (res == ConflictResolution.Skip)
                {
                    continue;
                }
            }

            itemsToTransfer.Add(item);
        }

        if (itemsToTransfer.Count == 0)
        {
            progressInfo.IsTransferring = false;
            progressInfo.StatusMessage = "Nenhum arquivo para transferir.";
            progress.Report(progressInfo);
            return;
        }

        // 2. Mapear todos os arquivos a serem copiados (incluindo subpastas)
        var fileEntries = new List<TransferQueueItem>();
        long totalBytes = 0;

        await Task.Run(() =>
        {
            foreach (var item in itemsToTransfer)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.IsDirectory)
                {
                    CollectDirectoryItems(item, destinationDirectory, fileEntries, ref totalBytes, cancellationToken);
                }
                else
                {
                    string destFile = destIsMtp
                        ? PathSecurityHelper.NormalizeAndSanitizeMtpPath(destinationDirectory, item.Name)
                        : PathSecurityHelper.GetSanitizedLocalPath(Path.Combine(destinationDirectory, item.Name), destinationDirectory);

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

        // 3. Executar cópia com medição de velocidade e preservação de datas
        var stopwatch = Stopwatch.StartNew();
        long totalBytesCopied = 0;
        long lastSpeedCalcBytes = 0;
        var speedStopwatch = Stopwatch.StartNew();
        double currentSpeed = 0;

        for (int i = 0; i < fileEntries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = fileEntries[i];

            // Checagem prévia no nível de arquivo individual
            bool destExists = false;
            if (destIsMtp && !string.IsNullOrEmpty(destMtpDeviceId))
            {
                destExists = await Task.Run(() =>
                {
                    var devices = MediaDeviceManager.Instance?.GetDevices();
                    var device = devices?.FirstOrDefault(d => d.DeviceId == destMtpDeviceId);
                    if (device == null) return false;
                    using (device)
                    {
                        device.Connect();
                        bool ex = device.FileExists(entry.DestinationPath);
                        device.Disconnect();
                        return ex;
                    }
                }, cancellationToken);
            }
            else
            {
                destExists = File.Exists(entry.DestinationPath);
            }

            if (destExists)
            {
                var res = await ResolveConflictAsync(entry.SourceItem.Name, entry.DestinationPath, false);
                if (res == ConflictResolution.Cancel)
                {
                    progressInfo.IsTransferring = false;
                    progressInfo.StatusMessage = "Transferência cancelada pelo usuário.";
                    progress.Report(progressInfo);
                    throw new OperationCanceledException("Transferência cancelada pelo usuário.");
                }
                else if (res == ConflictResolution.Skip)
                {
                    totalBytesCopied += entry.Length;
                    progressInfo.BytesTransferred = totalBytesCopied;
                    progressInfo.StatusMessage = $"Pulado: {entry.SourceItem.Name}";
                    progressInfo.OverallPercentage = totalBytes > 0 ? ((double)totalBytesCopied / totalBytes) * 100 : 100;
                    progress.Report(progressInfo);
                    continue;
                }
            }

            progressInfo.CurrentFileName = entry.SourceItem.Name;
            progressInfo.CurrentFileIndex = i + 1;
            progressInfo.CurrentFileTotalBytes = entry.Length;
            progressInfo.CurrentFileBytes = 0;
            progressInfo.FilePercentage = 0;
            progressInfo.StatusMessage = $"Copiando: {entry.SourceItem.Name} ({i + 1}/{fileEntries.Count})";
            progress.Report(progressInfo);

            if (entry.SourceItem.IsMtp && destIsMtp)
            {
                // MTP -> MTP
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
                // PC Local -> PC Local
                await CopyLocalToLocalWithMetadataAsync(entry, (bytesChunk) =>
                {
                    totalBytesCopied += bytesChunk;
                    progressInfo.BytesTransferred = totalBytesCopied;
                    progressInfo.CurrentFileBytes += bytesChunk;

                    UpdateMetrics(progressInfo, speedStopwatch, ref lastSpeedCalcBytes, ref currentSpeed, totalBytesCopied, totalBytes);
                    progress.Report(progressInfo);
                }, cancellationToken);
            }

            // Notifica atualização incremental do painel de destino
            if (onItemTransferred != null)
            {
                FileItem transferredItem;

                if (destIsMtp)
                {
                    transferredItem = new FileItem
                    {
                        Name = Path.GetFileName(entry.DestinationPath),
                        FullPath = entry.DestinationPath,
                        IsDirectory = false,
                        Length = entry.Length,
                        LastWriteTimeUtc = entry.SourceItem.LastWriteTimeUtc,
                        CreationTimeUtc = entry.SourceItem.CreationTimeUtc,
                        Extension = Path.GetExtension(entry.DestinationPath),
                        IsMtp = true,
                        MtpDeviceId = destMtpDeviceId ?? string.Empty
                    };
                }
                else
                {
                    var relPath = Path.GetRelativePath(destinationDirectory, entry.DestinationPath);
                    var pathParts = relPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

                    if (pathParts.Length > 1)
                    {
                        var topLevelName = pathParts[0];
                        var topLevelPath = PathSecurityHelper.GetSanitizedLocalPath(Path.Combine(destinationDirectory, topLevelName), destinationDirectory);
                        transferredItem = new FileItem
                        {
                            Name = topLevelName,
                            FullPath = topLevelPath,
                            IsDirectory = true,
                            Length = 0,
                            LastWriteTimeUtc = entry.SourceItem.LastWriteTimeUtc ?? DateTime.UtcNow,
                            CreationTimeUtc = entry.SourceItem.CreationTimeUtc ?? DateTime.UtcNow,
                            Extension = string.Empty,
                            IsMtp = false,
                            MtpDeviceId = string.Empty
                        };
                    }
                    else
                    {
                        DateTime? lastWrite = entry.SourceItem.LastWriteTimeUtc;
                        DateTime? creation = entry.SourceItem.CreationTimeUtc;
                        long len = entry.Length;

                        if (File.Exists(entry.DestinationPath))
                        {
                            try
                            {
                                var fi = new FileInfo(entry.DestinationPath);
                                len = fi.Length;
                                lastWrite = fi.LastWriteTimeUtc;
                                creation = fi.CreationTimeUtc;
                            }
                            catch { }
                        }

                        transferredItem = new FileItem
                        {
                            Name = Path.GetFileName(entry.DestinationPath),
                            FullPath = entry.DestinationPath,
                            IsDirectory = false,
                            Length = len,
                            LastWriteTimeUtc = lastWrite,
                            CreationTimeUtc = creation,
                            Extension = Path.GetExtension(entry.DestinationPath),
                            IsMtp = false,
                            MtpDeviceId = string.Empty
                        };
                    }
                }

                onItemTransferred(transferredItem, destExists);
            }

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
        var sourcePath = PathSecurityHelper.GetSanitizedLocalPath(entry.SourceItem.FullPath);
        var destPath = PathSecurityHelper.GetSanitizedLocalPath(entry.DestinationPath);

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var sourceInfo = new FileInfo(sourcePath);
        DateTime creationTimeUtc = DateTime.UtcNow;
        DateTime lastWriteTimeUtc = DateTime.UtcNow;
        DateTime lastAccessTimeUtc = DateTime.UtcNow;

        try
        {
            creationTimeUtc = sourceInfo.CreationTimeUtc;
            lastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
            lastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;
        }
        catch { }

        byte[] buffer = new byte[BufferSize];

        try
        {
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
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PathTooLongException)
        {
            throw new IOException($"Falha ao transferir arquivo '{Path.GetFileName(sourcePath)}': {ex.Message}", ex);
        }

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
        var destPath = PathSecurityHelper.GetSanitizedLocalPath(entry.DestinationPath);
        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var item = entry.SourceItem;
        var deviceId = item.MtpDeviceId;
        var sourceMtpPath = PathSecurityHelper.NormalizeAndSanitizeMtpPath(item.FullPath, string.Empty);

        await Task.Run(() =>
        {
            var devices = MediaDeviceManager.Instance?.GetDevices();
            var device = devices?.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device == null)
                throw new InvalidOperationException($"Dispositivo MTP com ID '{deviceId}' não foi encontrado.");

            using (device)
            {
                device.Connect();

                try
                {
                    using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                    {
                        device.DownloadFile(sourceMtpPath, destStream);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new IOException($"Falha ao baixar arquivo do MTP '{Path.GetFileName(sourceMtpPath)}': {ex.Message}", ex);
                }

                device.Disconnect();
            }

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

                var sourcePath = PathSecurityHelper.GetSanitizedLocalPath(entry.SourceItem.FullPath);
                var destPath = PathSecurityHelper.NormalizeAndSanitizeMtpPath(entry.DestinationPath, string.Empty);
                var destDir = GetMtpParentDirectory(destPath);

                if (!string.IsNullOrEmpty(destDir) && destDir != @"\" && !device.DirectoryExists(destDir))
                {
                    device.CreateDirectory(destDir);
                }

                if (device.FileExists(destPath))
                {
                    device.DeleteFile(destPath);
                }

                try
                {
                    device.UploadFile(sourcePath, destPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new IOException($"Falha ao enviar arquivo para o MTP '{Path.GetFileName(destPath)}': {ex.Message}", ex);
                }

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
        var tempFilePath = PathSecurityHelper.GetSanitizedLocalPath(Path.Combine(Path.GetTempPath(), $"preserva_mtp_{Guid.NewGuid():N}.tmp"));

        try
        {
            var sourceDeviceId = entry.SourceItem.MtpDeviceId;
            var sourceMtpPath = PathSecurityHelper.NormalizeAndSanitizeMtpPath(entry.SourceItem.FullPath, string.Empty);

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
                        sourceDevice.DownloadFile(sourceMtpPath, tempStream);
                    }

                    sourceDevice.Disconnect();
                }
            }, cancellationToken);

            try
            {
                if (entry.SourceItem.CreationTimeUtc.HasValue)
                    File.SetCreationTimeUtc(tempFilePath, entry.SourceItem.CreationTimeUtc.Value);
                if (entry.SourceItem.LastWriteTimeUtc.HasValue)
                    File.SetLastWriteTimeUtc(tempFilePath, entry.SourceItem.LastWriteTimeUtc.Value);
            }
            catch { }

            await Task.Run(() =>
            {
                var devices = MediaDeviceManager.Instance?.GetDevices();
                var destDevice = devices?.FirstOrDefault(d => d.DeviceId == destMtpDeviceId);
                if (destDevice == null)
                    throw new InvalidOperationException($"Dispositivo MTP de destino com ID '{destMtpDeviceId}' não foi encontrado.");

                using (destDevice)
                {
                    destDevice.Connect();

                    var destPath = PathSecurityHelper.NormalizeAndSanitizeMtpPath(entry.DestinationPath, string.Empty);
                    var destDir = GetMtpParentDirectory(destPath);

                    if (!string.IsNullOrEmpty(destDir) && destDir != @"\" && !destDevice.DirectoryExists(destDir))
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
            CollectMtpDirectoryItems(dirItem, baseDestDir, queue, ref totalBytes, cancellationToken);
            return;
        }

        var sourceRoot = PathSecurityHelper.GetSanitizedLocalPath(dirItem.FullPath);
        var dirInfo = new DirectoryInfo(sourceRoot);
        if (!dirInfo.Exists) return;

        // Se o próprio diretório for um Reparse Point / Symlink / Junction, não recursar
        if (PathSecurityHelper.IsReparsePoint(dirInfo))
        {
            return;
        }

        var targetDir = PathSecurityHelper.GetSanitizedLocalPath(Path.Combine(baseDestDir, dirItem.Name), baseDestDir);

        EnumerateDirectoryRecursive(dirInfo, sourceRoot, targetDir, queue, ref totalBytes, cancellationToken);
    }

    private void EnumerateDirectoryRecursive(
        DirectoryInfo currentDir,
        string sourceRoot,
        string targetBaseDir,
        List<TransferQueueItem> queue,
        ref long totalBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Evita seguir links simbólicos ou junções durante varredura recursiva
        if (PathSecurityHelper.IsReparsePoint(currentDir))
        {
            return;
        }

        IEnumerable<FileInfo> files;
        try
        {
            files = currentDir.EnumerateFiles();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is DirectoryNotFoundException || ex is IOException)
        {
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (PathSecurityHelper.IsReparsePoint(file))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceRoot, file.FullName);
            string destPath = PathSecurityHelper.GetSanitizedLocalPath(Path.Combine(targetBaseDir, relativePath), targetBaseDir);

            var item = new FileItem
            {
                Name = file.Name,
                FullPath = PathSecurityHelper.GetSanitizedLocalPath(file.FullName),
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

        IEnumerable<DirectoryInfo> subDirs;
        try
        {
            subDirs = currentDir.EnumerateDirectories();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is DirectoryNotFoundException || ex is IOException)
        {
            return;
        }

        foreach (var subDir in subDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnumerateDirectoryRecursive(subDir, sourceRoot, targetBaseDir, queue, ref totalBytes, cancellationToken);
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
            var targetDir = PathSecurityHelper.NormalizeAndSanitizeMtpPath(baseDestDir, dirItem.Name);
            var sourceMtpDir = PathSecurityHelper.NormalizeAndSanitizeMtpPath(dirItem.FullPath, string.Empty);
            CollectMtpRecursive(device, sourceMtpDir, sourceMtpDir, targetDir, dirItem.MtpDeviceId, queue, ref totalBytes, cancellationToken);
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

        string[]? files = null;
        try
        {
            files = device.GetFiles(currentMtpDir);
        }
        catch { }

        if (files != null)
        {
            foreach (var f in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sanitizedFile = PathSecurityHelper.NormalizeAndSanitizeMtpPath(f, string.Empty);
                var relPath = sanitizedFile.Length >= rootMtpDir.Length
                    ? sanitizedFile.Substring(rootMtpDir.Length).TrimStart('\\', '/')
                    : Path.GetFileName(sanitizedFile);

                var destPath = PathSecurityHelper.GetSanitizedLocalPath(Path.Combine(targetLocalBase, relPath), targetLocalBase);

                long len = 0;
                DateTime? cTime = null;
                DateTime? wTime = null;
                try
                {
                    var fi = device.GetFileInfo(sanitizedFile);
                    len = (long)fi.Length;
                    cTime = fi.CreationTime?.ToUniversalTime();
                    wTime = fi.LastWriteTime?.ToUniversalTime();
                }
                catch { }

                var item = new FileItem
                {
                    Name = Path.GetFileName(sanitizedFile),
                    FullPath = sanitizedFile,
                    IsDirectory = false,
                    Length = len,
                    CreationTimeUtc = cTime,
                    LastWriteTimeUtc = wTime,
                    Extension = Path.GetExtension(sanitizedFile),
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

        string[]? dirs = null;
        try
        {
            dirs = device.GetDirectories(currentMtpDir);
        }
        catch { }

        if (dirs != null)
        {
            foreach (var d in dirs)
            {
                var sanitizedSubDir = PathSecurityHelper.NormalizeAndSanitizeMtpPath(d, string.Empty);
                CollectMtpRecursive(device, sanitizedSubDir, rootMtpDir, targetLocalBase, deviceId, queue, ref totalBytes, cancellationToken);
            }
        }
    }

    public sealed class TransferQueueItem
    {
        public required FileItem SourceItem { get; set; }
        public required string DestinationPath { get; set; }
        public long Length { get; set; }
    }
}
