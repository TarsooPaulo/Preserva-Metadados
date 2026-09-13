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

    // Arrasto de janela ao clicar no cabeçalho
    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            this.DragMove();
    }

    // Botão Minimizar
    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    // Botão Maximizar / Restaurar
    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = (this.WindowState == WindowState.Maximized)
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    // Botão Fechar
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}