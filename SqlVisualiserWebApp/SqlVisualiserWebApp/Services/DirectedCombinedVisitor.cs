namespace SqlVisualiserWebApp.Services;

using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text; // For StringBuilder

public class DirectedCombinedVisitor : TSqlFragmentVisitor
{
    private readonly List<ISqlObject> _sqlObjects; // Unified list
    private ISqlObject? _currentSqlObject = null; // Store the full object for context
    private readonly TSql150Parser _dynamicSqlParser;
    private string? _currentDmlTargetTableKey = null; // Tracks the *unique key* of the table being modified

    // Graph uses OrdinalIgnoreCase for the unique keys ([Catalog].[Schema].[Name])
    public Dictionary<string, DirectedGraphNode> Graph { get; } = new Dictionary<string, DirectedGraphNode>(StringComparer.OrdinalIgnoreCase);

    public DirectedCombinedVisitor(List<ISqlObject> sqlObjects)
    {
        _sqlObjects = sqlObjects ?? throw new ArgumentNullException(nameof(sqlObjects));
        _dynamicSqlParser = new TSql150Parser(false);
    }

    // --- Unique Key Generation ---
    private string GetUniqueNodeKey(ISqlObject obj)
    {
        // Use canonical properties from the ISqlObject
        return $"[{obj.Catalog ?? "Unknown"}].[{obj.Schema ?? "dbo"}].[{obj.Name}]";
    }

    // Overload to generate key from parsed SchemaObjectName, using current context for defaults
    private string GetUniqueNodeKey(SchemaObjectName schemaObjectName, string defaultCatalog, string defaultSchema)
    {
        string? catalog = schemaObjectName.DatabaseIdentifier?.Value ?? defaultCatalog;
        string? schema = schemaObjectName.SchemaIdentifier?.Value ?? defaultSchema;
        string? name = schemaObjectName.BaseIdentifier?.Value;

        if (string.IsNullOrWhiteSpace(name))
        {
            // Should not happen for valid references, but handle defensively
            return $"[{catalog ?? "Unknown"}].[{schema ?? "Unknown"}].[INVALID_NAME]";
        }
        return $"[{catalog ?? "Unknown"}].[{schema ?? "Unknown"}].[{name}]";
    }

    // Find ISqlObject based on a potentially qualified name from SQL, using current context
    private ISqlObject? ResolveSqlObject(SchemaObjectName schemaObjectName)
    {
        if (_currentSqlObject == null) return null; // Need context

        string? catalog = schemaObjectName.DatabaseIdentifier?.Value ?? _currentSqlObject.Catalog;
        string? schema = schemaObjectName.SchemaIdentifier?.Value ?? _currentSqlObject.Schema; // Or maybe always default to dbo if schema missing? Decide rule.
        string? name = schemaObjectName.BaseIdentifier?.Value;

        if (string.IsNullOrWhiteSpace(name)) return null;

        // Find the best match based on qualification provided
        return _sqlObjects.FirstOrDefault(obj =>
            string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(obj.Schema, schema, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(obj.Catalog, catalog, StringComparison.OrdinalIgnoreCase)
        );
    }
    // --- End Key Generation ---


    public void SetCurrentNode(ISqlObject sqlObject)
    {
        if (sqlObject == null || string.IsNullOrWhiteSpace(sqlObject.Name))
        {
            Console.Error.WriteLine("Attempted to set current node with null or empty name.");
            _currentSqlObject = null;
            return;
        }
        _currentSqlObject = sqlObject; // Store the full object
        string uniqueKey = GetUniqueNodeKey(_currentSqlObject);

        if (!Graph.ContainsKey(uniqueKey))
        {
            // Create node using canonical info from ISqlObject
            Graph[uniqueKey] = new DirectedGraphNode(
                _currentSqlObject.Name,
                _currentSqlObject.Type,
                _currentSqlObject.Catalog ?? "Unknown" // Use canonical catalog
            );
        }
    }

    // Helper: Data flows FROM source TO consumer
    // Uses unique keys for graph operations
    private void AddDataFlowDependency(string sourceUniqueKey, ISqlObject sourceObject, string consumerUniqueKey, ISqlObject consumerObject)
    {
        if (string.IsNullOrWhiteSpace(consumerUniqueKey) || string.IsNullOrWhiteSpace(sourceUniqueKey)) return;

        // Ensure nodes exist in graph using unique keys and canonical info
        if (!Graph.ContainsKey(consumerUniqueKey)) { Graph[consumerUniqueKey] = new DirectedGraphNode(consumerObject.Name, consumerObject.Type, consumerObject.Catalog ?? "Unknown"); }
        if (!Graph.ContainsKey(sourceUniqueKey)) { Graph[sourceUniqueKey] = new DirectedGraphNode(sourceObject.Name, sourceObject.Type, sourceObject.Catalog ?? "Unknown"); }

        // Add edge using unique keys, avoid self-loops
        if (!sourceUniqueKey.Equals(consumerUniqueKey, StringComparison.OrdinalIgnoreCase))
        {
             Graph[sourceUniqueKey].OutNodes.Add(consumerUniqueKey);
             Graph[consumerUniqueKey].InNodes.Add(sourceUniqueKey);
        }
    }

    // Helper: Data is written/modified FROM modifier TO target
    // Uses unique keys for graph operations
    private void AddDataWriteDependency(string modifierUniqueKey, ISqlObject modifierObject, string targetUniqueKey, ISqlObject targetObject)
    {
         if (string.IsNullOrWhiteSpace(modifierUniqueKey) || string.IsNullOrWhiteSpace(targetUniqueKey)) return;
         if (targetObject.Type != NodeType.Table) { Console.Error.WriteLine($"Write target '{targetObject.Name}' is not a Table."); return; } // Ensure target is a table

        // Ensure nodes exist
        if (!Graph.ContainsKey(modifierUniqueKey)) { Graph[modifierUniqueKey] = new DirectedGraphNode(modifierObject.Name, modifierObject.Type, modifierObject.Catalog ?? "Unknown"); }
        if (!Graph.ContainsKey(targetUniqueKey)) { Graph[targetUniqueKey] = new DirectedGraphNode(targetObject.Name, targetObject.Type, targetObject.Catalog ?? "Unknown"); }

        // Add edge using unique keys, avoid self-loops
        if (!modifierUniqueKey.Equals(targetUniqueKey, StringComparison.OrdinalIgnoreCase))
        {
             Console.WriteLine($"DEBUG: Adding Data Write Edge: {modifierUniqueKey} -> {targetUniqueKey}"); // Log edge add
            Graph[modifierUniqueKey].OutNodes.Add(targetUniqueKey);
            // *** TYPO WAS HERE *** It was adding targetUniqueKey to InNodes instead of modifierUniqueKey
            Graph[targetUniqueKey].InNodes.Add(modifierUniqueKey); // Corrected
        } else {
             Console.WriteLine($"DEBUG: Skipping self-loop Data Write Edge: {modifierUniqueKey} -> {targetUniqueKey}");
        }
    }


    // Catch SCALAR function calls
    public override void Visit(FunctionCall node)
    {
        if (_currentSqlObject == null) { base.Visit(node); return; } // Need current object context

        var functionNameNode = node.FunctionName; // This is an Identifier
        if (functionNameNode != null && !string.IsNullOrWhiteSpace(functionNameNode.Value))
        {
            // Construct a temporary SchemaObjectName to resolve the function
            // This assumes scalar functions aren't typically schema/db qualified inline,
            // but resolution might need enhancement if they are.
            var tempSchemaObjName = new SchemaObjectName();
            tempSchemaObjName.Identifiers.Add(functionNameNode);
            var resolvedFunction = ResolveSqlObject(tempSchemaObjName);

            if (resolvedFunction != null && (resolvedFunction.Type == NodeType.Function))
            {
                string sourceKey = GetUniqueNodeKey(resolvedFunction);
                string consumerKey = GetUniqueNodeKey(_currentSqlObject);
                AddDataFlowDependency(sourceKey, resolvedFunction, consumerKey, _currentSqlObject);
            }
        }
        base.Visit(node);
    }

    // Catch TABLE-VALUED function calls in FROM/JOIN clauses
    public override void Visit(SchemaObjectFunctionTableReference node)
    {
        if (_currentSqlObject == null || node.SchemaObject == null) { base.Visit(node); return; }

        var resolvedFunction = ResolveSqlObject(node.SchemaObject);

        if (resolvedFunction != null && (resolvedFunction.Type == NodeType.Function))
        {
            string sourceKey = GetUniqueNodeKey(resolvedFunction);
            string consumerKey = GetUniqueNodeKey(_currentSqlObject);
            AddDataFlowDependency(sourceKey, resolvedFunction, consumerKey, _currentSqlObject);
        }
        base.Visit(node);
    }


    // Handles references to TABLES, VIEWS, or FUNCTIONS referenced with table-like syntax
    public override void Visit(NamedTableReference node)
    {
         if (_currentSqlObject == null || node.SchemaObject == null) { base.Visit(node); return; }

        var referencedObject = ResolveSqlObject(node.SchemaObject);

        if (referencedObject != null)
        {
            string uniqueKey = GetUniqueNodeKey(referencedObject);
            bool isCurrentDmlTarget = !string.IsNullOrWhiteSpace(_currentDmlTargetTableKey) &&
                                      uniqueKey.Equals(_currentDmlTargetTableKey, StringComparison.OrdinalIgnoreCase);

            if (referencedObject.Type == NodeType.Table && !isCurrentDmlTarget)
            {
                string consumerKey = GetUniqueNodeKey(_currentSqlObject);
                AddDataFlowDependency(uniqueKey, referencedObject, consumerKey, _currentSqlObject);
            }
            else if (referencedObject.Type == NodeType.Function)
            {
                 // TVFs used like tables always flow data to consumer
                 string consumerKey = GetUniqueNodeKey(_currentSqlObject);
                 AddDataFlowDependency(uniqueKey, referencedObject, consumerKey, _currentSqlObject);
            }
            // Add logic for Views if needed
        }
        base.Visit(node);
    }

    // Visit SELECT to ensure contained elements are processed
    public override void Visit(SelectStatement node)
    {
        if (_currentSqlObject == null) { base.Visit(node); return; }
        base.Visit(node);
    }

    // --- DML Statements ---
    // Updated to use unique keys and store target key
    public override void Visit(InsertStatement node)
    {
        string? originalDmlTargetKey = _currentDmlTargetTableKey;
        _currentDmlTargetTableKey = null;

        if (_currentSqlObject == null) { base.Visit(node); return; }
        if (node.InsertSpecification?.Target is NamedTableReference namedTable && namedTable.SchemaObject != null)
        {
            var targetObject = ResolveSqlObject(namedTable.SchemaObject);
            if (targetObject != null && targetObject.Type == NodeType.Table)
            {
                string targetKey = GetUniqueNodeKey(targetObject);
                string modifierKey = GetUniqueNodeKey(_currentSqlObject);
                AddDataWriteDependency(modifierKey, _currentSqlObject, targetKey, targetObject);
                _currentDmlTargetTableKey = targetKey; // Store the unique key
            }
        }
        try { base.Visit(node); }
        finally { _currentDmlTargetTableKey = originalDmlTargetKey; }
    }

    public override void Visit(UpdateStatement node)
    {
        string? originalDmlTargetKey = _currentDmlTargetTableKey;
        _currentDmlTargetTableKey = null;

        if (_currentSqlObject == null) { base.Visit(node); return; }
        if (node.UpdateSpecification?.Target is NamedTableReference namedTable && namedTable.SchemaObject != null)
        {
            var targetObject = ResolveSqlObject(namedTable.SchemaObject);
            if (targetObject != null && targetObject.Type == NodeType.Table)
            {
                 string targetKey = GetUniqueNodeKey(targetObject);
                 string modifierKey = GetUniqueNodeKey(_currentSqlObject);
                 AddDataWriteDependency(modifierKey, _currentSqlObject, targetKey, targetObject);
                 _currentDmlTargetTableKey = targetKey;
            }
        }
        try { base.Visit(node); }
        finally { _currentDmlTargetTableKey = originalDmlTargetKey; }
    }

    public override void Visit(DeleteStatement node)
    {
        string? originalDmlTargetKey = _currentDmlTargetTableKey;
        _currentDmlTargetTableKey = null;

         if (_currentSqlObject == null) { base.Visit(node); return; }
        if (node.DeleteSpecification?.Target is NamedTableReference namedTable && namedTable.SchemaObject != null)
        {
            var targetObject = ResolveSqlObject(namedTable.SchemaObject);
            if (targetObject != null && targetObject.Type == NodeType.Table)
            {
                 string targetKey = GetUniqueNodeKey(targetObject);
                 string modifierKey = GetUniqueNodeKey(_currentSqlObject);
                 AddDataWriteDependency(modifierKey, _currentSqlObject, targetKey, targetObject);
                 _currentDmlTargetTableKey = targetKey;
            }
        }
        try { base.Visit(node); }
        finally { _currentDmlTargetTableKey = originalDmlTargetKey; }
    }

    public override void Visit(MergeStatement node)
    {
        string? originalDmlTargetKey = _currentDmlTargetTableKey;
        _currentDmlTargetTableKey = null;

        if (_currentSqlObject == null) { base.Visit(node); return; }
        if (node.MergeSpecification?.Target is NamedTableReference namedTable && namedTable.SchemaObject != null)
        {
            var targetObject = ResolveSqlObject(namedTable.SchemaObject);
            if (targetObject != null && targetObject.Type == NodeType.Table)
            {
                 string targetKey = GetUniqueNodeKey(targetObject);
                 string modifierKey = GetUniqueNodeKey(_currentSqlObject);
                 AddDataWriteDependency(modifierKey, _currentSqlObject, targetKey, targetObject);
                 _currentDmlTargetTableKey = targetKey;
            }
        }
        try { base.Visit(node); }
        finally { _currentDmlTargetTableKey = originalDmlTargetKey; }
    }

    // EXECUTE statement handling (Caller -> Callee dependency)
    public override void Visit(ExecuteStatement node)
    {
        if (_currentSqlObject == null) { base.Visit(node); return; }
        if (node.ExecuteSpecification?.ExecutableEntity is ExecutableProcedureReference procedureReference)
        {
            // ProcedureReference.ProcedureReference is the SchemaObjectName
            if(procedureReference.ProcedureReference?.ProcedureReference != null)
            {
                HandlePotentialDependency(procedureReference.ProcedureReference.ProcedureReference.Name); // Pass SchemaObjectName
            }
        }
        else if (node.ExecuteSpecification?.ExecutableEntity is ExecutableStringList stringList)
        {
             foreach (var sqlString in stringList.Strings)
             {
                 if (sqlString is ValueExpression valExpr) { HandleDynamicSql(valExpr); }
             }
        }
        base.Visit(node);
    }

    // --- Helper Methods ---

    // Handles CALLER -> CALLEE dependencies (e.g., for EXEC)
    // *** UPDATED: Use unique keys and ISqlObject context ***
    private void HandlePotentialDependency(SchemaObjectName calleeSchemaObjectName)
    {
        if (_currentSqlObject == null) return;

        var callerObject = _currentSqlObject; // Already have this
        var calleeObject = ResolveSqlObject(calleeSchemaObjectName); // Resolve based on SQL name + context

        if (callerObject == null || string.IsNullOrWhiteSpace(callerObject.Name)) { /* Log error */ return; }
        if (calleeObject == null || string.IsNullOrWhiteSpace(calleeObject.Name)) { /* Log error or ignore if not found */ return; }

        string callerKey = GetUniqueNodeKey(callerObject);
        string calleeKey = GetUniqueNodeKey(calleeObject);

        // Ensure nodes exist
        if (!Graph.ContainsKey(callerKey)) { Graph[callerKey] = new DirectedGraphNode(callerObject.Name, callerObject.Type, callerObject.Catalog ?? "Unknown"); }
        if (!Graph.ContainsKey(calleeKey)) { Graph[calleeKey] = new DirectedGraphNode(calleeObject.Name, calleeObject.Type, calleeObject.Catalog ?? "Unknown"); }

        // Add edge using unique keys, avoid self-calls
        if (!callerKey.Equals(calleeKey, StringComparison.OrdinalIgnoreCase))
        {
            Graph[callerKey].OutNodes.Add(calleeKey);
            Graph[calleeKey].InNodes.Add(callerKey);
        }
    }

    // Dynamic SQL parsing (use with caution)
    private void HandleDynamicSql(ValueExpression sqlExpression)
    {
        if (_currentSqlObject == null) return; // Need context for recursive calls
        if (sqlExpression == null || sqlExpression.ScriptTokenStream == null) return;
        try
        {
            string dynamicSql = string.Join("", sqlExpression.ScriptTokenStream
                .Skip(sqlExpression.FirstTokenIndex).Take(sqlExpression.LastTokenIndex - sqlExpression.FirstTokenIndex + 1).Select(t => t.Text));
            if (dynamicSql.StartsWith("'") && dynamicSql.EndsWith("'")) { dynamicSql = dynamicSql.Substring(1, dynamicSql.Length - 2).Replace("''", "'"); }
            else if (dynamicSql.StartsWith("N'") && dynamicSql.EndsWith("'")) { dynamicSql = dynamicSql.Substring(2, dynamicSql.Length - 3).Replace("''", "'"); }
            if (string.IsNullOrWhiteSpace(dynamicSql)) return;

            using (var reader = new StringReader(dynamicSql))
            {
                var fragment = _dynamicSqlParser.Parse(reader, out var errors);
                if (errors != null && errors.Count > 0) { Console.Error.WriteLine($"Errors parsing dynamic SQL within {GetUniqueNodeKey(_currentSqlObject)}: {string.Join("; ", errors.Select(e => e.Message))}"); return; }
                if (fragment != null) { fragment.Accept(this); } // Recursive call maintains _currentSqlObject context
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Exception parsing dynamic SQL within {GetUniqueNodeKey(_currentSqlObject)}: {ex.Message}"); }
    }
}
