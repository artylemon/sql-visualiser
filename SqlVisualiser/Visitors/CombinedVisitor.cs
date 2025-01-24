namespace SqlVisualiser.Visitors;

using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlVisualiser.Models;
using SqlVisualiser.Utils.Enums;
using Utils;

public class CombinedVisitor : TSqlFragmentVisitor
{
    private readonly List<string> _tables;
    private readonly List<string> _procedures;
    private string _currentProcedure;

    public Dictionary<string, GraphNode> Graph { get; } = new Dictionary<string, GraphNode>();

    public CombinedVisitor(List<string> tables, List<string> procedures)
    {
        _tables = tables;
        _procedures = procedures;
    }

    public void SetCurrentProcedure(string procedureName)
    {
        _currentProcedure = procedureName;
        if (!Graph.ContainsKey(procedureName))
        {
            Graph[procedureName] = new GraphNode(procedureName, NodeType.Procedure);
        }
    }

    public override void Visit(NamedTableReference node)
    {
        var tableName = node.SchemaObject.BaseIdentifier.Value;
        if (_tables.Contains(tableName))
        {
            if (!Graph.ContainsKey(tableName))
            {
                Graph[tableName] = new GraphNode(tableName, NodeType.Table);
            }
            Graph[_currentProcedure].AdjacentNodes.Add(tableName);
            Graph[tableName].AdjacentNodes.Add(_currentProcedure);
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
                    Graph[tableName] = new GraphNode(tableName, NodeType.Table);
                }
                Graph[_currentProcedure].AdjacentNodes.Add(tableName);
                Graph[tableName].AdjacentNodes.Add(_currentProcedure);
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
                    Graph[tableName] = new GraphNode(tableName, NodeType.Table);
                }
                Graph[_currentProcedure].AdjacentNodes.Add(tableName);
                Graph[tableName].AdjacentNodes.Add(_currentProcedure);
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
                    Graph[tableName] = new GraphNode(tableName, NodeType.Table);
                }
                Graph[_currentProcedure].AdjacentNodes.Add(tableName);
                Graph[tableName].AdjacentNodes.Add(_currentProcedure);
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
                    Graph[tableName] = new GraphNode(tableName, NodeType.Table);
                }
                Graph[_currentProcedure].AdjacentNodes.Add(tableName);
                Graph[tableName].AdjacentNodes.Add(_currentProcedure);
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
                    Graph[procedureName] = new GraphNode(procedureName, NodeType.Procedure);
                }
                Graph[_currentProcedure].AdjacentNodes.Add(procedureName);
                Graph[procedureName].AdjacentNodes.Add(_currentProcedure);
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
                                Graph[tableName] = new GraphNode(tableName, NodeType.Table);
                            }
                            Graph[_currentProcedure].AdjacentNodes.Add(tableName);
                            Graph[tableName].AdjacentNodes.Add(_currentProcedure);
                        }
                    }
                    else if (tableReference is SchemaObjectFunctionTableReference functionTableReference)
                    {
                        var procedureName = functionTableReference.SchemaObject.BaseIdentifier.Value;
                        if (_procedures.Contains(procedureName))
                        {
                            if (!Graph.ContainsKey(procedureName))
                            {
                                Graph[procedureName] = new GraphNode(procedureName, NodeType.Procedure);
                            }
                            Graph[_currentProcedure].AdjacentNodes.Add(procedureName);
                            Graph[procedureName].AdjacentNodes.Add(_currentProcedure);
                        }
                    }
                }
            }
        }
        base.Visit(node);
    }
}
