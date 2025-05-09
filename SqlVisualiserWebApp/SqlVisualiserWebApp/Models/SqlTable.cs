namespace SqlVisualiserWebApp.Models
{
    using SqlVisualiserWebApp.Models.Enums;
    using SqlVisualiserWebApp.Models.Interfaces;

    public class SqlTable : ISqlObject
    {
        public string Name
        {
            get; set;
        }
        public string Schema
        {
            get; set;
        }
        public string Definition { get; set; } = string.Empty;
        public NodeType Type { get; } = NodeType.Table; // Always NodeType.Table
        public string Catalog
        {
            get; set;
        }
    }
}
