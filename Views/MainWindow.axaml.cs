using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using PreservaMetadados.ViewModels;

namespace PreservaMetadados.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SetupDragAndDrop();
    }
    
    private void SetupDragAndDrop()
    {
        var dragDropBorder = this.FindControl<Border>("DragDropBorder");
        if (dragDropBorder != null)
        {
            DragDrop.SetAllowDrop(dragDropBorder, true);
            dragDropBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            dragDropBorder.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }
    
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DragEffects & DragDropEffects.Copy;
        if (!e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.None;
        }
    }
    
    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                var paths = files
                    .Select(f => f.Path.LocalPath)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToArray();
                    
                if (paths.Length > 0 && DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.AddFilesFromPaths(paths);
                }
            }
        }
    }
}