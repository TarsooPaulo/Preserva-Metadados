using MediaDevices;
using PreservaMetadados.Models;
using System.Diagnostics;
using System.IO;

namespace PreservaMetadados.Services;

public class FileTransferService
{
    private const int BufferSize = 1024 * 1024; // 1 MB buffer para máxima performance

    public static string CombineMtpPath(string path1, string path2)
    {
        if (string.IsNullOrEmpty(path1)) return path2 ?? string.Empty;
        if (string.IsNullOrEmpty(path2)) return path1 ?? string.Empty;

        var p1 = path1.TrimEnd('\\', '/');
        var p2 = path2.TrimStart('\\', '/');

        if (string.IsNullOrEmpty(p1)) return @"\" + p2;
        return $"{p1}\\{p2}";
    }

    public static string GetMtpParentDirectory(string mtpPath)
    {
        if (string.IsNullOrWhiteSpace(mtpPath) || mtpPath == @"\" || mtpPath == "/")
            return @"\";

        var normalized = mtpPath.Replace('/', '\\').TrimEnd('\\');
        var lastSlash = normalized.LastIndexOf('\\');
        if (lastSlash <= 0) return @"\";
        return normalized.Substring(0, lastSlash);
    }

    private static void EnsureMtpDirectoryExists(MediaDevice device, string targetDir)
    {
        if (string.IsNullOrWhiteSpace(targetDir) || targetDir == @"\" || targetDir == "/")
            return;

        if (device.DirectoryExists(targetDir))
            return;

        var normalized = targetDir.Replace('/', '\\').Trim('\\');
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var currentPath = @"\";

        foreach (var part in parts)
        {
            var nextPath = CombineMtpPath(currentPath, part);
            if (!device.DirectoryExists(nextPath))
            {
                try
                {
                    device.CreateDirectory(nextPath);
                }
                catch
                {
                    // Se já existir ou falhar por outra razão, continua
                }
            }
            currentPath = nextPath;
        }
    }

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
                    var destFile = CombineMtpPath(destinationDirectory, item.Name);
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
                    // MTP -> MTP (Armazenamento Interno -> Cartão SD ou entre Dispositivos/Pastas MTP) via Buffer Temporário
                    await CopyMtpToMtpViaTempAsync(entry, destMtpDeviceId!, (bytesChunk) =>
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
            catch (UnauthorizedAccessException ex)
            {
                throw new UnauthorizedAccessException($"Acesso negado ao transferir '{entry.SourceItem.Name}': {ex.Message}", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new DirectoryNotFoundException($"Caminho ou diretório não encontrado para '{entry.SourceItem.Name}': {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new Exception($"Falha ao transferir '{entry.SourceItem.Name}': {ex.Message}", ex);
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
            {
                throw new InvalidOperationException($"Dispositivo MTP de origem não encontrado (ID: {deviceId}).");
            }

            using (device)
            {
                device.Connect();

                using (var memoryStream = new MemoryStream())
                {
                    device.DownloadFile(item.FullPath, memoryStream);
                    memoryStream.Position = 0;

                    using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
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

            // Preservar metadados do MTP
            try
            {
                if (item.CreationTimeUtc.HasValue)
                    File.SetCreationTimeUtc(destPath, item.CreationTimeUtc.Value);
                if (item.LastWriteTimeUtc.HasValue)
                    File.SetLastWriteTimeUtc(destPath, item.LastWriteTimeUtc.Value);
            }
            catch { }
        }, cancellationToken);
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
                throw new InvalidOperationException($"Dispositivo MTP de destino não encontrado (ID: {mtpDeviceId}).");
            }

            using (device)
            {
                device.Connect();
                var destDir = GetMtpParentDirectory(entry.DestinationPath);
                EnsureMtpDirectoryExists(device, destDir);

                device.UploadFile(entry.SourceItem.FullPath, destDir);

                // Tentar restaurar metadados no objeto MTP gravado
                try
                {
                    var uploadedMtpPath = entry.DestinationPath;
                    if (device.FileExists(uploadedMtpPath))
                    {
                        var info = device.GetFileInfo(uploadedMtpPath);
                        if (info != null)
                        {
                            if (entry.SourceItem.CreationTimeUtc.HasValue)
                                info.CreationTime = entry.SourceItem.CreationTimeUtc.Value.ToLocalTime();
                            if (entry.SourceItem.LastWriteTimeUtc.HasValue)
                                info.LastWriteTime = entry.SourceItem.LastWriteTimeUtc.Value.ToLocalTime();
                        }
                    }
                }
                catch { }

                device.Disconnect();
            }
        }, cancellationToken);
    }

    private async Task CopyMtpToMtpViaTempAsync(
        TransferQueueItem entry,
        string destMtpDeviceId,
        Action<int> onBytesRead,
        CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PreservaMetadadosTemp");
        if (!Directory.Exists(tempDir))
        {
            Directory.CreateDirectory(tempDir);
        }

        var tempFilePath = Path.Combine(tempDir, $"temp_{Guid.NewGuid():N}_{entry.SourceItem.Name}");

        try
        {
            // Passo 1: Baixar do MTP de Origem para arquivo temporário do sistema no PC
            var sourceDeviceId = entry.SourceItem.MtpDeviceId;
            await Task.Run(() =>
            {
                var devices = MediaDeviceManager.Instance?.GetDevices();
                var sourceDevice = devices?.FirstOrDefault(d => d.DeviceId == sourceDeviceId);
                if (sourceDevice == null)
                {
                    throw new InvalidOperationException($"Dispositivo MTP de origem não encontrado (ID: {sourceDeviceId}).");
                }

                using (sourceDevice)
                {
                    sourceDevice.Connect();

                    using (var memoryStream = new MemoryStream())
                    {
                        sourceDevice.DownloadFile(entry.SourceItem.FullPath, memoryStream);
                        memoryStream.Position = 0;

                        using (var destStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
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

                    sourceDevice.Disconnect();
                }

                // Preservar datas no arquivo temporário local
                try
                {
                    if (entry.SourceItem.CreationTimeUtc.HasValue)
                        File.SetCreationTimeUtc(tempFilePath, entry.SourceItem.CreationTimeUtc.Value);
                    if (entry.SourceItem.LastWriteTimeUtc.HasValue)
                        File.SetLastWriteTimeUtc(tempFilePath, entry.SourceItem.LastWriteTimeUtc.Value);
                }
                catch { }
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Passo 2: Enviar do arquivo temporário para o Cartão SD / MTP de Destino
            await Task.Run(() =>
            {
                var devices = MediaDeviceManager.Instance?.GetDevices();
                var destDevice = devices?.FirstOrDefault(d => d.DeviceId == destMtpDeviceId);
                if (destDevice == null)
                {
                    throw new InvalidOperationException($"Dispositivo MTP de destino não encontrado (ID: {destMtpDeviceId}).");
                }

                using (destDevice)
                {
                    destDevice.Connect();

                    var destDir = GetMtpParentDirectory(entry.DestinationPath);
                    EnsureMtpDirectoryExists(destDevice, destDir);

                    destDevice.UploadFile(tempFilePath, destDir);

                    // Restauração de Metadados: Garantir CreationTime e LastWriteTime originais no MTP de destino
                    try
                    {
                        var uploadedMtpPath = entry.DestinationPath;
                        if (destDevice.FileExists(uploadedMtpPath))
                        {
                            var info = destDevice.GetFileInfo(uploadedMtpPath);
                            if (info != null)
                            {
                                if (entry.SourceItem.CreationTimeUtc.HasValue)
                                    info.CreationTime = entry.SourceItem.CreationTimeUtc.Value.ToLocalTime();
                                if (entry.SourceItem.LastWriteTimeUtc.HasValue)
                                    info.LastWriteTime = entry.SourceItem.LastWriteTimeUtc.Value.ToLocalTime();
                            }
                        }
                    }
                    catch { }

                    destDevice.Disconnect();
                }
            }, cancellationToken);
        }
        finally
        {
            // Passo 3: Apagar o arquivo temporário do PC
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

        var sourceRoot = dirItem.FullPath;
        var dirInfo = new DirectoryInfo(sourceRoot);
        if (!dirInfo.Exists) return;

        var targetDir = CombineMtpPath(baseDestDir, dirItem.Name);

        foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceRoot, file.FullName);
            var destPath = CombineMtpPath(targetDir, relativePath);

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
            var targetDir = CombineMtpPath(baseDestDir, dirItem.Name);
            CollectMtpRecursive(device, dirItem.FullPath, dirItem.FullPath, targetDir, dirItem.MtpDeviceId, queue, ref totalBytes, cancellationToken);
            device.Disconnect();
        }
    }

    private void CollectMtpRecursive(
        MediaDevice device,
        string currentMtpDir,
        string rootMtpDir,
        string targetBaseDir,
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
                var destPath = CombineMtpPath(targetBaseDir, relPath);

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
                CollectMtpRecursive(device, d, rootMtpDir, targetBaseDir, deviceId, queue, ref totalBytes, cancellationToken);
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
