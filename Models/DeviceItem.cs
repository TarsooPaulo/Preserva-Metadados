using CommunityToolkit.Mvvm.ComponentModel;

namespace PreservaMetadados.Models;

public enum DeviceItemType
{
    FixedDrive,
    RemovableDrive,
    NetworkDrive,
    MtpDevice
}

public partial class DeviceItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public DeviceItemType DeviceType { get; set; } = DeviceItemType.FixedDrive;
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public string MtpDeviceId { get; set; } = string.Empty;

    public string IconKind => DeviceType switch
    {
        DeviceItemType.FixedDrive => "Harddisk",
        DeviceItemType.RemovableDrive => "UsbFlashDrive",
        DeviceItemType.NetworkDrive => "FolderNetwork",
        DeviceItemType.MtpDevice => "Cellphone",
        _ => "Harddisk"
    };

    public string DisplayName
    {
        get
        {
            if (DeviceType == DeviceItemType.MtpDevice)
                return $"📱 {Name}";

            if (!string.IsNullOrWhiteSpace(VolumeLabel))
                return $"{Name} ({VolumeLabel})";

            return Name;
        }
    }

    public string CapacityInfo
    {
        get
        {
            if (DeviceType == DeviceItemType.MtpDevice || TotalSize <= 0)
                return DeviceType == DeviceItemType.MtpDevice ? "Dispositivo MTP" : string.Empty;

            var freeGb = (double)FreeSpace / (1024 * 1024 * 1024);
            var totalGb = (double)TotalSize / (1024 * 1024 * 1024);
            return $"{freeGb:0.#} GB livres de {totalGb:0.#} GB";
        }
    }

    public override string ToString() => DisplayName;
}
