using PreservaMetadados.Services;
using Xunit;

namespace PreservaMetadados.Tests;

public class FileServiceTests
{
    [Fact]
    public async Task GetItemsAsync_EnumeratesDirectoryProgressively()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PreservaTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create test subfolders and files
            Directory.CreateDirectory(Path.Combine(tempDir, "SubFolder1"));
            Directory.CreateDirectory(Path.Combine(tempDir, "SubFolder2"));
            File.WriteAllText(Path.Combine(tempDir, "file1.txt"), "hello");
            File.WriteAllText(Path.Combine(tempDir, "file2.txt"), "world");

            var fileService = new FileService();
            var items = new List<string>();

            await foreach (var item in fileService.GetItemsAsync(tempDir))
            {
                items.Add(item.Name);
            }

            Assert.Equal(4, items.Count);
            Assert.Contains("SubFolder1", items);
            Assert.Contains("SubFolder2", items);
            Assert.Contains("file1.txt", items);
            Assert.Contains("file2.txt", items);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task GetItemsAsync_RespectsCancellationToken()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PreservaTestCancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            for (int i = 0; i < 50; i++)
            {
                File.WriteAllText(Path.Combine(tempDir, $"file_{i}.txt"), "test");
            }

            var fileService = new FileService();
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var item in fileService.GetItemsAsync(tempDir, cancellationToken: cts.Token))
                {
                    // Should throw cancellation
                }
            });
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
