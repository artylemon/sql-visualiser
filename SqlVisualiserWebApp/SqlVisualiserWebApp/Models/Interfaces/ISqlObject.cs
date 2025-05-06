using SqlVisualiserWebApp.Models.Enums;
namespace SqlVisualiserWebApp.Models.Interfaces;

public interface ISqlObject
{
    string Name
    {
        get; set;
    }
    string Schema
    {
        get; set;
    }
    string Definition
    {
        get; set;
    }
    NodeType Type
    {
        get;
    }

    // New property to store the catalog name
    string Catalog
    {
        get; set;
    }
}
