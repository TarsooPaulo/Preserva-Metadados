using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PreservaMetadados.Models;

namespace PreservaMetadados.Services;

/// <summary>
/// Serviço para transferência de arquivos via Wi-Fi usando servidor local.
/// </summary>
public class WifiTransferService
{
    private readonly LogService _logService;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    
    public WifiTransferService(LogService logService)
    {
        _logService = logService;
    }
    
    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            // Implementação simplificada - verifica se há servidor disponível
            // na rede local
            
            await Task.Delay(500);
            
            // Simulação de detecção de servidor
            _logService.LogInfo("Verificando conexão Wi-Fi", "Wifi");
            
            return false; // Retorna false por padrão
        }
        catch (Exception ex)
        {
            _logService.LogError("Erro ao verificar conexão Wi-Fi", "Wifi", ex);
            return false;
        }
    }
    
    public async Task StartServerAsync(int port = 8080, CancellationToken cancellationToken = default)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isRunning = true;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            _logService.LogInfo($"Servidor Wi-Fi iniciado na porta {port}", "Wifi");
            
            // Aceita conexões em loop
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logService.LogWarning("Servidor Wi-Fi cancelado", "Wifi");
        }
        catch (Exception ex)
        {
            _logService.LogError("Erro ao iniciar servidor Wi-Fi", "Wifi", ex);
            throw;
        }
    }
    
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            
            if (bytesRead > 0)
            {
                var request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _logService.LogInfo($"Requisição recebida: {request}", "Wifi");
                
                // Processa a requisição e envia resposta
                var response = "OK";
                var responseBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(responseBytes, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Erro ao processar cliente", "Wifi", ex);
        }
        finally
        {
            client.Close();
        }
    }
    
    public async Task StopServerAsync()
    {
        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        
        if (_listener != null)
        {
            _listener.Stop();
            _listener = null;
        }
        
        _logService.LogInfo("Servidor Wi-Fi parado", "Wifi");
        await Task.CompletedTask;
    }
    
    public async Task TransferFileAsync(string sourcePath, string ipAddress, int port, string destPath,
        IProgress<(long bytesTransferred, long totalBytes)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ipAddress, port, cancellationToken);
            
            using var stream = client.GetStream();
            using var fileStream = File.OpenRead(sourcePath);
            
            var fileInfo = new FileInfo(sourcePath);
            var totalBytes = fileInfo.Length;
            var buffer = new byte[4096];
            var bytesTransferred = 0;
            
            // Envia informações do arquivo
            var fileInfoJson = $"{{\"name\":\"{Path.GetFileName(sourcePath)}\",\"size\":{totalBytes}}}";
            var infoBytes = Encoding.UTF8.GetBytes(fileInfoJson);
            await stream.WriteAsync(infoBytes, cancellationToken);
            
            // Transfere o conteúdo do arquivo
            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                await stream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                
                bytesTransferred += bytesRead;
                progress?.Report((bytesTransferred, totalBytes));
            }
            
            _logService.LogInfo($"Arquivo transferido via Wi-Fi: {sourcePath} → {ipAddress}:{port}", "Wifi");
        }
        catch (Exception ex)
        {
            _logService.LogError($"Erro ao transferir arquivo via Wi-Fi: {sourcePath}", "Wifi", ex);
            throw;
        }
    }
    
    public async Task<List<string>> DiscoverServersAsync()
    {
        try
        {
            // Implementação simplificada - na versão completa, usaria UDP broadcast
            // para descobrir servidores disponíveis na rede local
            
            await Task.Delay(1000);
            
            // Simulação de lista de servidores
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logService.LogError("Erro ao descobrir servidores", "Wifi", ex);
            throw;
        }
    }
}