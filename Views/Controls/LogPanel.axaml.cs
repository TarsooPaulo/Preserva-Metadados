using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using PreservaMetadados.Models;

namespace PreservaMetadados.Views.Controls;

public partial class LogPanel : UserControl
{
    public static readonly StyledProperty<ObservableCollection<LogEntry>?> LogsProperty = 
        AvaloniaProperty.Register<LogPanel, ObservableCollection<LogEntry>?>(nameof(Logs));

    public ObservableCollection<LogEntry>? Logs
    {
        get => GetValue(LogsProperty);
        set => SetValue(LogsProperty, value);
    }

    public LogPanel()
    {
        InitializeComponent();
    }
}