using PreservaMetadados.Services;
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
    }
}
