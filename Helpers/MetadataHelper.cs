using System;
using System.Collections.Generic;
using System.IO;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;

namespace PreservaMetadados.Helpers;

/// <summary>
/// Utilitários para extração e preservação de metadados de mídia.
/// </summary>
public static class MetadataHelper
{
    /// <summary>
    /// Extrai todos os metadados de um arquivo de mídia.
    /// </summary>
    public static Dictionary<string, object> ExtractAllMetadata(string filePath)
    {
        var metadata = new Dictionary<string, object>();
        
        try
        {
            if (!File.Exists(filePath))
                return metadata;
            
            // Informações básicas do arquivo
            var fileInfo = new FileInfo(filePath);
            metadata["FileSize"] = fileInfo.Length;
            metadata["FileExtension"] = fileInfo.Extension;
            metadata["CreationTimeUtc"] = fileInfo.CreationTimeUtc;
            metadata["LastWriteTimeUtc"] = fileInfo.LastWriteTimeUtc;
            metadata["LastAccessTimeUtc"] = fileInfo.LastAccessTimeUtc;
            
            // Tenta extrair metadados usando MetadataExtractor
            if (IsMediaFile(fileInfo.Extension))
            {
                try
                {
                    var directories = ImageMetadataReader.ReadMetadata(filePath);
                    foreach (var directory in directories)
                    {
                        foreach (var tag in directory.Tags)
                        {
                            metadata[$"{directory.Name}:{tag.Name}"] = tag.Description ?? string.Empty;
                        }
                    }
                }
                catch { }

                // Tenta extrair informações gerais via TagLib
                try
                {
                    using var tagFile = TagLib.File.Create(filePath);
                    if (tagFile?.Tag != null)
                    {
                        if (!string.IsNullOrEmpty(tagFile.Tag.Comment))
                            metadata["Description"] = tagFile.Tag.Comment;
                        if (!string.IsNullOrEmpty(tagFile.Tag.Copyright))
                            metadata["Copyright"] = tagFile.Tag.Copyright;
                        if (tagFile.Tag.Genres != null && tagFile.Tag.Genres.Length > 0)
                            metadata["Keywords"] = tagFile.Tag.Genres;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            metadata["Error"] = ex.Message;
        }
        
        return metadata;
    }
    
    /// <summary>
    /// Verifica se um arquivo é de mídia (foto, vídeo, áudio).
    /// </summary>
    public static bool IsMediaFile(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;
        
        var ext = extension.ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".tiff" or ".bmp" or ".gif" => true,
            ".mp4" or ".mov" or ".avi" or ".wmv" or ".flv" or ".mkv" => true,
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => true,
            _ => false
        };
    }
    
    /// <summary>
    /// Preserva metadados de um arquivo de origem para um arquivo de destino.
    /// </summary>
    public static void PreserveMetadata(string sourcePath, string destinationPath)
    {
        try
        {
            // Preserva timestamps
            var sourceInfo = new FileInfo(sourcePath);
            var destInfo = new FileInfo(destinationPath);
            
            if (sourceInfo.Exists && destInfo.Exists)
            {
                destInfo.CreationTimeUtc = sourceInfo.CreationTimeUtc;
                destInfo.LastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
                destInfo.LastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;
            }
            
            // Preserva tags embutidas se for arquivo de mídia
            if (IsMediaFile(sourceInfo.Extension))
            {
                try
                {
                    using var sourceFile = TagLib.File.Create(sourcePath);
                    if (sourceFile?.Tag != null)
                    {
                        using var destFile = TagLib.File.Create(destinationPath);
                        if (destFile?.Tag != null)
                        {
                            if (!string.IsNullOrEmpty(sourceFile.Tag.Comment))
                                destFile.Tag.Comment = sourceFile.Tag.Comment;
                            if (!string.IsNullOrEmpty(sourceFile.Tag.Copyright))
                                destFile.Tag.Copyright = sourceFile.Tag.Copyright;
                            if (sourceFile.Tag.Genres != null && sourceFile.Tag.Genres.Length > 0)
                                destFile.Tag.Genres = sourceFile.Tag.Genres;
                            
                            destFile.Save();
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception)
        {
            // Ignora erros ao preservar metadados
        }
    }
    
    /// <summary>
    /// Verifica se os metadados foram preservados corretamente.
    /// </summary>
    public static bool VerifyMetadataPreservation(string sourcePath, string destinationPath)
    {
        try
        {
            var sourceInfo = new FileInfo(sourcePath);
            var destInfo = new FileInfo(destinationPath);
            
            // Verifica timestamps
            if (sourceInfo.CreationTimeUtc != destInfo.CreationTimeUtc)
                return false;
            if (sourceInfo.LastWriteTimeUtc != destInfo.LastWriteTimeUtc)
                return false;
            if (sourceInfo.LastAccessTimeUtc != destInfo.LastAccessTimeUtc)
                return false;
            
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}