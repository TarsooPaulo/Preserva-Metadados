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
                            Path = drive.RootDirectory.FullName,
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
            foreach (var item in GetMtpItems(mtpDeviceId, path, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            yield break;
        }

        if (!Directory.Exists(path))
            yield break;

        var dirInfo = new DirectoryInfo(path);

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
                    FullPath = subDir.FullName,
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
                    FullPath = file.FullName,
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
        var items = new List<FileItem>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var devices = MediaDeviceManager.Instance?.GetDevices();
            if (devices == null) return items;
            var device = devices.FirstOrDefault(d => d.DeviceId == mtpDeviceId);
            if (device == null) return items;

            using (device)
            {
                device.Connect();

                var currentPath = string.IsNullOrWhiteSpace(path) ? @"\" : path;

                cancellationToken.ThrowIfCancellationRequested();
                var subDirs = device.GetDirectories(currentPath);
                if (subDirs != null)
                {
                    foreach (var subDir in subDirs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var dirName = Path.GetFileName(subDir.TrimEnd('\\'));
                        if (string.IsNullOrEmpty(dirName)) dirName = subDir;

                        DateTime? lastWrite = null;
                        DateTime? creation = null;
                        try
                        {
                            var info = device.GetDirectoryInfo(subDir);
                            lastWrite = info.LastWriteTime?.ToUniversalTime();
                            creation = info.CreationTime?.ToUniversalTime();
                        }
                        catch { }

                        items.Add(new FileItem
                        {
                            Name = dirName,
                            FullPath = subDir,
                            IsDirectory = true,
                            Length = 0,
                            LastWriteTimeUtc = lastWrite,
                            CreationTimeUtc = creation,
                            Extension = string.Empty,
                            IsMtp = true,
                            MtpDeviceId = mtpDeviceId
                        });
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                var files = device.GetFiles(currentPath);
                if (files != null)
                {
                    foreach (var filePath in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var fileName = Path.GetFileName(filePath);
                        long length = 0;
                        DateTime? lastWrite = null;
                        DateTime? creation = null;

                        try
                        {
                            var info = device.GetFileInfo(filePath);
                            length = (long)info.Length;
                            lastWrite = info.LastWriteTime?.ToUniversalTime();
                            creation = info.CreationTime?.ToUniversalTime();
                        }
                        catch { }

                        items.Add(new FileItem
                        {
                            Name = fileName,
                            FullPath = filePath,
                            IsDirectory = false,
                            Length = length,
                            LastWriteTimeUtc = lastWrite,
                            CreationTimeUtc = creation,
                            Extension = Path.GetExtension(fileName),
                            IsMtp = true,
                            MtpDeviceId = mtpDeviceId
                        });
                    }
                }

                device.Disconnect();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch { }

        return items;
    }

    public IDisposable? CreateWatcher(string path, Action onDirectoryChanged)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            // Debouncer para não sobrecarregar a interface com múltiplos eventos sucessivos
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
