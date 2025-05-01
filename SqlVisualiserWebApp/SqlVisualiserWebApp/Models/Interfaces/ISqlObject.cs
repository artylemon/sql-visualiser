using SqlVisualiserWebApp.Models.Enums;
namespace SqlVisualiserWebApp.Models.Interfaces;

public interface ISqlObject
{
    string Name { get; set; }
    string Definition { get; set; }
    NodeType Type { get; } // Add NodeType to identify the object type
}
