using System;
using System.Collections.Generic;
using System.IO;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;

namespace PreservaMetadados.Models;

/// <summary>
/// Representa os metadados de um arquivo, incluindo timestamps e tags embutidas.
/// </summary>
public class FileMetadata
{
    public DateTime CreationTimeUtc { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public DateTime LastAccessTimeUtc { get; set; }
    public long FileSize { get; set; }
    public string? FileExtension { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    
    // Metadados EXIF/IPTC/XMP
    public Dictionary<string, string> ExifTags { get; set; } = new();
    public Dictionary<string, string> IptcTags { get; set; } = new();
    public Dictionary<string, string> XmpTags { get; set; } = new();
    
    // Informações adicionais
    public string? MimeType { get; set; }
    public string? Description { get; set; }
    public string? Copyright { get; set; }
    public string[]? Keywords { get; set; }
    
    /// <summary>
    /// Cria uma instância de FileMetadata a partir de um arquivo no sistema de arquivos.
    /// </summary>
    public static FileMetadata FromFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var metadata = new FileMetadata
        {
            CreationTimeUtc = fileInfo.CreationTimeUtc,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            LastAccessTimeUtc = fileInfo.LastAccessTimeUtc,
            FileSize = fileInfo.Length,
            FileExtension = fileInfo.Extension,
            FileName = fileInfo.Name,
            FilePath = filePath
        };
        
        // Tenta extrair metadados detalhados usando MetadataExtractor
        try
        {
            if (IsMediaFile(fileInfo.Extension) && fileInfo.Exists)
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                foreach (var directory in directories)
                {
                    if (directory is ExifDirectoryBase)
                    {
                        foreach (var tag in directory.Tags)
                        {
                            metadata.ExifTags[tag.Name] = tag.Description ?? string.Empty;
                        }
                    }
                    else if (directory is IptcDirectory)
                    {
                        foreach (var tag in directory.Tags)
                        {
                            metadata.IptcTags[tag.Name] = tag.Description ?? string.Empty;
                        }
                    }
                    else if (directory is XmpDirectory)
                    {
                        foreach (var tag in directory.Tags)
                        {
                            metadata.XmpTags[tag.Name] = tag.Description ?? string.Empty;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao extrair metadados com MetadataExtractor: {ex.Message}");
        }

        // Tenta extrair metadados de mídia usando TagLib
        try
        {
            if (IsMediaFile(fileInfo.Extension) && fileInfo.Exists)
            {
                using var tagFile = TagLib.File.Create(filePath);
                if (tagFile?.Tag != null)
                {
                    metadata.Description ??= tagFile.Tag.Comment;
                    metadata.Copyright ??= tagFile.Tag.Copyright;
                    metadata.Keywords ??= tagFile.Tag.Genres;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao extrair metadados com TagLib: {ex.Message}");
        }
        
        return metadata;
    }
    
    /// <summary>
    /// Aplica os metadados a um arquivo de destino, preservando timestamps e tags.
    /// </summary>
    public void ApplyTo(string destFilePath)
    {
        // Aplica timestamps do sistema de arquivos
        var destInfo = new FileInfo(destFilePath);
        if (destInfo.Exists)
        {
            destInfo.CreationTimeUtc = CreationTimeUtc;
            destInfo.LastWriteTimeUtc = LastWriteTimeUtc;
            destInfo.LastAccessTimeUtc = LastAccessTimeUtc;
        }
        
        // Aplica metadados embutidos se for arquivo de mídia
        try
        {
            if (IsMediaFile(FileExtension) && (!string.IsNullOrEmpty(Description) || !string.IsNullOrEmpty(Copyright) || Keywords != null))
            {
                using var tagFile = TagLib.File.Create(destFilePath);
                if (tagFile?.Tag != null)
                {
                    if (!string.IsNullOrEmpty(Description))
                        tagFile.Tag.Comment = Description;
                    if (!string.IsNullOrEmpty(Copyright))
                        tagFile.Tag.Copyright = Copyright;
                    if (Keywords != null && Keywords.Length > 0)
                        tagFile.Tag.Genres = Keywords;
                    
                    tagFile.Save();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao aplicar metadados: {ex.Message}");
        }
    }
    
    private static bool IsMediaFile(string? extension)
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
    /// Verifica se os metadados estão válidos e completos.
    /// </summary>
    public bool IsValid()
    {
        return CreationTimeUtc != default && 
               LastWriteTimeUtc != default && 
               LastAccessTimeUtc != default;
    }
    
    /// <summary>
    /// Cria uma cópia profunda dos metadados.
    /// </summary>
    public FileMetadata Clone()
    {
        return new FileMetadata
        {
            CreationTimeUtc = CreationTimeUtc,
            LastWriteTimeUtc = LastWriteTimeUtc,
            LastAccessTimeUtc = LastAccessTimeUtc,
            FileSize = FileSize,
            FileExtension = FileExtension,
            FileName = FileName,
            FilePath = FilePath,
            ExifTags = new Dictionary<string, string>(ExifTags),
            IptcTags = new Dictionary<string, string>(IptcTags),
            XmpTags = new Dictionary<string, string>(XmpTags),
            MimeType = MimeType,
            Description = Description,
            Copyright = Copyright,
            Keywords = Keywords != null ? (string[])Keywords.Clone() : null
        };
    }
}