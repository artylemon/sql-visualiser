namespace SqlVisualiser.Visitors;

using Microsoft.SqlServer.TransactSql.ScriptDom;

public class TableUsageVisitor : TSqlFragmentVisitor
{
    private readonly HashSet<string> tableNames;
    public Dictionary<string, (bool IsRead, bool IsWrite)> TableUsages { get; } = new Dictionary<string, (bool IsRead, bool IsWrite)>();

    public TableUsageVisitor(IEnumerable<string> tableNames)
    {
        this.tableNames = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);
    }

    private void MarkTableUsage(string tableName, bool isRead, bool isWrite)
    {
        if (!TableUsages.ContainsKey(tableName))
        {
            TableUsages[tableName] = (IsRead: false, IsWrite: false);
        }

        var usage = TableUsages[tableName];
        TableUsages[tableName] = (IsRead: usage.IsRead || isRead, IsWrite: usage.IsWrite || isWrite);
    }

    public override void Visit(InsertStatement node)
    {
        if (node.InsertSpecification.Target is NamedTableReference target)
        {
            var tableName = target.SchemaObject.BaseIdentifier.Value;
            if (tableNames.Contains(tableName))
            {
                MarkTableUsage(tableName, isRead: false, isWrite: true);
            }
        }
    }

    public override void Visit(UpdateStatement node)
    {
        if (node.UpdateSpecification.Target is NamedTableReference target)
        {
            var tableName = target.SchemaObject.BaseIdentifier.Value;
            if (tableNames.Contains(tableName))
            {
                MarkTableUsage(tableName, isRead: false, isWrite: true);
            }
        }
    }

    public override void Visit(DeleteStatement node)
    {
        if (node.DeleteSpecification.Target is NamedTableReference target)
        {
            var tableName = target.SchemaObject.BaseIdentifier.Value;
            if (tableNames.Contains(tableName))
            {
                MarkTableUsage(tableName, isRead: false, isWrite: true);
            }
        }
    }

    public override void Visit(MergeStatement node)
    {
        if (node.MergeSpecification.Target is NamedTableReference target)
        {
            var tableName = target.SchemaObject.BaseIdentifier.Value;
            if (tableNames.Contains(tableName))
            {
                MarkTableUsage(tableName, isRead: false, isWrite: true);
            }
        }
    }

    public override void Visit(SelectStatement node)
    {
        if (node.QueryExpression is QuerySpecification querySpecification)
        {
            foreach (var tableReference in querySpecification.FromClause.TableReferences)
            {
                if (tableReference is NamedTableReference namedTableReference)
                {
                    var tableName = namedTableReference.SchemaObject.BaseIdentifier.Value;
                    if (tableNames.Contains(tableName))
                    {
                        MarkTableUsage(tableName, isRead: true, isWrite: false);
                    }
                }
                else if (tableReference is SchemaObjectFunctionTableReference functionTableReference)
                {
                    var tableName = functionTableReference.SchemaObject.BaseIdentifier.Value;
                    if (tableNames.Contains(tableName))
                    {
                        MarkTableUsage(tableName, isRead: true, isWrite: false);
                    }
                }
            }
        }
    }

    public override void Visit(ExecuteStatement node)
    {
        if (node.ExecuteSpecification.ExecutableEntity is ExecutableProcedureReference procedureReference)
        {
            foreach (var parameter in procedureReference.Parameters)
            {
                if (parameter.Variable is { } variableReference)
                {
                    var tableName = variableReference.Name;
                    if (tableNames.Contains(tableName))
                    {
                        MarkTableUsage(tableName, isRead: true, isWrite: false);
                    }
                }
            }
        }
    }
}
