namespace SqlVisualiser.Models;

using System.Collections.Generic;

class TableUsage
{
    public string TableName { get; set; }
    public List<string> Readers { get; set; } = new List<string>();
    public List<string> Writers { get; set; } = new List<string>();
}
