using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PreservaMetadados.Models;
using PreservaMetadados.Services;

namespace PreservaMetadados.ViewModels;

/// <summary>
/// ViewModel para a janela de configurações.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly LogService _logService;
    
    [ObservableProperty]
    private bool _isDarkTheme;
    
    [ObservableProperty]
    private ConflictResolutionSettings _conflictSettings = new();
    
    [ObservableProperty]
    private bool _preserveMetadata = true;
    
    [ObservableProperty]
    private bool _verifyTransfer = true;
    
    [ObservableProperty]
    private bool _autoStartTransfer = false;
    
    [ObservableProperty]
    private int _maxParallelTransfers = 3;
    
    [ObservableProperty]
    private string _wifiServerPort = "8080";
    
    [ObservableProperty]
    private string? _wifiServerAddress;
    
    public SettingsViewModel(LogService logService)
    {
        _logService = logService;
        LoadSettings();
    }
    
    private void LoadSettings()
    {
        // Carrega configurações do armazenamento persistente
        // Implementação simplificada - na versão completa, usaria arquivo de configuração
        IsDarkTheme = false;
        MaxParallelTransfers = 3;
        WifiServerPort = "8080";
        
        _logService.LogInfo("Configurações carregadas", "Settings");
    }
    
    [RelayCommand]
    private void SaveSettings()
    {
        // Salva configurações no armazenamento persistente
        // Implementação simplificada - na versão completa, usaria arquivo de configuração
        
        _logService.LogInfo("Configurações salvas", "Settings");
    }
    
    [RelayCommand]
    private void ResetDefaults()
    {
        IsDarkTheme = false;
        ConflictSettings = new ConflictResolutionSettings();
        PreserveMetadata = true;
        VerifyTransfer = true;
        AutoStartTransfer = false;
        MaxParallelTransfers = 3;
        WifiServerPort = "8080";
        WifiServerAddress = null;
        
        _logService.LogInfo("Configurações restauradas para o padrão", "Settings");
    }
    
    [RelayCommand]
    private void TestWifiConnection()
    {
        // Testa conexão com servidor Wi-Fi
        // Implementação simplificada
        _logService.LogInfo($"Testando conexão com servidor Wi-Fi: {WifiServerAddress}:{WifiServerPort}", "WifiTest");
    }
}