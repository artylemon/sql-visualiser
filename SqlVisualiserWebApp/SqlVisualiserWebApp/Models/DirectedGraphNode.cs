namespace SqlVisualiserWebApp.Models;

using SqlVisualiserWebApp.Models.Enums;

public class DirectedGraphNode
{
    public string Name { get; set; }
    public NodeType Type { get; set; }

    // Nodes that represent incoming relationships (e.g., sprocs reading from this table)
    public HashSet<string> InNodes { get; set; } = new HashSet<string>();

    // Nodes that represent outgoing relationships (e.g., sprocs writing to this table)
    public HashSet<string> OutNodes { get; set; } = new HashSet<string>();

    public DirectedGraphNode(string name, NodeType type)
    {
        this.Name = name;
        this.Type = type;
    }
}
