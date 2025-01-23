namespace SqlVisualiser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

class TableUsageVisitor : TSqlFragmentVisitor
{
    private readonly string tableName;
    public bool IsRead { get; private set; }
    public bool IsWrite { get; private set; }

    public TableUsageVisitor(string tableName)
    {
        this.tableName = tableName;
    }

    public override void Visit(NamedTableReference node)
    {
        if (node.SchemaObject.BaseIdentifier.Value.Equals(tableName, StringComparison.OrdinalIgnoreCase))
        {
            IsRead = true;
        }
    }

    public override void Visit(InsertStatement node)
    {
        if (node.InsertSpecification.Target is NamedTableReference target &&
            target.SchemaObject.BaseIdentifier.Value.Equals(tableName, StringComparison.OrdinalIgnoreCase))
        {
            IsWrite = true;
        }
    }

    public override void Visit(UpdateStatement node)
    {
        if (node.UpdateSpecification.Target is NamedTableReference target &&
            target.SchemaObject.BaseIdentifier.Value.Equals(tableName, StringComparison.OrdinalIgnoreCase))
        {
            IsWrite = true;
        }
    }

    public override void Visit(DeleteStatement node)
    {
        if (node.DeleteSpecification.Target is NamedTableReference target &&
            target.SchemaObject.BaseIdentifier.Value.Equals(tableName, StringComparison.OrdinalIgnoreCase))
        {
            IsWrite = true;
        }
    }
}
