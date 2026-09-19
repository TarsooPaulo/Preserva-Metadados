using PreservaMetadados;
using PreservaMetadados.Models;
using PreservaMetadados.Services;
using System.IO;
using Xunit;

namespace PreservaMetadados.Tests;

public class FileTransferServiceTests
{
    [Fact]
    public void CombineMtpPath_CombinesPathsWithBackslashesCorrectly()
    {
        var result1 = FileTransferService.CombineMtpPath(@"\Cartão SD\DCIM", "Foto.jpg");
        Assert.Equal(@"\Cartão SD\DCIM\Foto.jpg", result1);

        var result2 = FileTransferService.CombineMtpPath(@"\Armazenamento Interno\", @"\Download\documento.pdf");
        Assert.Equal(@"\Armazenamento Interno\Download\documento.pdf", result2);

        var result3 = FileTransferService.CombineMtpPath("", "Pasta");
        Assert.Equal("Pasta", result3);

        var result4 = FileTransferService.CombineMtpPath(@"\Cartão SD\Music\Snaptube Audio", "VAI LENTA (Super Slowed)(MP3_320K).mp3");
        Assert.Equal(@"\Cartão SD\Music\Snaptube Audio\VAI LENTA (Super Slowed)(MP3_320K).mp3", result4);
    }

    [Fact]
    public void GetMtpParentDirectory_ReturnsCorrectParentDirectory()
    {
        var parent1 = FileTransferService.GetMtpParentDirectory(@"\Cartão SD\DCIM\Camera\Foto.jpg");
        Assert.Equal(@"\Cartão SD\DCIM\Camera", parent1);

        var parent2 = FileTransferService.GetMtpParentDirectory(@"\Cartão SD\Foto.jpg");
        Assert.Equal(@"\Cartão SD", parent2);

        var parent3 = FileTransferService.GetMtpParentDirectory(@"\Foto.jpg");
        Assert.Equal(@"\", parent3);

        var parent4 = FileTransferService.GetMtpParentDirectory(@"\");
        Assert.Equal(@"\", parent4);

        var parent5 = FileTransferService.GetMtpParentDirectory(@"\Cartão SD\Music\Snaptube Audio\VAI LENTA (Super Slowed)(MP3_320K).mp3");
        Assert.Equal(@"\Cartão SD\Music\Snaptube Audio", parent5);
    }

    [Fact]
    public async Task TransferItemsAsync_ExistingFile_OverwriteOption_OverwritesFileContentAndMetadata()
    {
        var tempSourceDir = Path.Combine(Path.GetTempPath(), "PreservaTest_Src_" + Guid.NewGuid().ToString("N"));
        var tempDestDir = Path.Combine(Path.GetTempPath(), "PreservaTest_Dest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSourceDir);
        Directory.CreateDirectory(tempDestDir);

        try
        {
            var fileName = "teste_conflito.txt";
            var srcFilePath = Path.Combine(tempSourceDir, fileName);
            var destFilePath = Path.Combine(tempDestDir, fileName);

            File.WriteAllText(srcFilePath, "Conteudo Novo da Origem");
            File.WriteAllText(destFilePath, "Conteudo Antigo do Destino");

            var originalCreatedUtc = new DateTime(2022, 5, 10, 12, 0, 0, DateTimeKind.Utc);
            var originalWriteUtc = new DateTime(2023, 8, 15, 14, 30, 0, DateTimeKind.Utc);
            File.SetCreationTimeUtc(srcFilePath, originalCreatedUtc);
            File.SetLastWriteTimeUtc(srcFilePath, originalWriteUtc);

            var service = new FileTransferService();
            var items = new List<FileItem>
            {
                new FileItem
                {
                    Name = fileName,
                    FullPath = srcFilePath,
                    IsDirectory = false,
                    Length = new FileInfo(srcFilePath).Length,
                    CreationTimeUtc = originalCreatedUtc,
                    LastWriteTimeUtc = originalWriteUtc,
                    Extension = ".txt"
                }
            };

            var progress = new Progress<TransferProgressInfo>();
            bool resolverCalled = false;

            await service.TransferItemsAsync(
                items,
                tempDestDir,
                destIsMtp: false,
                destMtpDeviceId: null,
                progress: progress,
                cancellationToken: CancellationToken.None,
                conflictResolver: conflict =>
                {
                    resolverCalled = true;
                    Assert.Equal(fileName, conflict.ItemName);
                    return Task.FromResult(new ConflictResolutionResult
                    {
                        Resolution = ConflictResolution.Overwrite,
                        ApplyToAll = false
                    });
                }
            );

            Assert.True(resolverCalled);
            Assert.Equal("Conteudo Novo da Origem", File.ReadAllText(destFilePath));
            Assert.Equal(originalCreatedUtc, File.GetCreationTimeUtc(destFilePath));
            Assert.Equal(originalWriteUtc, File.GetLastWriteTimeUtc(destFilePath));
        }
        finally
        {
            if (Directory.Exists(tempSourceDir)) Directory.Delete(tempSourceDir, true);
            if (Directory.Exists(tempDestDir)) Directory.Delete(tempDestDir, true);
        }
    }

    [Fact]
    public async Task TransferItemsAsync_ExistingFile_SkipOption_SkipsTransfer()
    {
        var tempSourceDir = Path.Combine(Path.GetTempPath(), "PreservaTest_Src_" + Guid.NewGuid().ToString("N"));
        var tempDestDir = Path.Combine(Path.GetTempPath(), "PreservaTest_Dest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSourceDir);
        Directory.CreateDirectory(tempDestDir);

        try
        {
            var fileName = "teste_pular.txt";
            var srcFilePath = Path.Combine(tempSourceDir, fileName);
            var destFilePath = Path.Combine(tempDestDir, fileName);

            File.WriteAllText(srcFilePath, "Conteudo Origem Nao Copiado");
            File.WriteAllText(destFilePath, "Conteudo Destino Intacto");

            var service = new FileTransferService();
            var items = new List<FileItem>
            {
                new FileItem
                {
                    Name = fileName,
                    FullPath = srcFilePath,
                    IsDirectory = false,
                    Length = new FileInfo(srcFilePath).Length,
                    Extension = ".txt"
                }
            };

            var progress = new Progress<TransferProgressInfo>();

            await service.TransferItemsAsync(
                items,
                tempDestDir,
                destIsMtp: false,
                destMtpDeviceId: null,
                progress: progress,
                cancellationToken: CancellationToken.None,
                conflictResolver: conflict => Task.FromResult(new ConflictResolutionResult
                {
                    Resolution = ConflictResolution.Skip,
                    ApplyToAll = false
                })
            );

            Assert.Equal("Conteudo Destino Intacto", File.ReadAllText(destFilePath));
        }
        finally
        {
            if (Directory.Exists(tempSourceDir)) Directory.Delete(tempSourceDir, true);
            if (Directory.Exists(tempDestDir)) Directory.Delete(tempDestDir, true);
        }
    }

    [Fact]
    public async Task TransferItemsAsync_ExistingFile_CancelOption_ThrowsOperationCanceledException()
    {
        var tempSourceDir = Path.Combine(Path.GetTempPath(), "PreservaTest_Src_" + Guid.NewGuid().ToString("N"));
        var tempDestDir = Path.Combine(Path.GetTempPath(), "PreservaTest_Dest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSourceDir);
        Directory.CreateDirectory(tempDestDir);

        try
        {
            var fileName = "teste_cancelar.txt";
            var srcFilePath = Path.Combine(tempSourceDir, fileName);
            var destFilePath = Path.Combine(tempDestDir, fileName);

            File.WriteAllText(srcFilePath, "Conteudo Novissimo");
            File.WriteAllText(destFilePath, "Conteudo Antigo");

            var service = new FileTransferService();
            var items = new List<FileItem>
            {
                new FileItem
                {
                    Name = fileName,
                    FullPath = srcFilePath,
                    IsDirectory = false,
                    Length = new FileInfo(srcFilePath).Length,
                    Extension = ".txt"
                }
            };

            var progress = new Progress<TransferProgressInfo>();

            await Assert.ThrowsAsync<OperationCanceledException>(() => service.TransferItemsAsync(
                items,
                tempDestDir,
                destIsMtp: false,
                destMtpDeviceId: null,
                progress: progress,
                cancellationToken: CancellationToken.None,
                conflictResolver: conflict => Task.FromResult(new ConflictResolutionResult
                {
                    Resolution = ConflictResolution.Cancel,
                    ApplyToAll = false
                })
            ));

            Assert.Equal("Conteudo Antigo", File.ReadAllText(destFilePath));
        }
        finally
        {
            if (Directory.Exists(tempSourceDir)) Directory.Delete(tempSourceDir, true);
            if (Directory.Exists(tempDestDir)) Directory.Delete(tempDestDir, true);
        }
    }
}
