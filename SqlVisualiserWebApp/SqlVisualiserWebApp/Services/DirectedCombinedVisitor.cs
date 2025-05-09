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
using Microsoft.Extensions.Logging; // Assuming ILogger is used

public class DirectedCombinedVisitor : TSqlFragmentVisitor
{
    private ISqlObject? _currentSqlObject = null;
    private string? _currentNodeKey = null;
    private readonly ILogger<SqlVisualiserService> _logger;
    private readonly TSql150Parser _dynamicSqlParser;
    private string? _currentDmlTargetTableKey = null;

    public Dictionary<string, DirectedGraphNode> Graph { get; } = new Dictionary<string, DirectedGraphNode>(StringComparer.OrdinalIgnoreCase);

    public DirectedCombinedVisitor(ILogger<SqlVisualiserService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dynamicSqlParser = new TSql150Parser(false);
    }

    public void SetupGraph(List<ISqlObject> sqlObjects)
    {
        var initialObjects = sqlObjects ?? throw new ArgumentNullException(nameof(sqlObjects));
        foreach (var obj in initialObjects)
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
                _logger.LogWarning($"Warning: Duplicate object key detected during initial graph population: {key}");
            }
        }
    }

    // --- Unique Key Generation ---
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
        _currentNodeKey = GetUniqueNodeKey(_currentSqlObject);

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

    // --- Dependency Helpers ---
    private void AddDataFlowDependency(string sourceKey, string consumerKey)
    {
        if (string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(sourceKey))
        {
            return;
        }

        if (Graph.ContainsKey(sourceKey) && Graph.ContainsKey(consumerKey))
        {
            if (!sourceKey.Equals(consumerKey, StringComparison.OrdinalIgnoreCase))
            {
                 Graph[sourceKey].OutNodes.Add(consumerKey);
                 Graph[consumerKey].InNodes.Add(sourceKey);
            }
        } else
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

        if (Graph.TryGetValue(targetKey, out var targetNode) && targetNode.Type == NodeType.Table && Graph.ContainsKey(modifierKey))
         {
            if (!modifierKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
            {
                Graph[modifierKey].OutNodes.Add(targetKey);
                Graph[targetKey].InNodes.Add(modifierKey);
            }
         } else
        {
            _logger.LogWarning($"Could not add data write edge. Modifier Key ('{modifierKey}' exists: {Graph.ContainsKey(modifierKey)}) or Target Key ('{targetKey}' exists and is Table: {Graph.ContainsKey(targetKey) && Graph.TryGetValue(targetKey, out var node) && node.Type == NodeType.Table}) condition not met.");
        }
    }

    private void HandlePotentialDependency(string callerKey, string calleeKey)
    {
        if (string.IsNullOrWhiteSpace(callerKey) || string.IsNullOrWhiteSpace(calleeKey))
        {
            return;
        }

        if (Graph.ContainsKey(callerKey) && Graph.ContainsKey(calleeKey))
        {
            if (!callerKey.Equals(calleeKey, StringComparison.OrdinalIgnoreCase))
            {
                Graph[callerKey].OutNodes.Add(calleeKey);
                Graph[calleeKey].InNodes.Add(callerKey);
            }
        } else
        {
            _logger.LogWarning($"Could not add potential dependency edge. Caller Key ('{callerKey}' exists: {Graph.ContainsKey(callerKey)}) or Callee Key ('{calleeKey}' exists: {Graph.ContainsKey(calleeKey)}) not found in graph.");
        }
    }
    // --- End Dependency Helpers ---

    // --- Visit Methods ---
    public override void Visit(FunctionCall node)
    {
        if (_currentSqlObject == null || _currentNodeKey == null || node.FunctionName == null)
        {
            base.Visit(node);
            return;
        }

        var tempSchemaObjName = new SchemaObjectName();
        tempSchemaObjName.Identifiers.Add(node.FunctionName);
        string functionKey = GetUniqueNodeKey(tempSchemaObjName, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
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

        string referencedKey = GetUniqueNodeKey(node.SchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");

        if (Graph.TryGetValue(referencedKey, out var referencedNode))
        {
            bool isCurrentDmlTarget = !string.IsNullOrWhiteSpace(_currentDmlTargetTableKey) &&
                                      referencedKey.Equals(_currentDmlTargetTableKey, StringComparison.OrdinalIgnoreCase);

            if (referencedNode.Type == NodeType.Table && !isCurrentDmlTarget)
            {
                AddDataFlowDependency(referencedKey, _currentNodeKey);
            }
            else if (referencedNode.Type == NodeType.Function)
            {
                 AddDataFlowDependency(referencedKey, _currentNodeKey);
            }
        }

        base.Visit(node);
    }

    public override void Visit(SelectStatement node)
    {
        if (_currentSqlObject == null)
        {
            base.Visit(node);
            return; }

        base.Visit(node);
    }

    // --- DML Statements (Updated to resolve target alias) ---
    private SchemaObjectName? ResolveDmlTargetAlias(NamedTableReference targetAliasNode, FromClause? fromClause)
    {
        if (targetAliasNode.SchemaObject == null)
        {
            return null; // Alias node itself must have a schema object (even if just BaseIdentifier)
        }

        string aliasToFind = targetAliasNode.SchemaObject.BaseIdentifier.Value;
        if (string.IsNullOrWhiteSpace(aliasToFind))
        {
            return null;
        }

        _logger.LogTrace($"ResolveDmlTargetAlias: Attempting to resolve alias '{aliasToFind}' for DML target.");

        if (fromClause == null)
        {
            _logger.LogTrace($"ResolveDmlTargetAlias: No FROM clause provided for alias '{aliasToFind}'. Returning original target node.");
            return targetAliasNode.SchemaObject; // Cannot resolve without a FROM clause to check
        }

        SchemaObjectName? FindAliasedObjectInTableRef(TableReference tableRef)
        {
            if (tableRef is NamedTableReference namedRef)
            {
                // _logger.LogTrace($"ResolveDmlTargetAlias (FindAliasedObjectInTableRef): Checking NamedTableReference '{namedRef.SchemaObject?.BaseIdentifier?.Value}' with alias '{namedRef.Alias?.Value}'.");
                if (namedRef.Alias != null && namedRef.Alias.Value.Equals(aliasToFind, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogTrace($"ResolveDmlTargetAlias: Found alias '{aliasToFind}' pointing to '{namedRef.SchemaObject?.BaseIdentifier?.Value}'.");
                    return namedRef.SchemaObject;
                }
            }
            else if (tableRef is QualifiedJoin qualifiedJoin)
            {
                // _logger.LogTrace("ResolveDmlTargetAlias (FindAliasedObjectInTableRef): Recursing into QualifiedJoin.");
                var firstResult = FindAliasedObjectInTableRef(qualifiedJoin.FirstTableReference);
                if (firstResult != null)
                {
                    return firstResult;
                }

                var secondResult = FindAliasedObjectInTableRef(qualifiedJoin.SecondTableReference);
                if (secondResult != null)
                {
                    return secondResult;
                }
            }
            // Add other TableReference types if needed (e.g., PivotedTableReference, UnpivotedTableReference, OpenRowset...)
            return null;
        }

        foreach (var tableReference in fromClause.TableReferences)
        {
            var resolvedSchemaObject = FindAliasedObjectInTableRef(tableReference);
            if (resolvedSchemaObject != null)
            {
                return resolvedSchemaObject;
            }
        }

        _logger.LogTrace($"ResolveDmlTargetAlias: Alias '{aliasToFind}' not found in FROM clause. Returning original target node.");
        return targetAliasNode.SchemaObject; // Fallback: if alias not found in FROM, assume the target itself is the object name
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
            // INSERT target is typically not aliased via a FROM clause in the same statement.
            // The SchemaObject of the NamedTableReference should be the actual table.
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

        if (node.UpdateSpecification?.Target is NamedTableReference targetRefNode && targetRefNode.SchemaObject != null)
        {
            SchemaObjectName actualTargetSchemaObject = targetRefNode.SchemaObject;
            // Only try to resolve alias if the target is a simple, unqualified identifier
            if (targetRefNode.SchemaObject.DatabaseIdentifier == null && targetRefNode.SchemaObject.SchemaIdentifier == null)
            {
                 actualTargetSchemaObject = ResolveDmlTargetAlias(targetRefNode, node.UpdateSpecification.FromClause) ?? targetRefNode.SchemaObject;
            }

            string targetKey = GetUniqueNodeKey(actualTargetSchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
            if (Graph.TryGetValue(targetKey, out var targetGraphNode) && targetGraphNode.Type == NodeType.Table)
            {
                 AddDataWriteDependency(_currentNodeKey, targetKey);
                 _currentDmlTargetTableKey = targetKey;
            } else
            {
                _logger.LogWarning($"UPDATE target '{actualTargetSchemaObject.BaseIdentifier.Value}' (resolved to key '{targetKey}') not found in graph or not a table for current node {_currentNodeKey}.");
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

        if (node.DeleteSpecification?.Target is NamedTableReference targetRefNode && targetRefNode.SchemaObject != null)
        {
            SchemaObjectName actualTargetSchemaObject = targetRefNode.SchemaObject;
            if (targetRefNode.SchemaObject.DatabaseIdentifier == null && targetRefNode.SchemaObject.SchemaIdentifier == null)
            {
                 actualTargetSchemaObject = ResolveDmlTargetAlias(targetRefNode, node.DeleteSpecification.FromClause) ?? targetRefNode.SchemaObject;
            }

            string targetKey = GetUniqueNodeKey(actualTargetSchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
             if (Graph.TryGetValue(targetKey, out var targetGraphNode) && targetGraphNode.Type == NodeType.Table)
            {
                 AddDataWriteDependency(_currentNodeKey, targetKey);
                 _currentDmlTargetTableKey = targetKey;
            } else
            {
                _logger.LogWarning($"DELETE target '{actualTargetSchemaObject.BaseIdentifier.Value}' (resolved to key '{targetKey}') not found in graph or not a table for current node {_currentNodeKey}.");
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

        if (node.MergeSpecification?.Target is NamedTableReference targetRefNode && targetRefNode.SchemaObject != null)
        {
            // For MERGE, the targetRefNode.SchemaObject IS the actual table, even if aliased in the MERGE statement itself.
            // The alias is part of the targetRefNode, but SchemaObject refers to the base.
            // ResolveDmlTargetAlias might be overly complex here if the USING clause is passed.
            SchemaObjectName actualTargetSchemaObject = targetRefNode.SchemaObject;

            string targetKey = GetUniqueNodeKey(actualTargetSchemaObject, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
            if (Graph.TryGetValue(targetKey, out var targetGraphNode) && targetGraphNode.Type == NodeType.Table)
            {
                 AddDataWriteDependency(_currentNodeKey, targetKey);
                 _currentDmlTargetTableKey = targetKey;
            } else
            {
                _logger.LogWarning($"MERGE target '{actualTargetSchemaObject.BaseIdentifier.Value}' (resolved to key '{targetKey}') not found in graph or not a table for current node {_currentNodeKey}.");
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
            if(procedureReference.ProcedureReference?.ProcedureReference?.Name != null)
            {
                 string calleeKey = GetUniqueNodeKey(procedureReference.ProcedureReference.ProcedureReference?.Name, _currentSqlObject.Catalog ?? "Unknown", _currentSqlObject.Schema ?? "dbo");
                 if(Graph.ContainsKey(calleeKey))
                {
                    HandlePotentialDependency(_currentNodeKey, calleeKey);
                }
                else
                {
                    _logger.LogWarning($"Could not resolve EXEC target '{procedureReference.ProcedureReference.ProcedureReference?.Name.BaseIdentifier.Value}' with current context to a known object key '{calleeKey}'.");
                }
            }
        }
        else if (node.ExecuteSpecification?.ExecutableEntity is ExecutableStringList stringList)
        {
             foreach (var sqlString in stringList.Strings)
            {
                if (sqlString is ValueExpression valExpr)
                {
                    HandleDynamicSql(valExpr); }
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

        string currentContextKey = GetUniqueNodeKey(_currentSqlObject);
        try
        {
            string dynamicSql = string.Join("", sqlExpression.ScriptTokenStream .Skip(sqlExpression.FirstTokenIndex).Take(sqlExpression.LastTokenIndex - sqlExpression.FirstTokenIndex + 1).Select(t => t.Text));
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
                    _logger.LogWarning($"Errors parsing dynamic SQL within {currentContextKey}: {string.Join("; ", errors.Select(e => e.Message))}");
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
            _logger.LogError(ex, $"Exception parsing dynamic SQL within {currentContextKey}. SQL: {sqlExpression.ToString() ?? "NULL"}");
        }
    }
}
