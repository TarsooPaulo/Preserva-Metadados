namespace PreservaMetadados.Models;

/// <summary>
/// Direção da transferência de arquivos.
/// </summary>
public enum TransferDirection
{
    /// <summary>
    /// Do PC para o Celular
    /// </summary>
    PcToPhone,
    
    /// <summary>
    /// Do Celular para o PC
    /// </summary>
    PhoneToPc,
    
    /// <summary>
    /// Bidirecional (sincronização)
    /// </summary>
    Bidirectional
}

/// <summary>
/// Modo de conexão com o dispositivo
/// </summary>
public enum ConnectionMode
{
    /// <summary>
    /// USB MTP (Media Transfer Protocol)
    /// </summary>
    UsbMtp,
    
    /// <summary>
    /// Wi-Fi (servidor local)
    /// </summary>
    Wifi
}