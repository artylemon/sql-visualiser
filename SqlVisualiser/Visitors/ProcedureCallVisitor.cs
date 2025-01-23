namespace SqlVisualiser.Visitors;

using Microsoft.SqlServer.TransactSql.ScriptDom;

class ProcedureCallVisitor : TSqlFragmentVisitor
{
    private readonly HashSet<string> procedureNames;
    public HashSet<string> CalledProcedures { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ProcedureCallVisitor(IEnumerable<string> procedureNames)
    {
        this.procedureNames = new HashSet<string>(procedureNames, StringComparer.OrdinalIgnoreCase);
    }

    private void AddProcedureIfCalled(string procedureName)
    {
        if (procedureNames.Contains(procedureName))
        {
            CalledProcedures.Add(procedureName);
        }
    }

    public override void Visit(ExecuteStatement node)
    {
        if (node.ExecuteSpecification.ExecutableEntity is ExecutableProcedureReference procedureReference)
        {
            var procedureName = procedureReference.ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value;
            AddProcedureIfCalled(procedureName);
        }
    }

    public override void Visit(SelectStatement node)
    {
        if (node.QueryExpression is QuerySpecification querySpecification)
        {
            foreach (var tableReference in querySpecification.FromClause.TableReferences)
            {
                if (tableReference is SchemaObjectFunctionTableReference functionTableReference)
                {
                    var procedureName = functionTableReference.SchemaObject.BaseIdentifier.Value;
                    AddProcedureIfCalled(procedureName);
                }
            }
        }
    }
}
