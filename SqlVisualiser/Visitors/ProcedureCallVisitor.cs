namespace SqlVisualiser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

class ProcedureCallVisitor : TSqlFragmentVisitor
{
    private readonly string targetProcedure;
    public bool IsCalled { get; private set; }

    public ProcedureCallVisitor(string targetProcedure)
    {
        this.targetProcedure = targetProcedure;
    }

    public override void Visit(ExecuteStatement node)
    {
        if (node.ExecuteSpecification.ExecutableEntity is ExecutableProcedureReference procedureReference &&
            procedureReference.ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value.Equals(targetProcedure, StringComparison.OrdinalIgnoreCase))
        {
            IsCalled = true;
        }
    }
}
