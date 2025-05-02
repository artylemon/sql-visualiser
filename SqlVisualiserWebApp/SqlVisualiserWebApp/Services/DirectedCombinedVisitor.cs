namespace SqlVisualiserWebApp.Services;

using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class DirectedCombinedVisitor : TSqlFragmentVisitor
{
    private readonly HashSet<string> _tables;
    private readonly List<ISqlObject> _sqlObjects;
    private string _currentNodeName = string.Empty;
    private readonly TSql150Parser _dynamicSqlParser;
    // *** NEW: Track the target table of the current DML operation ***
    private string? _currentDmlTargetTable = null;

    public Dictionary<string, DirectedGraphNode> Graph { get; } = new Dictionary<string, DirectedGraphNode>(StringComparer.OrdinalIgnoreCase);

    public DirectedCombinedVisitor(List<string> tables, List<ISqlObject> sqlObjects)
    {
        _sqlObjects = sqlObjects ?? throw new ArgumentNullException(nameof(sqlObjects));
        _tables = new HashSet<string>(tables ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _dynamicSqlParser = new TSql150Parser(false);
    }

    public void SetCurrentNode(ISqlObject sqlObject)
    {
        if (sqlObject == null || string.IsNullOrWhiteSpace(sqlObject.Name))
        {
            Console.Error.WriteLine("Attempted to set current node with null or empty name.");
            _currentNodeName = string.Empty;
            return;
        }

        _currentNodeName = sqlObject.Name;
        if (!Graph.ContainsKey(_currentNodeName))
        {
            Graph[_currentNodeName] = new DirectedGraphNode(_currentNodeName, sqlObject.Type);
        }
    }

    // Helper: Data flows FROM source TO consumer
    private void AddDataFlowDependency(string sourceOfDataNodeName, NodeType sourceOfDataType, string consumerNodeName)
    {
        if (string.IsNullOrWhiteSpace(consumerNodeName) || !Graph.ContainsKey(consumerNodeName))
        {
            Console.Error.WriteLine($"Consumer node '{consumerNodeName ?? "NULL"}' not found for data flow from '{sourceOfDataNodeName}'.");
            return;
        }

        if (!Graph.ContainsKey(sourceOfDataNodeName))
        {
            Graph[sourceOfDataNodeName] = new DirectedGraphNode(sourceOfDataNodeName, sourceOfDataType);
        }

        Graph[sourceOfDataNodeName].OutNodes.Add(consumerNodeName);
        Graph[consumerNodeName].InNodes.Add(sourceOfDataNodeName);
    }

    // Helper: Data is written/modified FROM modifier TO target
    private void AddDataWriteDependency(string modifierNodeName, string targetNodeName, NodeType targetNodeType)
    {
        if (string.IsNullOrWhiteSpace(modifierNodeName) || !Graph.ContainsKey(modifierNodeName))
        {
            Console.Error.WriteLine($"Modifier node '{modifierNodeName ?? "NULL"}' not found for data write to '{targetNodeName}'.");
            return;
        }

        if (!Graph.ContainsKey(targetNodeName))
        {
            Graph[targetNodeName] = new DirectedGraphNode(targetNodeName, targetNodeType);
        }

        Graph[modifierNodeName].OutNodes.Add(targetNodeName);
        Graph[targetNodeName].InNodes.Add(modifierNodeName);
    }

    // Catch SCALAR function calls (in WHERE, SELECT list, SET, etc.)
    public override void Visit(FunctionCall node)
    {
        var functionName = node.FunctionName?.Value;
        if (!string.IsNullOrWhiteSpace(_currentNodeName) && !string.IsNullOrWhiteSpace(functionName))
        {
            var sqlObject = GetSqlObjectByName(functionName);
            if (sqlObject != null && (sqlObject.Type == NodeType.Function || sqlObject.Type == NodeType.Function))
            {
                AddDataFlowDependency(functionName, sqlObject.Type, _currentNodeName);
            }
        }

        base.Visit(node);
    }

    // Catch TABLE-VALUED function calls in FROM/JOIN clauses (if parser identifies them as such)
    public override void Visit(SchemaObjectFunctionTableReference node)
    {
        // Console.WriteLine($"DEBUG: Entered Visit(SchemaObjectFunctionTableReference) for node: {node.SchemaObject?.BaseIdentifier?.Value}");
        var functionName = node.SchemaObject?.BaseIdentifier?.Value;
        if (!string.IsNullOrWhiteSpace(_currentNodeName) && !string.IsNullOrWhiteSpace(functionName))
        {
            var sqlObject = GetSqlObjectByName(functionName);
            if (sqlObject != null && (sqlObject.Type == NodeType.Function || sqlObject.Type == NodeType.Function))
            {
                // Console.WriteLine($"DEBUG: Adding DataFlowDependency (TVF): {functionName} -> {_currentNodeName}");
                AddDataFlowDependency(functionName, sqlObject.Type, _currentNodeName);
            }
        }

        base.Visit(node);
    }

    // Handles references to actual TABLES, VIEWS, or FUNCTIONS referenced with table-like syntax
    public override void Visit(NamedTableReference node)
    {
        var objectName = node.SchemaObject?.BaseIdentifier?.Value;
        // Console.WriteLine($"DEBUG: Entered Visit(NamedTableReference) for node: {objectName}. Current DML Target: {_currentDmlTargetTable}");

        if (!string.IsNullOrWhiteSpace(_currentNodeName) && !string.IsNullOrWhiteSpace(objectName))
        {
            // *** MODIFIED: Check if this reference is the target of the current DML operation ***
            bool isCurrentDmlTarget = !string.IsNullOrWhiteSpace(_currentDmlTargetTable) &&
                                      objectName.Equals(_currentDmlTargetTable, StringComparison.OrdinalIgnoreCase);

            if (_tables.Contains(objectName))
            {
                // Only add data flow dependency (read) if it's NOT the table being modified by the parent DML statement
                if (!isCurrentDmlTarget)
                {
                    // Console.WriteLine($"DEBUG: '{objectName}' identified as a TABLE (not DML target). Adding DataFlowDependency: {objectName} -> {_currentNodeName}");
                    AddDataFlowDependency(objectName, NodeType.Table, _currentNodeName);
                }
                else
                {
                    // Console.WriteLine($"DEBUG: '{objectName}' identified as a TABLE, but IS current DML target. Skipping read dependency.");
                }
            }
            else
            {
                var sqlObject = GetSqlObjectByName(objectName);
                if (sqlObject != null && (sqlObject.Type == NodeType.Function || sqlObject.Type == NodeType.Function))
                {
                    // Console.WriteLine($"DEBUG: '{objectName}' identified as a FUNCTION (Type: {sqlObject.Type}). Adding DataFlowDependency: {objectName} -> {_currentNodeName}");
                    AddDataFlowDependency(objectName, sqlObject.Type, _currentNodeName);
                }
                // else: Not a known table or function
            }
        }

        base.Visit(node);
    }

    // Visit SELECT to ensure contained elements are processed
    public override void Visit(SelectStatement node)
    {
        if (string.IsNullOrWhiteSpace(_currentNodeName))
        {
            base.Visit(node);
            return;
        }

        base.Visit(node);
    }

    // --- DML Statements ---
    public override void Visit(InsertStatement node)
    {
        string? originalDmlTarget = _currentDmlTargetTable; // Store previous value (for nesting, though unlikely)
        _currentDmlTargetTable = null; // Reset for this specific statement

        if (string.IsNullOrWhiteSpace(_currentNodeName))
        {
            base.Visit(node);
            return;
        }

        if (node.InsertSpecification?.Target is NamedTableReference namedTable)
        {
            var tableName = namedTable.SchemaObject?.BaseIdentifier?.Value;
            if (!string.IsNullOrWhiteSpace(tableName) && _tables.Contains(tableName))
            {
                AddDataWriteDependency(_currentNodeName, tableName, NodeType.Table);
                _currentDmlTargetTable = tableName; // Set context for children
            }
        }

        try
        {
            base.Visit(node); // Visit children (VALUES, SELECT source)
        }
        finally
        {
            _currentDmlTargetTable = originalDmlTarget; // Restore previous context
        }
    }

    public override void Visit(UpdateStatement node)
    {
        string? originalDmlTarget = _currentDmlTargetTable;
        _currentDmlTargetTable = null;

        if (string.IsNullOrWhiteSpace(_currentNodeName))
        {
            base.Visit(node);
            return;
        }

        if (node.UpdateSpecification?.Target is NamedTableReference namedTable)
        {
            var tableName = namedTable.SchemaObject?.BaseIdentifier?.Value;
            if (!string.IsNullOrWhiteSpace(tableName) && _tables.Contains(tableName))
            {
                AddDataWriteDependency(_currentNodeName, tableName, NodeType.Table);
                _currentDmlTargetTable = tableName; // Set context for children (FROM, WHERE, SET)
            }
        }

        try
        {
            base.Visit(node); // Visit SET, WHERE, FROM etc.
        }
        finally
        {
            _currentDmlTargetTable = originalDmlTarget; // Restore previous context
        }
    }

    public override void Visit(DeleteStatement node)
    {
        string? originalDmlTarget = _currentDmlTargetTable;
        _currentDmlTargetTable = null;

        if (string.IsNullOrWhiteSpace(_currentNodeName))
        {
            base.Visit(node);
            return;
        }

        if (node.DeleteSpecification?.Target is NamedTableReference namedTable)
        {
            var tableName = namedTable.SchemaObject?.BaseIdentifier?.Value;
            if (!string.IsNullOrWhiteSpace(tableName) && _tables.Contains(tableName))
            {
                AddDataWriteDependency(_currentNodeName, tableName, NodeType.Table);
                _currentDmlTargetTable = tableName; // Set context for children (WHERE, FROM)
            }
        }

        try
        {
            base.Visit(node); // Visit WHERE, FROM etc.
        }
        finally
        {
            _currentDmlTargetTable = originalDmlTarget; // Restore previous context
        }
    }

    public override void Visit(MergeStatement node)
    {
        string? originalDmlTarget = _currentDmlTargetTable;
        _currentDmlTargetTable = null;

        if (string.IsNullOrWhiteSpace(_currentNodeName))
        {
            base.Visit(node);
            return;
        }

        if (node.MergeSpecification?.Target is NamedTableReference namedTable)
        {
            var tableName = namedTable.SchemaObject?.BaseIdentifier?.Value;
            if (!string.IsNullOrWhiteSpace(tableName) && _tables.Contains(tableName))
            {
                AddDataWriteDependency(_currentNodeName, tableName, NodeType.Table);
                _currentDmlTargetTable = tableName; // Set context for children (USING, WHEN clauses)
            }
        }

        try
        {
            base.Visit(node); // Visit USING, WHEN MATCHED etc.
        }
        finally
        {
            _currentDmlTargetTable = originalDmlTarget; // Restore previous context
        }
    }

    // EXECUTE statement handling (Caller -> Callee dependency)
    public override void Visit(ExecuteStatement node)
    {
        if (string.IsNullOrWhiteSpace(_currentNodeName))
        {
            base.Visit(node);
            return;
        }

        if (node.ExecuteSpecification?.ExecutableEntity is ExecutableProcedureReference procedureReference)
        {
            var procedureName = procedureReference.ProcedureReference?.ProcedureReference?.Name?.BaseIdentifier?.Value;
            if (!string.IsNullOrWhiteSpace(procedureName))
            {
                HandlePotentialDependency(procedureName);
            }
        }
        else if (node.ExecuteSpecification?.ExecutableEntity is ExecutableStringList stringList)
        {
            foreach (var sqlString in stringList.Strings)
            {
                if (sqlString is ValueExpression valExpr)
                {
                    HandleDynamicSql(valExpr);
                }
            }
        }

        base.Visit(node);
    }

    // --- Helper Methods ---
    private ISqlObject? GetSqlObjectByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _sqlObjects.FirstOrDefault(obj => string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // Handles CALLER -> CALLEE dependencies (e.g., for EXEC)
    private void HandlePotentialDependency(string calleeName)
    {
        if (string.IsNullOrWhiteSpace(_currentNodeName) || string.IsNullOrWhiteSpace(calleeName))
        {
            return;
        }

        var sqlObject = GetSqlObjectByName(calleeName);
        if (sqlObject != null)
        {
            if (!Graph.ContainsKey(calleeName))
            {
                Graph[calleeName] = new DirectedGraphNode(calleeName, sqlObject.Type);
            }

            Graph[_currentNodeName].OutNodes.Add(calleeName);
            Graph[calleeName].InNodes.Add(_currentNodeName);
        }
    }

    // Dynamic SQL parsing (use with caution)
    private void HandleDynamicSql(ValueExpression sqlExpression)
    {
        if (sqlExpression == null || sqlExpression.ScriptTokenStream == null)
        {
            return;
        }

        try
        {
            string dynamicSql = string.Join("", sqlExpression.ScriptTokenStream
                .Skip(sqlExpression.FirstTokenIndex).Take(sqlExpression.LastTokenIndex - sqlExpression.FirstTokenIndex + 1).Select(t => t.Text));
            if (dynamicSql.StartsWith("'") && dynamicSql.EndsWith("'"))
            {
                dynamicSql = dynamicSql.Substring(1, dynamicSql.Length - 2).Replace("''", "'");
            }
            else if (dynamicSql.StartsWith("N'") && dynamicSql.EndsWith("'"))
            {
                dynamicSql = dynamicSql.Substring(2, dynamicSql.Length - 3).Replace("''", "'");
            }

            if (string.IsNullOrWhiteSpace(dynamicSql))
            {
                return;
            }

            using (var reader = new StringReader(dynamicSql))
            {
                var fragment = _dynamicSqlParser.Parse(reader, out var errors);
                if (errors != null && errors.Count > 0)
                {
                    Console.Error.WriteLine($"Errors parsing dynamic SQL within {_currentNodeName}: {string.Join("; ", errors.Select(e => e.Message))}");
                    return;
                }

                if (fragment != null)
                {
                    fragment.Accept(this);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Exception parsing dynamic SQL within {_currentNodeName}: {ex.Message}");
        }
    }
}
