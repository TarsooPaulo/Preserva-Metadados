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
        DataContext = new MainViewModel();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
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