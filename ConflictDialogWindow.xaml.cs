using System.Windows;
using System.Windows.Input;

namespace PreservaMetadados;

public enum ConflictResolution
{
    Overwrite,
    Skip,
    Cancel
}

public class FileConflictInfo
{
    public required string ItemName { get; set; }
    public required string DestinationPath { get; set; }
    public bool IsDirectory { get; set; }
}

public class ConflictResolutionResult
{
    public ConflictResolution Resolution { get; set; }
    public bool ApplyToAll { get; set; }
}

public partial class ConflictDialogWindow : Window
{
    public ConflictResolution SelectedResolution { get; private set; } = ConflictResolution.Cancel;
    public bool ApplyToAll { get; private set; }

    public ConflictDialogWindow(string itemName, bool isDirectory, string destinationPath)
    {
        InitializeComponent();
        TxtMessage.Text = $"O arquivo/pasta '{itemName}' já existe no destino. Como deseja prosseguir?";
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnOverwrite_Click(object sender, RoutedEventArgs e)
    {
        SelectedResolution = ConflictResolution.Overwrite;
        ApplyToAll = ChkApplyToAll.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        SelectedResolution = ConflictResolution.Skip;
        ApplyToAll = ChkApplyToAll.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedResolution = ConflictResolution.Cancel;
        ApplyToAll = ChkApplyToAll.IsChecked == true;
        DialogResult = false;
        Close();
    }
}
