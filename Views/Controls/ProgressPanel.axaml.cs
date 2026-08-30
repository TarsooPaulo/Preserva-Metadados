using System;
using Avalonia;
using Avalonia.Controls;

namespace PreservaMetadados.Views.Controls;

public partial class ProgressPanel : UserControl
{
    public static readonly StyledProperty<double> OverallProgressProperty = 
        AvaloniaProperty.Register<ProgressPanel, double>(nameof(OverallProgress));

    public static readonly StyledProperty<string?> StatusMessageProperty = 
        AvaloniaProperty.Register<ProgressPanel, string?>(nameof(StatusMessage));

    public static readonly StyledProperty<DateTime?> TransferStartTimeProperty = 
        AvaloniaProperty.Register<ProgressPanel, DateTime?>(nameof(TransferStartTime));

    public static readonly StyledProperty<DateTime?> TransferEndTimeProperty = 
        AvaloniaProperty.Register<ProgressPanel, DateTime?>(nameof(TransferEndTime));

    public ProgressPanel()
    {
        InitializeComponent();
    }
    
    public double OverallProgress
    {
        get => GetValue(OverallProgressProperty);
        set => SetValue(OverallProgressProperty, value);
    }
    
    public string? StatusMessage
    {
        get => GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }
    
    public DateTime? TransferStartTime
    {
        get => GetValue(TransferStartTimeProperty);
        set => SetValue(TransferStartTimeProperty, value);
    }
    
    public DateTime? TransferEndTime
    {
        get => GetValue(TransferEndTimeProperty);
        set => SetValue(TransferEndTimeProperty, value);
    }
    
    public TimeSpan? TransferDuration
    {
        get
        {
            if (TransferStartTime.HasValue && TransferEndTime.HasValue)
                return TransferEndTime.Value - TransferStartTime.Value;
            if (TransferStartTime.HasValue)
                return DateTime.UtcNow - TransferStartTime.Value;
            return null;
        }
    }
}