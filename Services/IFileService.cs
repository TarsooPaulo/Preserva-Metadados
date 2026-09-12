using PreservaMetadados.Models;

namespace PreservaMetadados.Services;

public interface IFileService
{
    Task<List<DeviceItem>> GetDevicesAsync();
    Task<List<FileItem>> GetItemsAsync(string path, string? mtpDeviceId = null);
    IDisposable? CreateWatcher(string path, Action onDirectoryChanged);
}
