using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreservaMetadados.Models;
using PreservaMetadados.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace PreservaMetadados.ViewModels;

public partial class FilePaneViewModel : ObservableObject, IDisposable
{
    private readonly IFileService _fileService;
    private readonly Stack<string> _backHistory = new();
    private readonly Stack<string> _forwardHistory = new();
    private IDisposable? _watcherSubscription;
    private List<FileItem> _allItems = new();

    public string PaneTitle { get; }

    public ObservableCollection<DeviceItem> Devices { get; } = new();

    [ObservableProperty]
    private DeviceItem? selectedDevice;

    [ObservableProperty]
    private string currentPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<BreadcrumbItem> breadcrumbs = new();

    [ObservableProperty]
    private ObservableCollection<FileItem> items = new();

    [ObservableProperty]
    private FileItem? selectedItem;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool canGoBack;

    [ObservableProperty]
    private bool canGoForward;

    [ObservableProperty]
    private bool canGoUp;

    [ObservableProperty]
    private string statusSummary = "0 itens";

    [ObservableProperty]
    private bool? isAllSelected = false;

    public FilePaneViewModel(string title, IFileService fileService)
    {
        PaneTitle = title;
        _fileService = fileService;
    }

    public async Task InitializeAsync()
    {
        await LoadDevicesAsync();
    }

    public async Task LoadDevicesAsync()
    {
        var currentDevPath = SelectedDevice?.Path;
        var list = await _fileService.GetDevicesAsync();

        Devices.Clear();
        foreach (var dev in list)
        {
            Devices.Add(dev);
        }

        // Selecionar o primeiro dispositivo se não houver um selecionado
        if (SelectedDevice == null && Devices.Count > 0)
        {
            SelectedDevice = Devices[0];
        }
        else if (currentDevPath != null)
        {
            var matching = Devices.FirstOrDefault(d => d.Path == currentDevPath);
            if (matching != null)
                SelectedDevice = matching;
        }
    }

    partial void OnSelectedDeviceChanged(DeviceItem? value)
    {
        if (value != null)
        {
            _backHistory.Clear();
            _forwardHistory.Clear();
            UpdateHistoryState();
            _ = NavigateToPathAsync(value.Path, recordHistory: false);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterItems();
    }

    public async Task NavigateToPathAsync(string targetPath, bool recordHistory = true)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        if (recordHistory && !string.IsNullOrWhiteSpace(CurrentPath) && CurrentPath != targetPath)
        {
            _backHistory.Push(CurrentPath);
            _forwardHistory.Clear();
            UpdateHistoryState();
        }

        CurrentPath = targetPath;
        UpdateBreadcrumbs(targetPath);
        UpdateCanGoUp(targetPath);

        await LoadItemsAsync();
    }

    [RelayCommand]
    public async Task OpenItemAsync(FileItem? item)
    {
        if (item == null) return;

        if (item.IsDirectory)
        {
            await NavigateToPathAsync(item.FullPath, recordHistory: true);
        }
    }

    [RelayCommand]
    public async Task NavigateToBreadcrumbAsync(BreadcrumbItem? item)
    {
        if (item != null && !string.IsNullOrWhiteSpace(item.FullPath))
        {
            await NavigateToPathAsync(item.FullPath, recordHistory: true);
        }
    }

    [RelayCommand]
    public async Task GoBackAsync()
    {
        if (_backHistory.Count > 0)
        {
            _forwardHistory.Push(CurrentPath);
            var prev = _backHistory.Pop();
            UpdateHistoryState();
            await NavigateToPathAsync(prev, recordHistory: false);
        }
    }

    [RelayCommand]
    public async Task GoForwardAsync()
    {
        if (_forwardHistory.Count > 0)
        {
            _backHistory.Push(CurrentPath);
            var next = _forwardHistory.Pop();
            UpdateHistoryState();
            await NavigateToPathAsync(next, recordHistory: false);
        }
    }

    [RelayCommand]
    public async Task GoUpAsync()
    {
        if (SelectedDevice != null && SelectedDevice.DeviceType == DeviceItemType.MtpDevice)
        {
            if (CurrentPath != @"\" && CurrentPath != "")
            {
                var parent = Path.GetDirectoryName(CurrentPath.TrimEnd('\\')) ?? @"\";
                if (string.IsNullOrEmpty(parent)) parent = @"\";
                await NavigateToPathAsync(parent, recordHistory: true);
            }
            return;
        }

        var dirInfo = Directory.GetParent(CurrentPath);
        if (dirInfo != null)
        {
            await NavigateToPathAsync(dirInfo.FullName, recordHistory: true);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadItemsAsync();
    }

    [RelayCommand]
    public void SelectAll(object? parameter)
    {
        bool select = true;
        if (parameter is bool b)
        {
            select = b;
        }
        else if (parameter is string s && bool.TryParse(s, out var parsed))
        {
            select = parsed;
        }

        foreach (var item in Items)
        {
            item.IsSelected = select;
        }
        UpdateSelectionSummary();
    }

    [RelayCommand]
    public void InvertSelection()
    {
        foreach (var item in Items)
        {
            item.IsSelected = !item.IsSelected;
        }
        UpdateSelectionSummary();
    }

    private async Task LoadItemsAsync()
    {
        IsLoading = true;

        // Limpar watcher anterior
        _watcherSubscription?.Dispose();
        _watcherSubscription = null;

        var path = CurrentPath;
        var mtpDeviceId = SelectedDevice?.DeviceType == DeviceItemType.MtpDevice ? SelectedDevice.MtpDeviceId : null;

        try
        {
            var list = await _fileService.GetItemsAsync(path, mtpDeviceId);

            // Ordenar: Pastas primeiro, depois arquivos alfabeticamente
            _allItems = list.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name).ToList();

            foreach (var item in _allItems)
            {
                item.SelectionChanged += OnItemSelectionChanged;
            }

            FilterItems();

            // Configurar FileSystemWatcher em caminhos locais
            if (string.IsNullOrEmpty(mtpDeviceId) && Directory.Exists(path))
            {
                _watcherSubscription = _fileService.CreateWatcher(path, () =>
                {
                    Application.Current?.Dispatcher?.InvokeAsync(async () =>
                    {
                        if (CurrentPath == path)
                        {
                            await LoadItemsAsync();
                        }
                    });
                });
            }
        }
        catch (Exception ex)
        {
            StatusSummary = $"Erro ao listar: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void FilterItems()
    {
        var query = SearchText?.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        Items.Clear();
        foreach (var it in filtered)
        {
            Items.Add(it);
        }

        UpdateSelectionSummary();
    }

    private void OnItemSelectionChanged()
    {
        UpdateSelectionSummary();
    }

    public void UpdateSelectionSummary()
    {
        var total = Items.Count;
        var selected = Items.Where(i => i.IsSelected).ToList();
        var selectedCount = selected.Count;
        var selectedBytes = selected.Where(i => !i.IsDirectory).Sum(i => i.Length);

        if (selectedCount == 0)
        {
            StatusSummary = $"{total} item(ns)";
            IsAllSelected = false;
        }
        else
        {
            var sizeStr = FileItem.FormatBytes(selectedBytes);
            StatusSummary = $"{total} item(ns) | {selectedCount} selecionado(s) ({sizeStr})";
            IsAllSelected = selectedCount == total ? true : (bool?)null;
        }
    }

    private void UpdateBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        if (string.IsNullOrWhiteSpace(path)) return;

        if (SelectedDevice != null && SelectedDevice.DeviceType == DeviceItemType.MtpDevice)
        {
            Breadcrumbs.Add(new BreadcrumbItem
            {
                Name = SelectedDevice.Name,
                FullPath = @"\",
                IsLast = path == @"\" || path == ""
            });

            if (path != @"\" && path != "")
            {
                var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                var accumulated = @"\";
                for (int i = 0; i < parts.Length; i++)
                {
                    accumulated = Path.Combine(accumulated, parts[i]);
                    Breadcrumbs.Add(new BreadcrumbItem
                    {
                        Name = parts[i],
                        FullPath = accumulated,
                        IsLast = i == parts.Length - 1
                    });
                }
            }
            return;
        }

        try
        {
            var dir = new DirectoryInfo(path);
            var partsList = new List<BreadcrumbItem>();

            var current = dir;
            while (current != null)
            {
                var name = string.IsNullOrEmpty(current.Name) ? current.FullName : current.Name;
                if (current.Parent == null) // Drive root ex: C:\
                    name = current.FullName.TrimEnd('\\');

                partsList.Insert(0, new BreadcrumbItem
                {
                    Name = name,
                    FullPath = current.FullName,
                    IsLast = false
                });

                current = current.Parent;
            }

            if (partsList.Count > 0)
            {
                partsList[^1].IsLast = true;
                foreach (var b in partsList)
                {
                    Breadcrumbs.Add(b);
                }
            }
        }
        catch
        {
            Breadcrumbs.Add(new BreadcrumbItem { Name = path, FullPath = path, IsLast = true });
        }
    }

    private void UpdateCanGoUp(string path)
    {
        if (SelectedDevice != null && SelectedDevice.DeviceType == DeviceItemType.MtpDevice)
        {
            CanGoUp = path != @"\" && path != "";
            return;
        }

        try
        {
            var parent = Directory.GetParent(path);
            CanGoUp = parent != null;
        }
        catch
        {
            CanGoUp = false;
        }
    }

    private void UpdateHistoryState()
    {
        CanGoBack = _backHistory.Count > 0;
        CanGoForward = _forwardHistory.Count > 0;
    }

    public IReadOnlyList<FileItem> GetSelectedOrFocusedItems()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count > 0)
            return selected;

        if (SelectedItem != null)
            return new[] { SelectedItem };

        return Array.Empty<FileItem>();
    }

    public void Dispose()
    {
        _watcherSubscription?.Dispose();
    }
}
