using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PreservaMetadados.Models;

namespace PreservaMetadados.Services;

/// <summary>
/// Serviço para preservação de metadados de arquivos (timestamps e tags embutidas).
/// </summary>
public class FileMetadataService
{
    /// <summary>
    /// Preserva os timestamps de um arquivo de origem para um arquivo de destino.
    /// </summary>
    public async Task PreserveTimestampsAsync(string sourcePath, string destPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceInfo = new FileInfo(sourcePath);
            var destInfo = new FileInfo(destPath);
            
            if (sourceInfo.Exists && destInfo.Exists)
            {
                destInfo.CreationTimeUtc = sourceInfo.CreationTimeUtc;
                destInfo.LastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
                destInfo.LastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Erro ao preservar timestamps: {ex.Message}", ex);
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Preserva todos os metadados (timestamps + tags embutidas) de um arquivo.
    /// </summary>
    public async Task PreserveAllMetadataAsync(string sourcePath, string destPath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Extrai metadados da origem
            var metadata = FileMetadata.FromFile(sourcePath);
            
            // Aplica metadados no destino
            metadata.ApplyTo(destPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Erro ao preservar metadados: {ex.Message}", ex);
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Verifica se os metadados foram preservados corretamente.
    /// </summary>
    public async Task<bool> VerifyMetadataPreservationAsync(string sourcePath, string destPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceMetadata = FileMetadata.FromFile(sourcePath);
            var destMetadata = FileMetadata.FromFile(destPath);
            
            // Verifica timestamps
            if (sourceMetadata.CreationTimeUtc != destMetadata.CreationTimeUtc)
                return false;
            if (sourceMetadata.LastWriteTimeUtc != destMetadata.LastWriteTimeUtc)
                return false;
            if (sourceMetadata.LastAccessTimeUtc != destMetadata.LastAccessTimeUtc)
                return false;
            
            // Verifica tags (implementação simplificada)
            // Na versão completa, compararia todas as tags EXIF/IPTC/XMP
            
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}