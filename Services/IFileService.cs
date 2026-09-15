using PreservaMetadados.Models;

namespace PreservaMetadados.Services;

public interface IFileService
{
    Task<List<DeviceItem>> GetDevicesAsync();
    IAsyncEnumerable<FileItem> GetItemsAsync(string path, string? mtpDeviceId = null, CancellationToken cancellationToken = default);
    IDisposable? CreateWatcher(string path, Action onDirectoryChanged);
}
