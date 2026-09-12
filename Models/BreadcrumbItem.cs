namespace PreservaMetadados.Models;

public class BreadcrumbItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsLast { get; set; }
}
