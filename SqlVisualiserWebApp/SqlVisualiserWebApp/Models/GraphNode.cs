namespace SqlVisualiserWebApp.Models;

using SqlVisualiserWebApp.Models.Enums;

public class GraphNode
{
    public string Name { get; set; }
    public NodeType Type { get; set; }
    public HashSet<string> AdjacentNodes { get; set; } = new HashSet<string>();

    public GraphNode(string name, NodeType type)
    {
        this.Name = name;
        this.Type = type;
    }
}