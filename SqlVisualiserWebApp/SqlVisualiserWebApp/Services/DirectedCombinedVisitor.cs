namespace SqlVisualiserWebApp.Services;

using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class DirectedCombinedVisitor : TSqlFragmentVisitor
{
    private ISqlObject? _currentSqlObject = null; // Still needed for context (schema/catalog defaults)
    private string? _currentNodeKey = null; // Store the unique key of the current node
    private readonly ILogger<SqlVisualiserService> _logger;
    private readonly TSql150Parser _dynamicSqlParser;
    private string? _currentDmlTargetTableKey = null;

    // Graph uses OrdinalIgnoreCase for the unique keys ([Catalog].[Schema].[Name])
    public Dictionary<string, DirectedGraphNode> Graph { get; } = new Dictionary<string, DirectedGraphNode>(StringComparer.OrdinalIgnoreCase);

    public DirectedCombinedVisitor(ILogger<SqlVisualiserService> logger)
    {
        this._logger = logger;
        _dynamicSqlParser = new TSql150Parser(false);
    }

    public void SetupGraph(List<ISqlObject> sqlObjects)
    {
        ArgumentNullException.ThrowIfNull(sqlObjects);

        foreach (var obj in sqlObjects)
        {
            if (obj == null || string.IsNullOrWhiteSpace(obj.Name))
            {
                continue;
            }

            obj.Catalog ??= "Unknown";
            obj.Schema ??= "dbo";
            string key = GetUniqueNodeKey(obj);

            if (!Graph.ContainsKey(key))
            {
                Graph.Add(key, new DirectedGraphNode(obj.Name, obj.Type, obj.Catalog));
            }
            else
            {
                // Log a warning for duplicate keys
                _logger.LogWarning($"Warning: Duplicate object key detected during graph setup: {key}");
            }
        }
    }

    // --- Unique Key Generation (remains the same) ---
    private string GetUniqueNodeKey(ISqlObject obj)
    {
        return $"[{obj.Catalog ?? "Unknown"}].[{obj.Schema ?? "dbo"}].[{obj.Name}]";
    }

    private string GetUniqueNodeKey(SchemaObjectName schemaObjectName, string defaultCatalog, string defaultSchema)
    {
        string? catalog = schemaObjectName.DatabaseIdentifier?.Value ?? defaultCatalog;
        string? schema = schemaObjectName.SchemaIdentifier?.Value ?? defaultSchema;
        string? name = schemaObjectName.BaseIdentifier?.Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"[{catalog ?? "Unknown"}].[{schema ?? "Unknown"}].[INVALID_NAME]";
        }

        return $"[{catalog ?? "Unknown"}].[{schema ?? "Unknown"}].[{name}]";
    }

    // --- End Key Generation ---
    public void SetCurrentNode(ISqlObject sqlObject)
    {
        if (sqlObject == null || string.IsNullOrWhiteSpace(sqlObject.Name))
        {
            _logger.LogWarning("Attempted to set current node with null or empty name.");
            _currentSqlObject = null;
            _currentNodeKey = null;
            return;
        }

        _currentSqlObject = sqlObject;
        _currentNodeKey = GetUniqueNodeKey(_currentSqlObject); // Store the key

        // Ensure node exists in graph (should have been added in constructor, but check for safety)
        if (!Graph.ContainsKey(_currentNodeKey))
        {
            _logger.LogWarning($"Warning: Current node key '{_currentNodeKey}' not found in pre-populated graph. Adding it now.");
            Graph[_currentNodeKey] = new DirectedGraphNode(
               _currentSqlObject.Name,
               _currentSqlObject.Type,
               _currentSqlObject.Catalog ?? "Unknown"
           );
        }
    }

    // --- Dependency Helpers (Now only need keys) ---
    private void AddDataFlowDependency(string sourceKey, string consumerKey)
    {
        if (string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(sourceKey))
        {
            return;
        }

        // Check if both nodes exist in the graph before adding edge
        if (Graph.ContainsKey(sourceKey) && Graph.ContainsKey(consumerKey))
        {
            if (!sourceKey.Equals(consumerKey, StringComparison.OrdinalIgnoreCase))
            {
                Graph[sourceKey].OutNodes.Add(consumerKey);
                Graph[consumerKey].InNodes.Add(sourceKey);
            }
        }
        else
        {
            _logger.LogWarning($"Could not add data flow edge. Source Key ('{sourceKey}' exists: {Graph.ContainsKey(sourceKey)}) or Consumer Key ('{consumerKey}' exists: {Graph.ContainsKey(consumerKey)}) not found in graph.");
        }
    }

    private void AddDataWriteDependency(string modifierKey, string targetKey)
    {
        if (string.IsNullOrWhiteSpace(modifierKey) || string.IsNullOrWhiteSpace(targetKey))
        {
            return;
        }

        // Check if both nodes exist and target is a table
        if (Graph.TryGetValue(targetKey, out var targetNode) && targetNode.Type == NodeType.Table && Graph.ContainsKey(modifierKey))
        {
            if (!modifierKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
            {
                Graph[modifierKey].OutNodes.Add(targetKey);
                Graph[targetKey].InNodes.Add(modifierKey);
            }
        }
        else
        {
            _logger.LogWarning($"Could not add data write edge. Modifier Key ('{modifierKey}' exists: {Graph.ContainsKey(modifierKey)}) or Target Key ('{targetKey}' exists and is Table: {Graph.ContainsKey(targetKey) && Graph[targetKey].Type == NodeType.Table}) condition not met.");
        }
    }

    private void HandlePotentialDependency(string callerKey, string calleeKey)
    {
        if (string.IsNullOrWhiteSpace(callerKey) || string.IsNullOrWhiteSpace(calleeKey))
        {
            return;
        }

        // Check if both nodes exist
        if (Graph.ContainsKey(callerKey) && Graph.ContainsKey(calleeKey))
        {
            if (!callerKey.Equals(calleeKey, StringComparison.OrdinalIgnoreCase))
            {
                Graph[callerKey].OutNodes.Add(calleeKey);
                Graph[calleeKey].InNodes.Add(callerKey);
            }
        }
        else
        {
            _logger.LogWarning($"Could not add potential dependency edge. Caller Key ('{callerKey}' exists: {Graph.ContainsKey(callerKey)}) or Callee Key ('{calleeKey}' exists: {Graph.ContainsKey(calleeKey)}) not found in graph.");
        }
    }
    // --- End Dependency Helpers ---

    // --- Visit Methods (Updated to use Graph.ContainsKey and pass keys to helpers) ---

    public override void Visit(FunctionCall node)
    {
        if (_currentSqlObject == null || _currentNodeKey == null || node.FunctionName == null)
        {
            base.Visit(node);
            return;
        }

        var tempSchemaObjName = new SchemaObjectName();
        tempSchemaObjName.Identifiers.Add(node.FunctionName);
        // Resolve key using current context
        string functionKey = GetUniqueNodeKey(tempSchemaObjName, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");

        // Check if the resolved key corresponds to a known function in the graph
        if (Graph.TryGetValue(functionKey, out var graphNode) && (graphNode.Type == NodeType.Function))
        {
            AddDataFlowDependency(functionKey, _currentNodeKey);
        }

        base.Visit(node);
    }

    public override void Visit(SchemaObjectFunctionTableReference node)
    {
        if (_currentSqlObject == null || _currentNodeKey == null || node.SchemaObject == null)
        {
            base.Visit(node);
            return;
        }

        // Resolve key using current context
        string functionKey = GetUniqueNodeKey(node.SchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");

        if (Graph.TryGetValue(functionKey, out var graphNode) && (graphNode.Type == NodeType.Function))
        {
            AddDataFlowDependency(functionKey, _currentNodeKey);
        }

        base.Visit(node);
    }

    public override void Visit(NamedTableReference node)
    {
        if (_currentSqlObject == null || _currentNodeKey == null || node.SchemaObject == null)
        {
            base.Visit(node);
            return;
        }

        // Resolve key using current context
        string referencedKey = GetUniqueNodeKey(node.SchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");

        if (Graph.TryGetValue(referencedKey, out var referencedNode)) // Check if the object exists in our graph
        {
            bool isCurrentDmlTarget = !string.IsNullOrWhiteSpace(_currentDmlTargetTableKey) &&
                                      referencedKey.Equals(_currentDmlTargetTableKey, StringComparison.OrdinalIgnoreCase);

            if (referencedNode.Type == NodeType.Table && !isCurrentDmlTarget)
            {
                AddDataFlowDependency(referencedKey, _currentNodeKey);
            }
            else if (referencedNode.Type == NodeType.Function)
            {
                // TVFs used like tables always flow data to consumer
                AddDataFlowDependency(referencedKey, _currentNodeKey);
            }
            // Add logic for Views if needed
        }

        base.Visit(node);
    }

    public override void Visit(SelectStatement node)
    {
        if (_currentSqlObject == null)
        {
            base.Visit(node);
            return;
        }

        base.Visit(node);
    }

    public override void Visit(InsertStatement node)
    {
        string? originalDmlTargetKey = _currentDmlTargetTableKey;
        _currentDmlTargetTableKey = null;
        if (_currentSqlObject == null || _currentNodeKey == null)
        {
            base.Visit(node);
            return;
        }

        if (node.InsertSpecification?.Target is NamedTableReference namedTable && namedTable.SchemaObject != null)
        {
            // Resolve key using current context
            string targetKey = GetUniqueNodeKey(namedTable.SchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
            if (Graph.TryGetValue(targetKey, out var targetNode) && targetNode.Type == NodeType.Table)
            {
                AddDataWriteDependency(_currentNodeKey, targetKey);
                _currentDmlTargetTableKey = targetKey;
            }
        }

        try
        {
            base.Visit(node);
        }
        finally
        {
            _currentDmlTargetTableKey = originalDmlTargetKey;
        }
    }

    public override void Visit(UpdateStatement node)
    {
        string? originalDmlTargetKey = _currentDmlTargetTableKey;
        _currentDmlTargetTableKey = null;
        if (_currentSqlObject == null || _currentNodeKey == null)
        {
            base.Visit(node);
            return;
        }

        if (node.UpdateSpecification?.Target is NamedTableReference namedTable && namedTable.SchemaObject != null)
        {
            // Resolve key using current context
            string targetKey = GetUniqueNodeKey(namedTable.SchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
            if (Graph.TryGetValue(targetKey, out var targetNode) && targetNode.Type == NodeType.Table)
            {
                AddDataWriteDependency(_currentNodeKey, targetKey);
                _currentDmlTargetTableKey = targetKey;
            }
        }

        try
        {
            base.Visit(node);
        }
        finally
        {
            _currentDmlTargetTableKey = originalDmlTargetKey;
        }
    }

    public override void Visit(DeleteStatement node)
    {
        string? originalDmlTargetKey = _currentDmlTargetTableKey;
        _currentDmlTargetTableKey = null;
        if (_currentSqlObject == null || _currentNodeKey == null)
        {
            base.Visit(node);
            return;
        }

        if (node.DeleteSpecification?.Target is NamedTableReference namedTable && namedTable.SchemaObject != null)
        {
            // Resolve key using current context
            string targetKey = GetUniqueNodeKey(namedTable.SchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
            if (Graph.TryGetValue(targetKey, out var targetNode) && targetNode.Type == NodeType.Table)
            {
                AddDataWriteDependency(_currentNodeKey, targetKey);
                _currentDmlTargetTableKey = targetKey;
            }
        }

        try
        {
            base.Visit(node);
        }
        finally
        {
            _currentDmlTargetTableKey = originalDmlTargetKey;
        }
    }

    public override void Visit(MergeStatement node)
    {
        string? originalDmlTargetKey = _currentDmlTargetTableKey;
        _currentDmlTargetTableKey = null;
        if (_currentSqlObject == null || _currentNodeKey == null)
        {
            base.Visit(node);
            return;
        }

        if (node.MergeSpecification?.Target is NamedTableReference namedTable && namedTable.SchemaObject != null)
        {
            // Resolve key using current context
            string targetKey = GetUniqueNodeKey(namedTable.SchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
            if (Graph.TryGetValue(targetKey, out var targetNode) && targetNode.Type == NodeType.Table)
            {
                AddDataWriteDependency(_currentNodeKey, targetKey);
                _currentDmlTargetTableKey = targetKey;
            }
        }

        try
        {
            base.Visit(node);
        }
        finally
        {
            _currentDmlTargetTableKey = originalDmlTargetKey;
        }
    }

    public override void Visit(ExecuteStatement node)
    {
        if (_currentSqlObject == null || _currentNodeKey == null)
        {
            base.Visit(node);
            return;
        }

        if (node.ExecuteSpecification?.ExecutableEntity is ExecutableProcedureReference procedureReference)
        {
            if (procedureReference.ProcedureReference?.ProcedureReference?.Name != null)
            {
                // Resolve key using current context
                string calleeKey = GetUniqueNodeKey(procedureReference.ProcedureReference.ProcedureReference.Name, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
                // Check if the resolved callee exists in the graph
                if (Graph.ContainsKey(calleeKey))
                {
                    HandlePotentialDependency(_currentNodeKey, calleeKey); // Pass keys
                }
                else
                {
                    _logger.LogWarning($"Could not resolve EXEC target '{procedureReference.ProcedureReference.ProcedureReference.Name.BaseIdentifier.Value}' with current context to a known object key '{calleeKey}'.");
                }
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

    // Dynamic SQL parsing (use with caution)
    private void HandleDynamicSql(ValueExpression sqlExpression)
    {
        if (_currentSqlObject == null)
        {
            return;
        }

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
                    _logger.LogWarning($"Errors parsing dynamic SQL within {GetUniqueNodeKey(_currentSqlObject)}: {string.Join("; ", errors.Select(e => e.Message))}");
                    return;
                }

                if (fragment != null)
                {
                    fragment.Accept(this);
                } // Recursive call maintains _currentSqlObject context
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Exception parsing dynamic SQL within {GetUniqueNodeKey(_currentSqlObject)}: {ex.Message}");
        }
    }
}
