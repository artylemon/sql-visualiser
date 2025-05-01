namespace SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;

public class SqlFunction : ISqlObject
{
    public string Name
    {
        get; set;
    }
    public string Definition
    {
        get; set;
    }
    public NodeType Type => NodeType.Function; // Always return Function
}
