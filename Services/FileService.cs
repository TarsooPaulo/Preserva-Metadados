using MediaDevices;
using PreservaMetadados.Models;
using System.IO;
using System.Runtime.CompilerServices;

namespace PreservaMetadados.Services;

public class FileService : IFileService
{
    public Task<List<DeviceItem>> GetDevicesAsync()
    {
        return Task.Run(() =>
        {
            var devices = new List<DeviceItem>();

            // 1. Discos locais, pendrives e drives de rede
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        var devType = drive.DriveType switch
                        {
                            DriveType.Removable => DeviceItemType.RemovableDrive,
                            DriveType.Network => DeviceItemType.NetworkDrive,
                            _ => DeviceItemType.FixedDrive
                        };

                        var totalSize = drive.IsReady ? drive.TotalSize : 0;
                        var freeSpace = drive.IsReady ? drive.AvailableFreeSpace : 0;
                        var label = drive.IsReady ? drive.VolumeLabel : string.Empty;

                        devices.Add(new DeviceItem
                        {
                            Name = drive.Name,
                            Path = PathSecurityHelper.GetSanitizedLocalPath(drive.RootDirectory.FullName),
                            VolumeLabel = label,
                            DeviceType = devType,
                            TotalSize = totalSize,
                            FreeSpace = freeSpace
                        });
                    }
                    catch { }
                }
            }
            catch { }

            // 2. Dispositivos MTP (Celulares / Câmeras)
            try
            {
                var mtpDevices = MediaDeviceManager.Instance?.GetDevices() ?? Enumerable.Empty<MediaDevice>();
                foreach (var dev in mtpDevices)
                {
                    try
                    {
                        devices.Add(new DeviceItem
                        {
                            Name = dev.FriendlyName ?? dev.Description ?? "Dispositivo Móvel",
                            Path = @"\",
                            VolumeLabel = "MTP / Celular",
                            DeviceType = DeviceItemType.MtpDevice,
                            MtpDeviceId = dev.DeviceId
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return devices;
        });
    }

    public async IAsyncEnumerable<FileItem> GetItemsAsync(
        string path,
        string? mtpDeviceId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(mtpDeviceId))
        {
            var sanitizedMtpPath = PathSecurityHelper.NormalizeAndSanitizeMtpPath(path, string.Empty);
            foreach (var item in GetMtpItems(mtpDeviceId, sanitizedMtpPath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            yield break;
        }

        string sanitizedLocalPath;
        try
        {
            sanitizedLocalPath = PathSecurityHelper.GetSanitizedLocalPath(path);
        }
        catch
        {
            yield break;
        }

        if (!Directory.Exists(sanitizedLocalPath))
            yield break;

        var dirInfo = new DirectoryInfo(sanitizedLocalPath);

        IEnumerable<DirectoryInfo> subDirs;
        try
        {
            subDirs = dirInfo.EnumerateDirectories();
        }
        catch
        {
            subDirs = Enumerable.Empty<DirectoryInfo>();
        }

        foreach (var subDir in subDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileItem? item = null;
            try
            {
                if ((subDir.Attributes & FileAttributes.System) != 0 && (subDir.Attributes & FileAttributes.Hidden) != 0)
                    continue;

                item = new FileItem
                {
                    Name = subDir.Name,
                    FullPath = PathSecurityHelper.GetSanitizedLocalPath(subDir.FullName),
                    IsDirectory = true,
                    Length = 0,
                    LastWriteTimeUtc = subDir.LastWriteTimeUtc,
                    CreationTimeUtc = subDir.CreationTimeUtc,
                    Extension = string.Empty
                };
            }
            catch { }

            if (item != null)
                yield return item;
        }

        IEnumerable<FileInfo> files;
        try
        {
            files = dirInfo.EnumerateFiles();
        }
        catch
        {
            files = Enumerable.Empty<FileInfo>();
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileItem? item = null;
            try
            {
                item = new FileItem
                {
                    Name = file.Name,
                    FullPath = PathSecurityHelper.GetSanitizedLocalPath(file.FullName),
                    IsDirectory = false,
                    Length = file.Length,
                    LastWriteTimeUtc = file.LastWriteTimeUtc,
                    CreationTimeUtc = file.CreationTimeUtc,
                    Extension = file.Extension
                };
            }
            catch { }

            if (item != null)
                yield return item;
        }
    }

    private IEnumerable<FileItem> GetMtpItems(string mtpDeviceId, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = MediaDeviceManager.Instance?.GetDevices();
        if (devices == null) yield break;
        var device = devices.FirstOrDefault(d => d.DeviceId == mtpDeviceId);
        if (device == null) yield break;

        using (device)
        {
            device.Connect();

            var currentPath = PathSecurityHelper.NormalizeAndSanitizeMtpPath(path, string.Empty);

            cancellationToken.ThrowIfCancellationRequested();
            string[]? subDirs = null;
            try
            {
                subDirs = device.GetDirectories(currentPath);
            }
            catch { }

            if (subDirs != null)
            {
                foreach (var subDir in subDirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sanitizedSubDir = PathSecurityHelper.NormalizeAndSanitizeMtpPath(subDir, string.Empty);
                    var dirName = Path.GetFileName(sanitizedSubDir.TrimEnd('\\'));
                    if (string.IsNullOrEmpty(dirName)) dirName = sanitizedSubDir;

                    DateTime? lastWrite = null;
                    DateTime? creation = null;
                    try
                    {
                        var info = device.GetDirectoryInfo(sanitizedSubDir);
                        lastWrite = info.LastWriteTime?.ToUniversalTime();
                        creation = info.CreationTime?.ToUniversalTime();
                    }
                    catch { }

                    yield return new FileItem
                    {
                        Name = dirName,
                        FullPath = sanitizedSubDir,
                        IsDirectory = true,
                        Length = 0,
                        LastWriteTimeUtc = lastWrite,
                        CreationTimeUtc = creation,
                        Extension = string.Empty,
                        IsMtp = true,
                        MtpDeviceId = mtpDeviceId
                    };
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            string[]? files = null;
            try
            {
                files = device.GetFiles(currentPath);
            }
            catch { }

            if (files != null)
            {
                foreach (var filePath in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sanitizedFilePath = PathSecurityHelper.NormalizeAndSanitizeMtpPath(filePath, string.Empty);
                    var fileName = Path.GetFileName(sanitizedFilePath);
                    long length = 0;
                    DateTime? lastWrite = null;
                    DateTime? creation = null;

                    try
                    {
                        var info = device.GetFileInfo(sanitizedFilePath);
                        length = (long)info.Length;
                        lastWrite = info.LastWriteTime?.ToUniversalTime();
                        creation = info.CreationTime?.ToUniversalTime();
                    }
                    catch { }

                    yield return new FileItem
                    {
                        Name = fileName,
                        FullPath = sanitizedFilePath,
                        IsDirectory = false,
                        Length = length,
                        LastWriteTimeUtc = lastWrite,
                        CreationTimeUtc = creation,
                        Extension = Path.GetExtension(fileName),
                        IsMtp = true,
                        MtpDeviceId = mtpDeviceId
                    };
                }
            }

            try
            {
                device.Disconnect();
            }
            catch { }
        }
    }

    public IDisposable? CreateWatcher(string path, Action onDirectoryChanged)
    {

        if (string.IsNullOrWhiteSpace(path))
            return null;

        string sanitizedPath;
        try
        {
            sanitizedPath = PathSecurityHelper.GetSanitizedLocalPath(path);
            if (!Directory.Exists(sanitizedPath))
                return null;
        }
        catch
        {
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(sanitizedPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            var timer = new System.Timers.Timer(400) { AutoReset = false };
            timer.Elapsed += (_, _) =>
            {
                try { onDirectoryChanged(); } catch { }
            };

            void TriggerChange(object sender, FileSystemEventArgs e)
            {
                timer.Stop();
                timer.Start();
            }

            void TriggerRename(object sender, RenamedEventArgs e)
            {
                timer.Stop();
                timer.Start();
            }

            watcher.Created += TriggerChange;
            watcher.Deleted += TriggerChange;
            watcher.Changed += TriggerChange;
            watcher.Renamed += TriggerRename;

            return new WatcherSubscription(watcher, timer);
        }
        catch
        {
            return null;
        }
    }

    public Task DeleteItemAsync(FileItem item, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.IsMtp)
            {
                var mtpPath = PathSecurityHelper.NormalizeAndSanitizeMtpPath(item.FullPath, string.Empty);
                var devices = MediaDeviceManager.Instance?.GetDevices();
                var device = devices?.FirstOrDefault(d => d.DeviceId == item.MtpDeviceId);
                if (device == null)
                    throw new InvalidOperationException($"Dispositivo MTP com ID '{item.MtpDeviceId}' não foi encontrado.");

                using (device)
                {
                    device.Connect();
                    if (item.IsDirectory)
                    {
                        if (device.DirectoryExists(mtpPath))
                        {
                            device.DeleteDirectory(mtpPath, true);
                        }
                    }
                    else
                    {
                        if (device.FileExists(mtpPath))
                        {
                            device.DeleteFile(mtpPath);
                        }
                    }
                    device.Disconnect();
                }
                return;
            }

            string sanitizedPath = PathSecurityHelper.GetSanitizedLocalPath(item.FullPath);

            if (item.IsDirectory)
            {
                // Proteção contra Symlinks e Junction Points durante exclusão:
                // Se o diretório for um ReparsePoint, remover apenas a junção/link simbólico sem recursar no destino original!
                if (PathSecurityHelper.IsReparsePoint(sanitizedPath))
                {
                    try
                    {
                        Directory.Delete(sanitizedPath, false);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        throw new IOException($"Não foi possível remover o link simbólico/junção '{Path.GetFileName(sanitizedPath)}': {ex.Message}", ex);
                    }
                    return;
                }

                if (Directory.Exists(sanitizedPath))
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            sanitizedPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is NotSupportedException)
                    {
                        try
                        {
                            Directory.Delete(sanitizedPath, true);
                        }
                        catch (Exception innerEx) when (innerEx is IOException || innerEx is UnauthorizedAccessException)
                        {
                            throw new IOException($"Falha ao excluir o diretório '{Path.GetFileName(sanitizedPath)}': {innerEx.Message}", innerEx);
                        }
                    }
                }
            }
            else
            {
                if (File.Exists(sanitizedPath))
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            sanitizedPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is NotSupportedException)
                    {
                        try
                        {
                            File.Delete(sanitizedPath);
                        }
                        catch (Exception innerEx) when (innerEx is IOException || innerEx is UnauthorizedAccessException)
                        {
                            throw new IOException($"Falha ao excluir o arquivo '{Path.GetFileName(sanitizedPath)}': {innerEx.Message}", innerEx);
                        }
                    }
                }
            }
        }, cancellationToken);
    }

    private sealed class WatcherSubscription : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly System.Timers.Timer _timer;

        public WatcherSubscription(FileSystemWatcher watcher, System.Timers.Timer timer)
        {
            _watcher = watcher;
            _timer = timer;
        }

        public void Dispose()
        {
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _timer.Dispose();
            }
            catch { }
        }
    }
}
