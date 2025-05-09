namespace SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;

public class SqlFunction : ISqlObject
{
    public string Name
    {
        get; set;
    }
    public string Schema
    {
        get; set;
    }
    public string Definition
    {
        get; set;
    }
    public NodeType Type { get; } = NodeType.Function;

    // New property to store the catalog name
    public string Catalog
    {
        get; set;
    }
}
