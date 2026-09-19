using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PreservaMetadados.Services;

public static class PathSecurityHelper
{
    /// <summary>
    /// Normaliza e valida um caminho local. Garante que o caminho seja absoluto e resolve sequências como '..'.
    /// Se um diretório base for especificado, garante que o caminho final esteja estritamente dentro desse diretório.
    /// </summary>
    public static string GetSanitizedLocalPath(string path, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("O caminho não pode ser nulo ou vazio.", nameof(path));

        // Obter caminho absoluto resolvido
        string fullPath = Path.GetFullPath(path);

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            string fullBase = Path.GetFullPath(baseDirectory);

            string normalizedBase = fullBase.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase)
                ? fullBase
                : fullBase + Path.DirectorySeparatorChar;

            string normalizedPath = fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath + Path.DirectorySeparatorChar;

            if (!normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fullPath, fullBase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Acesso negado: O caminho '{path}' tenta acessar um diretório fora do escopo permitido.");
            }
        }

        return fullPath;
    }

    /// <summary>
    /// Normaliza e sanitiza caminhos do protocolo MTP prevenindo Path Traversal.
    /// </summary>
    public static string NormalizeAndSanitizeMtpPath(string basePath, string relativePath)
    {
        string rawPath;
        if (string.IsNullOrEmpty(basePath))
        {
            rawPath = relativePath ?? @"\";
        }
        else if (string.IsNullOrEmpty(relativePath))
        {
            rawPath = basePath;
        }
        else
        {
            rawPath = $"{basePath.TrimEnd('\\', '/')}\\{relativePath.TrimStart('\\', '/')}";
        }

        var segments = rawPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var stack = new Stack<string>();

        foreach (var seg in segments)
        {
            if (seg == ".")
                continue;
            if (seg == "..")
            {
                if (stack.Count > 0)
                    stack.Pop();
            }
            else
            {
                var invalidChars = Path.GetInvalidFileNameChars();
                var cleanSeg = string.Concat(seg.Where(c => !invalidChars.Contains(c)));
                if (!string.IsNullOrWhiteSpace(cleanSeg))
                {
                    stack.Push(cleanSeg);
                }
            }
        }

        if (stack.Count == 0)
            return @"\";

        return @"\" + string.Join(@"\", stack.Reverse());
    }

    /// <summary>
    /// Verifica se o item de arquivo/diretório especificado é um Link Simbólico, Junction Point ou Reparse Point.
    /// </summary>
    public static bool IsReparsePoint(FileSystemInfo info)
    {
        if (info == null) return false;
        try
        {
            info.Refresh();
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifica se o caminho especificado é um Link Simbólico, Junction Point ou Reparse Point.
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                return (dirInfo.Attributes & FileAttributes.ReparsePoint) != 0;
            }

            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                return (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0;
            }
        }
        catch { }

        return false;
    }
}
