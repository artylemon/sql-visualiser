namespace SqlVisualiserWebApp.Services;

using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;

public class DirectedCombinedVisitor : TSqlFragmentVisitor
{
    private readonly List<string> _tables;
    private readonly List<string> _procedures;
    private string _currentProcedure;

    public Dictionary<string, DirectedGraphNode> Graph { get; } = new Dictionary<string, DirectedGraphNode>();

    public DirectedCombinedVisitor(List<string> tables, List<string> procedures)
    {
        _tables = tables;
        _procedures = procedures;
    }

    public void SetCurrentProcedure(string procedureName)
    {
        _currentProcedure = procedureName;
        if (!Graph.ContainsKey(procedureName))
        {
            Graph[procedureName] = new DirectedGraphNode(procedureName, NodeType.Procedure);
        }
    }

    public override void Visit(NamedTableReference node)
    {
        var tableName = node.SchemaObject.BaseIdentifier.Value;
        if (_tables.Contains(tableName))
        {
            if (!Graph.ContainsKey(tableName))
            {
                Graph[tableName] = new DirectedGraphNode(tableName, NodeType.Table);
            }
            Graph[_currentProcedure].InNodes.Add(tableName); // Reading from the table
            Graph[tableName].OutNodes.Add(_currentProcedure); // Procedure reads from the table
        }
        base.Visit(node);
    }

    public override void Visit(InsertStatement node)
    {
        if (node.InsertSpecification.Target is NamedTableReference namedTable)
        {
            var tableName = namedTable.SchemaObject.BaseIdentifier.Value;
            if (_tables.Contains(tableName))
            {
                if (!Graph.ContainsKey(tableName))
                {
                    Graph[tableName] = new DirectedGraphNode(tableName, NodeType.Table);
                }
                Graph[_currentProcedure].OutNodes.Add(tableName); // Writing to the table
                Graph[tableName].InNodes.Add(_currentProcedure); // Procedure writes to the table
            }
        }
        base.Visit(node);
    }

    public override void Visit(UpdateStatement node)
    {
        if (node.UpdateSpecification.Target is NamedTableReference namedTable)
        {
            var tableName = namedTable.SchemaObject.BaseIdentifier.Value;
            if (_tables.Contains(tableName))
            {
                if (!Graph.ContainsKey(tableName))
                {
                    Graph[tableName] = new DirectedGraphNode(tableName, NodeType.Table);
                }
                Graph[tableName].OutNodes.Add(_currentProcedure); // Writing to the table
                Graph[_currentProcedure].InNodes.Add(tableName); // Procedure writes to the table
            }
        }
        base.Visit(node);
    }

    public override void Visit(DeleteStatement node)
    {
        if (node.DeleteSpecification.Target is NamedTableReference namedTable)
        {
            var tableName = namedTable.SchemaObject.BaseIdentifier.Value;
            if (_tables.Contains(tableName))
            {
                if (!Graph.ContainsKey(tableName))
                {
                    Graph[tableName] = new DirectedGraphNode(tableName, NodeType.Table);
                }
                Graph[_currentProcedure].OutNodes.Add(tableName); // Writing to the table
                Graph[tableName].InNodes.Add(_currentProcedure); // Procedure writes to the table
            }
        }
        base.Visit(node);
    }

    public override void Visit(MergeStatement node)
    {
        if (node.MergeSpecification.Target is NamedTableReference namedTable)
        {
            var tableName = namedTable.SchemaObject.BaseIdentifier.Value;
            if (_tables.Contains(tableName))
            {
                if (!Graph.ContainsKey(tableName))
                {
                    Graph[tableName] = new DirectedGraphNode(tableName, NodeType.Table);
                }
                Graph[_currentProcedure].OutNodes.Add(tableName); // Writing to the table
                Graph[tableName].InNodes.Add(_currentProcedure); // Procedure writes to the table
            }
        }
        base.Visit(node);
    }

    public override void Visit(ExecuteStatement node)
    {
        if (node.ExecuteSpecification.ExecutableEntity is ExecutableProcedureReference procedureReference)
        {
            var procedureName = procedureReference.ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value;
            if (_procedures.Contains(procedureName))
            {
                if (!Graph.ContainsKey(procedureName))
                {
                    Graph[procedureName] = new DirectedGraphNode(procedureName, NodeType.Procedure);
                }
                Graph[_currentProcedure].OutNodes.Add(procedureName); // Procedure calls another procedure
                Graph[procedureName].InNodes.Add(_currentProcedure); // Called procedure is referenced
            }
        }
        base.Visit(node);
    }

    public override void Visit(SelectStatement node)
    {
        if (node.QueryExpression is QuerySpecification querySpecification)
        {
            if (querySpecification.FromClause?.TableReferences != null)
            {
                foreach (var tableReference in querySpecification.FromClause.TableReferences)
                {
                    if (tableReference is NamedTableReference namedTableReference)
                    {
                        var tableName = namedTableReference.SchemaObject.BaseIdentifier.Value;
                        if (_tables.Contains(tableName))
                        {
                            if (!Graph.ContainsKey(tableName))
                            {
                                Graph[tableName] = new DirectedGraphNode(tableName, NodeType.Table);
                            }
                            Graph[_currentProcedure].InNodes.Add(tableName); // Reading from the table
                            Graph[tableName].OutNodes.Add(_currentProcedure); // Procedure reads from the table
                        }
                    }
                    else if (tableReference is SchemaObjectFunctionTableReference functionTableReference)
                    {
                        var procedureName = functionTableReference.SchemaObject.BaseIdentifier.Value;
                        if (_procedures.Contains(procedureName))
                        {
                            if (!Graph.ContainsKey(procedureName))
                            {
                                Graph[procedureName] = new DirectedGraphNode(procedureName, NodeType.Procedure);
                            }
                            Graph[procedureName].OutNodes.Add(_currentProcedure); // Procedure calls another procedure
                            Graph[_currentProcedure].InNodes.Add(procedureName); // Called procedure is referenced
                        }
                    }
                }
            }
        }
        base.Visit(node);
    }
}
