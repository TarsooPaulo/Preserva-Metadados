using MediaDevices;
using PreservaMetadados.Models;
using System.IO;
using System.Timers;

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

    public Task<List<FileItem>> GetItemsAsync(string path, string? mtpDeviceId = null)
    {
        return Task.Run(() =>
        {
            var items = new List<FileItem>();

            if (!string.IsNullOrEmpty(mtpDeviceId))
            {
                return GetMtpItems(mtpDeviceId, path);
            }

            // Sistema de Arquivos Local
            if (!Directory.Exists(path))
                return items;

            try
            {
                var dirInfo = new DirectoryInfo(path);

                // Diretórios
                foreach (var subDir in dirInfo.EnumerateDirectories())
                {
                    try
                    {
                        // Omitir pastas ocultas de sistema irrelevantes se necessário, ou listar normalmente
                        if ((subDir.Attributes & FileAttributes.System) != 0 && (subDir.Attributes & FileAttributes.Hidden) != 0)
                            continue;

                        items.Add(new FileItem
                        {
                            Name = subDir.Name,
                            FullPath = subDir.FullName,
                            IsDirectory = true,
                            Length = 0,
                            LastWriteTimeUtc = subDir.LastWriteTimeUtc,
                            CreationTimeUtc = subDir.CreationTimeUtc,
                            Extension = string.Empty
                        });
                    }
                    catch { }
                }

                // Arquivos
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    try
                    {
                        items.Add(new FileItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsDirectory = false,
                            Length = file.Length,
                            LastWriteTimeUtc = file.LastWriteTimeUtc,
                            CreationTimeUtc = file.CreationTimeUtc,
                            Extension = file.Extension
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return items;
        });
    }

    private List<FileItem> GetMtpItems(string mtpDeviceId, string path)
    {
        var items = new List<FileItem>();
        try
        {
            var devices = MediaDeviceManager.Instance?.GetDevices();
            if (devices == null) return items;
            var device = devices.FirstOrDefault(d => d.DeviceId == mtpDeviceId);
            if (device == null) return items;

            using (device)
            {
                device.Connect();

                var currentPath = string.IsNullOrWhiteSpace(path) ? @"\" : path;

                // Pastas MTP
                var subDirs = device.GetDirectories(currentPath);
                if (subDirs != null)
                {
                    foreach (var subDir in subDirs)
                    {
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

                // Arquivos MTP
                var files = device.GetFiles(currentPath);
                if (files != null)
                {
                    foreach (var filePath in files)
                    {
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

    public Task DeleteItemsAsync(IEnumerable<FileItem> items)
    {
        return Task.Run(() =>
        {
            var mtpGroup = items.Where(i => i.IsMtp && !string.IsNullOrEmpty(i.MtpDeviceId))
                                .GroupBy(i => i.MtpDeviceId!);

            foreach (var group in mtpGroup)
            {
                var mtpDeviceId = group.Key;
                try
                {
                    var devices = MediaDeviceManager.Instance?.GetDevices();
                    var device = devices?.FirstOrDefault(d => d.DeviceId == mtpDeviceId);
                    if (device != null)
                    {
                        using (device)
                        {
                            device.Connect();
                            foreach (var item in group)
                            {
                                try
                                {
                                    if (item.IsDirectory)
                                    {
                                        device.DeleteDirectory(item.FullPath, true);
                                    }
                                    else
                                    {
                                        device.DeleteFile(item.FullPath);
                                    }
                                }
                                catch { }
                            }
                            device.Disconnect();
                        }
                    }
                }
                catch { }
            }

            var localItems = items.Where(i => !i.IsMtp);
            foreach (var item in localItems)
            {
                try
                {
                    if (item.IsDirectory)
                    {
                        if (Directory.Exists(item.FullPath))
                        {
                            try
                            {
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                                    item.FullPath,
                                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }
                            catch
                            {
                                Directory.Delete(item.FullPath, true);
                            }
                        }
                    }
                    else
                    {
                        if (File.Exists(item.FullPath))
                        {
                            try
                            {
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                                    item.FullPath,
                                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }
                            catch
                            {
                                File.Delete(item.FullPath);
                            }
                        }
                    }
                }
                catch { }
            }
        });
    }
}
