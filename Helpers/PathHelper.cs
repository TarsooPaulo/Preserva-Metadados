using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PreservaMetadados.Helpers;

/// <summary>
/// Utilitários para manipulação de caminhos de arquivo.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Normaliza um caminho de arquivo, removendo caracteres inválidos.
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        
        // Remove caracteres inválidos
        var invalidChars = Path.GetInvalidPathChars();
        var normalized = path;
        
        foreach (var c in invalidChars)
        {
            normalized = normalized.Replace(c.ToString(), string.Empty);
        }
        
        // Normaliza separadores de diretório
        normalized = normalized.Replace('\\', Path.DirectorySeparatorChar);
        normalized = normalized.Replace('/', Path.DirectorySeparatorChar);
        
        // Remove diretórios pai relativos
        normalized = normalized.Replace("..", string.Empty);
        
        return normalized;
    }
    
    /// <summary>
    /// Verifica se um caminho é válido.
    /// </summary>
    public static bool IsValidPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        
        try
        {
            // Tenta criar um FileInfo para verificar a validade
            var _ = new FileInfo(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Cria um nome de arquivo único adicionando sufixo.
    /// </summary>
    public static string GetUniqueFilename(string path, string suffix = "_copy")
    {
        var directory = Path.GetDirectoryName(path);
        var filename = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        
        var counter = 1;
        string newPath;
        do
        {
            newPath = Path.Combine(directory ?? string.Empty, $"{filename}{suffix}{counter}{extension}");
            counter++;
        } while (File.Exists(newPath));
        
        return newPath;
    }
    
    /// <summary>
    /// Combina caminhos de forma segura.
    /// </summary>
    public static string CombinePaths(params string[] paths)
    {
        if (paths == null || paths.Length == 0)
            return string.Empty;
        
        var combined = paths[0];
        
        for (int i = 1; i < paths.Length; i++)
        {
            combined = Path.Combine(combined, paths[i]);
        }
        
        return NormalizePath(combined);
    }
    
    /// <summary>
    /// Verifica se um caminho é absoluto.
    /// </summary>
    public static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        
        return Path.IsPathRooted(path);
    }
    
    /// <summary>
    /// Converte um caminho relativo em absoluto.
    /// </summary>
    public static string GetAbsolutePath(string basePath, string relativePath)
    {
        if (IsAbsolutePath(relativePath))
            return relativePath;
        
        return Path.GetFullPath(CombinePaths(basePath, relativePath));
    }
    
    /// <summary>
    /// Verifica se um caminho está dentro de outro.
    /// </summary>
    public static bool IsPathWithinPath(string path, string basePath)
    {
        var fullBasePath = Path.GetFullPath(basePath);
        var fullPath = Path.GetFullPath(path);
        
        return fullPath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase);
    }
}