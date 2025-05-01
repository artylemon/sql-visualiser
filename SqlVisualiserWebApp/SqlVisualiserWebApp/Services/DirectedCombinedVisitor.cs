namespace SqlVisualiserWebApp.Services;

using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;

public class DirectedCombinedVisitor : TSqlFragmentVisitor
{
    private readonly HashSet<string> _tables;
    private readonly List<ISqlObject> _sqlObjects;
    private string _currentNodeName;

    public Dictionary<string, DirectedGraphNode> Graph { get; } = new Dictionary<string, DirectedGraphNode>(StringComparer.OrdinalIgnoreCase);

    public DirectedCombinedVisitor(List<string> tables, List<ISqlObject> sqlObjects)
    {
        _tables = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase); // Case-insensitive table names
        _sqlObjects = sqlObjects;
    }

    public void SetCurrentNode(ISqlObject sqlObject)
    {
        _currentNodeName = sqlObject.Name;
        if (!Graph.ContainsKey(_currentNodeName))
        {
            Graph[_currentNodeName] = new DirectedGraphNode(_currentNodeName, sqlObject.Type);
        }
    }

    public override void Visit(NamedTableReference node)
    {
        var objectName = node.SchemaObject.BaseIdentifier.Value;

        // Check if the object is a table
        if (_tables.Contains(objectName))
        {
            if (!Graph.ContainsKey(objectName))
            {
                Graph[objectName] = new DirectedGraphNode(objectName, NodeType.Table);
            }

            Graph[_currentNodeName].InNodes.Add(objectName); // Reading from the table
            Graph[objectName].OutNodes.Add(_currentNodeName); // Procedure reads from the table
        }
        else
        {
            // Check if the object is a known SQL object (e.g., function)
            var sqlObject = GetSqlObjectByName(objectName);
            if (sqlObject != null)
            {
                if (!Graph.ContainsKey(objectName))
                {
                    Graph[objectName] = new DirectedGraphNode(objectName, sqlObject.Type);
                }

                Graph[_currentNodeName].InNodes.Add(objectName); // Current node calls the SQL object
                Graph[objectName].OutNodes.Add(_currentNodeName); // SQL object is called by the current node
            }
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
                Graph[_currentNodeName].OutNodes.Add(tableName); // Writing to the table
                Graph[tableName].InNodes.Add(_currentNodeName); // Procedure writes to the table
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
                Graph[tableName].OutNodes.Add(_currentNodeName); // Writing to the table
                Graph[_currentNodeName].InNodes.Add(tableName); // Procedure writes to the table
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
                Graph[_currentNodeName].OutNodes.Add(tableName); // Writing to the table
                Graph[tableName].InNodes.Add(_currentNodeName); // Procedure writes to the table
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
                Graph[_currentNodeName].OutNodes.Add(tableName); // Writing to the table
                Graph[tableName].InNodes.Add(_currentNodeName); // Procedure writes to the table
            }
        }
        base.Visit(node);
    }

    public override void Visit(ExecuteStatement node)
    {
        // Case 1: Directly executing a stored procedure
        if (node.ExecuteSpecification.ExecutableEntity is ExecutableProcedureReference procedureReference)
        {
            var procedureName = procedureReference.ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value;
            HandleSqlObjectReference(procedureName);
        }
        // Case 2: Executing dynamic SQL
        else if (node.ExecuteSpecification.ExecutableEntity is ExecutableStringList stringList)
        {
            foreach (var sqlString in stringList.Strings)
            {
                // Attempt to parse the dynamic SQL and extract references
                HandleDynamicSql(sqlString);
            }
        }

        base.Visit(node);
    }

    public override void Visit(SelectStatement node)
    {
        if (node.QueryExpression is QuerySpecification querySpecification)
        {
            // Handle function calls in the SELECT list
            if (querySpecification.SelectElements != null)
            {
                foreach (var selectElement in querySpecification.SelectElements)
                {
                    if (selectElement is SelectScalarExpression scalarExpression &&
                        scalarExpression.Expression is FunctionCall functionCall)
                    {
                        var functionName = functionCall.FunctionName.Value;
                        var sqlObject = GetSqlObjectByName(functionName);

                        if (sqlObject != null)
                        {
                            if (!Graph.ContainsKey(functionName))
                            {
                                Graph[functionName] = new DirectedGraphNode(functionName, sqlObject.Type);
                            }
                            Graph[_currentNodeName].InNodes.Add(functionName); // Current node calls the function
                            Graph[functionName].OutNodes.Add(_currentNodeName); // Function is called by the current node
                        }
                    }
                }
            }

            // Handle function calls in the FROM clause
            if (querySpecification.FromClause?.TableReferences != null)
            {
                foreach (var tableReference in querySpecification.FromClause.TableReferences)
                {
                    if (tableReference is SchemaObjectFunctionTableReference functionTableReference)
                    {
                        var functionName = functionTableReference.SchemaObject.BaseIdentifier.Value;
                        var sqlObject = GetSqlObjectByName(functionName);

                        if (sqlObject != null)
                        {
                            if (!Graph.ContainsKey(functionName))
                            {
                                Graph[functionName] = new DirectedGraphNode(functionName, sqlObject.Type);
                            }
                            Graph[_currentNodeName].InNodes.Add(functionName); // Current node calls the function
                            Graph[functionName].OutNodes.Add(_currentNodeName); // Function is called by the current node
                        }
                    }
                    else if (tableReference is NamedTableReference namedTableReference)
                    {
                        var tableName = namedTableReference.SchemaObject.BaseIdentifier.Value;
                        if (_tables.Contains(tableName))
                        {
                            if (!Graph.ContainsKey(tableName))
                            {
                                Graph[tableName] = new DirectedGraphNode(tableName, NodeType.Table);
                            }
                            Graph[_currentNodeName].InNodes.Add(tableName); // Reading from the table
                            Graph[tableName].OutNodes.Add(_currentNodeName); // Table is read by the current node
                        }
                    }
                }
            }
        }

        base.Visit(node);
    }

    private ISqlObject GetSqlObjectByName(string name)
    {
        // Use StringComparer.OrdinalIgnoreCase for robust case-insensitive comparison
        return _sqlObjects.FirstOrDefault(obj =>
            string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void HandleSqlObjectReference(string objectName)
    {
        var sqlObject = GetSqlObjectByName(objectName);

        if (sqlObject != null)
        {
            if (!Graph.ContainsKey(objectName))
            {
                Graph[objectName] = new DirectedGraphNode(objectName, sqlObject.Type);
            }

            Graph[_currentNodeName].OutNodes.Add(objectName); // Current node calls another node
            Graph[objectName].InNodes.Add(_currentNodeName); // Called node is referenced
        }
    }

    private void HandleDynamicSql(ValueExpression sqlExpression)
    {
        // Use TSql150Parser to parse the dynamic SQL
        var parser = new TSql150Parser(false);
        using (var reader = new StringReader(sqlExpression.ScriptTokenStream
            .Skip(sqlExpression.FirstTokenIndex)
            .Take(sqlExpression.LastTokenIndex - sqlExpression.FirstTokenIndex + 1)
            .Aggregate(string.Empty, (current, token) => current + token.Text)))
        {
            var fragment = parser.Parse(reader, out var errors);

            if (errors != null && errors.Count > 0)
            {
                // Log or handle parsing errors
                return;
            }

            // Visit the parsed fragment to extract references
            fragment.Accept(this);
        }
    }

}
