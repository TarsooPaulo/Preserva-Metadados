using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PreservaMetadados.Models;
using PreservaMetadados.Services;
using Xunit;

namespace PreservaMetadados.Tests;

public class TransferServiceTests
{
    [Fact]
    public async Task TransferItemsAsync_LocalToLocal_PreservesMetadata()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"preserva_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(tempDir, "source");
        var targetDir = Path.Combine(tempDir, "target");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(targetDir);

        try
        {
            var testFile = Path.Combine(sourceDir, "test_audio.mp3");
            var sampleContent = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            await File.WriteAllBytesAsync(testFile, sampleContent);

            var expectedCreation = new DateTime(2022, 5, 10, 14, 30, 0, DateTimeKind.Utc);
            var expectedLastWrite = new DateTime(2023, 8, 15, 18, 45, 0, DateTimeKind.Utc);

            File.SetCreationTimeUtc(testFile, expectedCreation);
            File.SetLastWriteTimeUtc(testFile, expectedLastWrite);

            var fileItem = new FileItem
            {
                Name = "test_audio.mp3",
                FullPath = testFile,
                IsDirectory = false,
                Length = sampleContent.Length,
                CreationTimeUtc = expectedCreation,
                LastWriteTimeUtc = expectedLastWrite,
                IsMtp = false
            };

            var service = new FileTransferService();
            var progress = new Progress<TransferProgressInfo>();

            // Act
            await service.TransferItemsAsync(
                new List<FileItem> { fileItem },
                targetDir,
                destIsMtp: false,
                destMtpDeviceId: null,
                progress,
                CancellationToken.None);

            // Assert
            var copiedFile = Path.Combine(targetDir, "test_audio.mp3");
            Assert.True(File.Exists(copiedFile));

            var copiedContent = await File.ReadAllBytesAsync(copiedFile);
            Assert.Equal(sampleContent, copiedContent);

            var actualCreation = File.GetCreationTimeUtc(copiedFile);
            var actualLastWrite = File.GetLastWriteTimeUtc(copiedFile);

            Assert.Equal(expectedCreation, actualCreation);
            Assert.Equal(expectedLastWrite, actualLastWrite);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TransferItemsAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"preserva_cancel_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var testFile = Path.Combine(tempDir, "cancel_test.bin");
            await File.WriteAllBytesAsync(testFile, new byte[1024]);

            var fileItem = new FileItem
            {
                Name = "cancel_test.bin",
                FullPath = testFile,
                IsDirectory = false,
                Length = 1024,
                IsMtp = false
            };

            var service = new FileTransferService();
            var progress = new Progress<TransferProgressInfo>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await service.TransferItemsAsync(
                    new List<FileItem> { fileItem },
                    tempDir,
                    destIsMtp: false,
                    destMtpDeviceId: null,
                    progress,
                    cts.Token);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
