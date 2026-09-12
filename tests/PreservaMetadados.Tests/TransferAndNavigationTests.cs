using PreservaMetadados.Models;
using PreservaMetadados.Services;
using PreservaMetadados.ViewModels;
using System.IO;

namespace PreservaMetadados.Tests;

public class TransferAndNavigationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _destDir;

    public TransferAndNavigationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "PreservaMetadados_Tests_" + Guid.NewGuid());
        _sourceDir = Path.Combine(_testRoot, "Source");
        _destDir = Path.Combine(_testRoot, "Dest");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task TransferItemsAsync_MustPreserveCreationAndLastWriteTimeUtc_Exactly()
    {
        // Arrange
        var testFile = Path.Combine(_sourceDir, "documento_original.txt");
        File.WriteAllText(testFile, "Conteúdo de teste com metadados estritos.");

        // Definir datas específicas no passado para verificação estrita
        var expectedCreationUtc = new DateTime(2021, 5, 14, 10, 30, 45, DateTimeKind.Utc);
        var expectedLastWriteUtc = new DateTime(2023, 8, 22, 16, 40, 12, DateTimeKind.Utc);

        File.SetCreationTimeUtc(testFile, expectedCreationUtc);
        File.SetLastWriteTimeUtc(testFile, expectedLastWriteUtc);

        var fileItem = new FileItem
        {
            Name = "documento_original.txt",
            FullPath = testFile,
            IsDirectory = false,
            Length = new FileInfo(testFile).Length,
            CreationTimeUtc = expectedCreationUtc,
            LastWriteTimeUtc = expectedLastWriteUtc,
            Extension = ".txt"
        };

        var transferService = new FileTransferService();
        var progress = new Progress<TransferProgressInfo>();

        // Act
        await transferService.TransferItemsAsync(
            new[] { fileItem },
            _destDir,
            destIsMtp: false,
            destMtpDeviceId: null,
            progress,
            CancellationToken.None
        );

        // Assert
        var destinationFile = Path.Combine(_destDir, "documento_original.txt");
        Assert.True(File.Exists(destinationFile), "O arquivo de destino deve existir.");

        var destInfo = new FileInfo(destinationFile);

        // Tolerância de 2 segundos devido a diferenças de granularidade entre sistemas de arquivos FAT/NTFS
        var diffCreation = Math.Abs((destInfo.CreationTimeUtc - expectedCreationUtc).TotalSeconds);
        var diffWrite = Math.Abs((destInfo.LastWriteTimeUtc - expectedLastWriteUtc).TotalSeconds);

        Assert.True(diffCreation < 2.0, $"CreationTimeUtc não preservado! Esperado: {expectedCreationUtc}, Atual: {destInfo.CreationTimeUtc}");
        Assert.True(diffWrite < 2.0, $"LastWriteTimeUtc não preservado! Esperado: {expectedLastWriteUtc}, Atual: {destInfo.LastWriteTimeUtc}");
    }

    [Fact]
    public async Task FileService_GetItemsAsync_ListsFilesWithCorrectMetadata()
    {
        // Arrange
        var subDir = Path.Combine(_sourceDir, "SubPasta1");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(_sourceDir, "foto1.jpg");
        File.WriteAllBytes(file1, new byte[1024]);

        var fileService = new FileService();

        // Act
        var items = await fileService.GetItemsAsync(_sourceDir);

        // Assert
        Assert.Contains(items, i => i.IsDirectory && i.Name == "SubPasta1");
        var photoItem = items.FirstOrDefault(i => !i.IsDirectory && i.Name == "foto1.jpg");
        Assert.NotNull(photoItem);
        Assert.Equal(1024, photoItem.Length);
        Assert.Equal("FileImage", photoItem.IconKind);
        Assert.NotNull(photoItem.CreationTimeUtc);
        Assert.NotNull(photoItem.LastWriteTimeUtc);
    }

    [Fact]
    public async Task FilePaneViewModel_NavigationAndBreadcrumbs_WorkCorrectly()
    {
        // Arrange
        var fileService = new FileService();
        var pane = new FilePaneViewModel("Origem", fileService);

        var deepDir = Path.Combine(_sourceDir, "Nivel1", "Nivel2");
        Directory.CreateDirectory(deepDir);

        // Act
        await pane.NavigateToPathAsync(deepDir, recordHistory: true);

        // Assert
        Assert.Equal(deepDir, pane.CurrentPath);
        Assert.True(pane.Breadcrumbs.Count >= 3);
        Assert.True(pane.CanGoUp);

        // Subir pasta
        await pane.GoUpAsync();
        Assert.Equal(Path.Combine(_sourceDir, "Nivel1"), pane.CurrentPath);
        Assert.True(pane.CanGoBack);

        // Voltar
        await pane.GoBackAsync();
        Assert.Equal(deepDir, pane.CurrentPath);
        Assert.True(pane.CanGoForward);
    }

    [Fact]
    public void FilePaneViewModel_SelectionSummary_CalculatesAccurately()
    {
        // Arrange
        var fileService = new FileService();
        var pane = new FilePaneViewModel("Origem", fileService);

        pane.Items.Add(new FileItem { Name = "a.txt", Length = 1000, IsDirectory = false, IsSelected = false });
        pane.Items.Add(new FileItem { Name = "b.txt", Length = 2000, IsDirectory = false, IsSelected = false });
        pane.Items.Add(new FileItem { Name = "c.txt", Length = 3000, IsDirectory = false, IsSelected = false });

        // Act 1: Sem seleção
        pane.UpdateSelectionSummary();
        Assert.Contains("3 item(ns)", pane.StatusSummary);

        // Act 2: Selecionar 2 arquivos
        pane.Items[0].IsSelected = true;
        pane.Items[1].IsSelected = true;
        pane.UpdateSelectionSummary();

        // Assert 2
        Assert.Contains("2 selecionado(s)", pane.StatusSummary);
        Assert.Contains("KB", pane.StatusSummary);
    }
}
