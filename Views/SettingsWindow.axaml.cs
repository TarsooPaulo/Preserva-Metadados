using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PreservaMetadados.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }
    
    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}