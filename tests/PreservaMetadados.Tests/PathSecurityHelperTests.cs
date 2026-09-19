using PreservaMetadados.Services;
using PreservaMetadados.ViewModels;
using System;
using System.IO;
using Xunit;

namespace PreservaMetadados.Tests;

public class PathSecurityHelperTests
{
    [Fact]
    public void GetSanitizedLocalPath_ReturnsFullPathForValidPath()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string sanitized = PathSecurityHelper.GetSanitizedLocalPath(currentDir);

        Assert.NotNull(sanitized);
        Assert.True(Path.IsPathFullyQualified(sanitized));
    }

    [Fact]
    public void GetSanitizedLocalPath_AllowsPathWithinBaseDirectory()
    {
        string baseDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "BaseFolder"));
        string subPath = Path.Combine(baseDir, "SubFolder", "File.txt");

        string result = PathSecurityHelper.GetSanitizedLocalPath(subPath, baseDir);

        Assert.Equal(Path.GetFullPath(subPath), result);
    }

    [Fact]
    public void GetSanitizedLocalPath_ThrowsOnDirectoryTraversalAttempt()
    {
        string baseDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "BaseFolder"));
        string invalidPath = Path.Combine(baseDir, "..", "..", "Windows", "System32", "cmd.exe");

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            PathSecurityHelper.GetSanitizedLocalPath(invalidPath, baseDir);
        });

        Assert.Contains("Acesso negado", ex.Message);
    }

    [Fact]
    public void NormalizeAndSanitizeMtpPath_HandlesNormalAndTraversalPaths()
    {
        string path1 = PathSecurityHelper.NormalizeAndSanitizeMtpPath(@"\Armazenamento", @"DCIM\Camera");
        Assert.Equal(@"\Armazenamento\DCIM\Camera", path1);

        string path2 = PathSecurityHelper.NormalizeAndSanitizeMtpPath(@"\Armazenamento\DCIM", @"..\..\..\Windows\System32");
        Assert.Equal(@"\Windows\System32", path2);

        string path3 = PathSecurityHelper.NormalizeAndSanitizeMtpPath(@"\", @"..\..\..");
        Assert.Equal(@"\", path3);
    }

    [Fact]
    public void SanitizeUserMessage_ReturnsFriendlyMessage()
    {
        var ex1 = new UnauthorizedAccessException("Access denied stacktrace details");
        Assert.Equal("Acesso negado. Permissões insuficientes para concluir a operação.", MainViewModel.SanitizeUserMessage(ex1));

        var ex2 = new DirectoryNotFoundException("Could not find directory C:\\Secret");
        Assert.Equal("O arquivo ou pasta especificado não foi encontrado.", MainViewModel.SanitizeUserMessage(ex2));

        var ex3 = new IOException("Falha ao excluir o arquivo 'teste.txt': Em uso por outro processo");
        Assert.Equal("Falha ao excluir o arquivo 'teste.txt': Em uso por outro processo", MainViewModel.SanitizeUserMessage(ex3));
    }
}
