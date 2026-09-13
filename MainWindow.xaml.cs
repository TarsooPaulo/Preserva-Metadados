using PreservaMetadados.Models;
using PreservaMetadados.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace PreservaMetadados;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowState = WindowState.Maximized;
        DataContext = new MainViewModel();
    }

    private void SourceList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SourcePane.SelectedItem != null)
        {
            _ = vm.SourcePane.OpenItemAsync(vm.SourcePane.SelectedItem);
        }
    }

    private void DestList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.DestinationPane.SelectedItem != null)
        {
            _ = vm.DestinationPane.OpenItemAsync(vm.DestinationPane.SelectedItem);
        }
    }
}