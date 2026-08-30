using System;
using System.IO;
using System.Security.Cryptography;

namespace PreservaMetadados.Helpers;

/// <summary>
/// Utilitários para operações de arquivo.
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Copia um arquivo de forma assíncrona com callback de progresso.
    /// </summary>
    public static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        const int bufferSize = 81920; // 80KB
        var buffer = new byte[bufferSize];
        
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        
        long totalTransferred = 0;
        int bytesRead;
        
        while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await destinationStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            
            totalTransferred += bytesRead;
            progress?.Report(totalTransferred);
        }
    }
    
    /// <summary>
    /// Calcula o hash MD5 de um arquivo.
    /// </summary>
    public static async Task<string> CalculateMd5Async(string filePath, CancellationToken cancellationToken = default)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        
        var hashBytes = await md5.ComputeHashAsync(stream, cancellationToken);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
    
    /// <summary>
    /// Verifica se dois arquivos são idênticos comparando seus hashes.
    /// </summary>
    public static async Task<bool> FilesAreEqualAsync(string filePath1, string filePath2, CancellationToken cancellationToken = default)
    {
        var hash1 = await CalculateMd5Async(filePath1, cancellationToken);
        var hash2 = await CalculateMd5Async(filePath2, cancellationToken);
        
        return hash1 == hash2;
    }
    
    /// <summary>
    /// Formata o tamanho do arquivo em uma string legível.
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
    
    /// <summary>
    /// Verifica se um arquivo está bloqueado.
    /// </summary>
    public static bool IsFileLocked(string filePath)
    {
        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
    
    /// <summary>
    /// Cria um diretório se não existir.
    /// </summary>
    public static void EnsureDirectoryExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }
}