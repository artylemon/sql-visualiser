namespace SqlVisualiserWebApp.Models;

using SqlVisualiserWebApp.Models.Enums;

public class DirectedGraphNode
{
    public string Name
    {
        get; set;
    }
    public NodeType Type
    {
        get; set;
    }

    // New property to store the catalog name
    public string Catalog
    {
        get; set;
    }

    // Nodes that represent incoming relationships (e.g., sprocs reading from this table)
    public HashSet<string> InNodes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Nodes that represent outgoing relationships (e.g., sprocs writing to this table)
    public HashSet<string> OutNodes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public DirectedGraphNode(string name, NodeType type, string catalog)
    {
        this.Name = name;
        this.Type = type;
        this.Catalog = catalog;
    }
}
