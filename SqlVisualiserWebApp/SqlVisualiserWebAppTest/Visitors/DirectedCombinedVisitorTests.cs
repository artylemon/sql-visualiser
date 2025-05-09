using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using SqlVisualiserWebApp.Services; // Assuming SqlVisualiserService is here for ILogger<SqlVisualiserService>
using SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.IO;
using System.Linq;
using System;
using Microsoft.Extensions.Logging; // Required for ILogger
using Microsoft.Extensions.Logging.Abstractions; // Required for NullLogger

namespace SqlVisualiserWebAppTest;

[TestClass]
public class DirectedCombinedVisitorTests
{
    private const string DEFAULT_CATALOG = "TestCatalog";
    private const string DEFAULT_SCHEMA = "dbo";
    private ILogger<SqlVisualiserService> _logger;

    [TestInitialize]
    public void TestInitialize()
    {
        // Use NullLogger to avoid actual logging during tests
        _logger = NullLogger<SqlVisualiserService>.Instance;
    }

    private TSqlFragment ParseSql(string sql)
    {
        var parser = new TSql150Parser(false);
        using (var reader = new StringReader(sql))
        {
            var fragment = parser.Parse(reader, out var errors);
            Assert.AreEqual(0, errors.Count, $"SQL Parsing failed: {string.Join(", ", errors.Select(e => e.Message))}");
            return fragment;
        }
    }

    private string GetUniqueKey(string name, string schema = DEFAULT_SCHEMA, string catalog = DEFAULT_CATALOG)
    {
        return $"[{catalog}].[{schema}].[{name}]";
    }

    // *** UPDATED: Test Setup Helper to use new visitor constructor and SetupGraph ***
    private (DirectedCombinedVisitor visitor, ISqlObject currentNode) SetupVisitor(
        List<ISqlObject>? sqlObjects = null,
        string currentObjectName = "TestProc",
        NodeType currentNodeType = NodeType.Procedure,
        string currentObjectCatalog = DEFAULT_CATALOG,
        string currentObjectSchema = DEFAULT_SCHEMA)
    {
        sqlObjects ??= new List<ISqlObject>();

        foreach (var obj in sqlObjects)
        {
            obj.Catalog ??= DEFAULT_CATALOG;
            obj.Schema ??= DEFAULT_SCHEMA;
        }

        var currentNode = sqlObjects.FirstOrDefault(o =>
                            o.Name.Equals(currentObjectName, StringComparison.OrdinalIgnoreCase) &&
                            (o.Schema?.Equals(currentObjectSchema, StringComparison.OrdinalIgnoreCase) ?? (currentObjectSchema == DEFAULT_SCHEMA)) &&
                            (o.Catalog?.Equals(currentObjectCatalog, StringComparison.OrdinalIgnoreCase) ?? (currentObjectCatalog == DEFAULT_CATALOG))
                           );

        if (currentNode == null)
        {
            switch (currentNodeType)
            {
                case NodeType.Procedure:
                    currentNode = new StoredProcedure { Name = currentObjectName, Definition = "-- Test Proc", Catalog = currentObjectCatalog, Schema = currentObjectSchema };
                    break;
                case NodeType.Function:
                     currentNode = new SqlFunction { Name = currentObjectName, Definition = "-- Test Func", Catalog = currentObjectCatalog, Schema = currentObjectSchema };
                     break;
                case NodeType.Table:
                     currentNode = new SqlTable { Name = currentObjectName, Catalog = currentObjectCatalog, Schema = currentObjectSchema };
                     break;
                default:
                     throw new ArgumentException($"Unsupported NodeType '{currentNodeType}' for current object setup.");
            }
            sqlObjects.Insert(0, currentNode);
        }
        else
        {
            currentNode.Catalog ??= currentObjectCatalog;
            currentNode.Schema ??= currentObjectSchema;
        }

        // *** UPDATED: Instantiate visitor with logger ***
        var visitor = new DirectedCombinedVisitor(_logger);
        // *** NEW: Call SetupGraph to initialize the graph with all objects ***
        visitor.SetupGraph(sqlObjects);
        // Set current node after graph is set up with all potential nodes
        visitor.SetCurrentNode(currentNode);
        return (visitor, currentNode);
    }

    [TestMethod]
    public void Visit_ExecuteStatement_StoredProcedureReference_ShouldLinkCorrectly()
    {
        // Arrange
        string callerName = "CallerProc";
        string calleeName = "MyStoredProcedure";
        string callerKey = GetUniqueKey(callerName);
        string calleeKey = GetUniqueKey(calleeName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                // Current node (CallerProc) will be added by SetupVisitor if not present
                new StoredProcedure { Name = calleeName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: callerName
        );
        var sql = $"EXEC {calleeName};";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(calleeKey), "Callee SP node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller SP node missing.");
        Assert.IsTrue(visitor.Graph[callerKey].OutNodes.Contains(calleeKey), "Caller should have OutNode to callee.");
        Assert.IsTrue(visitor.Graph[calleeKey].InNodes.Contains(callerKey), "Callee should have InNode from caller.");
        Assert.AreEqual(DEFAULT_CATALOG, visitor.Graph[calleeKey].Catalog, "Callee catalog mismatch.");
        Assert.AreEqual(DEFAULT_CATALOG, visitor.Graph[callerKey].Catalog, "Caller catalog mismatch.");
    }

    [TestMethod]
    public void Visit_ScalarFunctionCall_ShouldLinkCorrectly()
    {
        // Arrange
        string callerName = "CallerProc";
        string funcName = "MyFunction";
        string callerKey = GetUniqueKey(callerName);
        string funcKey = GetUniqueKey(funcName);

         var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlFunction { Name = funcName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: callerName
        );
        var sql = $"SELECT {DEFAULT_SCHEMA}.{funcName}();";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller SP node missing.");
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(callerKey), "Function should flow data TO caller.");
        Assert.IsTrue(visitor.Graph[callerKey].InNodes.Contains(funcKey), "Caller should receive data FROM function.");
        Assert.AreEqual(DEFAULT_CATALOG, visitor.Graph[funcKey].Catalog, "Function catalog mismatch.");
    }

     [TestMethod]
    public void Visit_TableValuedFunctionInFrom_ShouldLinkCorrectly()
    {
        // Arrange
        string callerName = "CallerProc";
        string funcName = "fn_MyTVF";
        string callerKey = GetUniqueKey(callerName);
        string funcKey = GetUniqueKey(funcName);

         var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlFunction { Name = funcName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: callerName
        );
        var sql = $"SELECT T.Col FROM {DEFAULT_SCHEMA}.{funcName}() AS T;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "TVF node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller SP node missing.");
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(callerKey), "TVF should flow data TO caller.");
        Assert.IsTrue(visitor.Graph[callerKey].InNodes.Contains(funcKey), "Caller should receive data FROM TVF.");
        Assert.AreEqual(DEFAULT_CATALOG, visitor.Graph[funcKey].Catalog, "TVF catalog mismatch.");
    }

    [TestMethod]
    public void Visit_SelectFromTable_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "MyProcedure";
        string tableName = "MyTable";
        string procKey = GetUniqueKey(procName);
        string tableKey = GetUniqueKey(tableName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
             currentObjectName: procName
        );
        var sql = $"SELECT * FROM {tableName};";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[tableKey].OutNodes.Contains(procKey), "Table should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(tableKey), "Procedure should receive data FROM table.");
         Assert.AreEqual(DEFAULT_CATALOG, visitor.Graph[tableKey].Catalog, "Table catalog mismatch.");
    }

    [TestMethod]
    public void Visit_DynamicSql_ShouldParseAndAddReferences()
    {
        // Arrange
         string callerName = "CallerProc";
         string tableName = "MyTable";
         string procName = "MyStoredProcedure";
         string callerKey = GetUniqueKey(callerName);
         string tableKey = GetUniqueKey(tableName);
         string procKey = GetUniqueKey(procName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new StoredProcedure { Name = procName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: callerName
        );
        var sql = $"EXEC('SELECT * FROM {tableName}; EXEC {procName};');";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "SP node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller node missing.");
        Assert.IsTrue(visitor.Graph[tableKey].OutNodes.Contains(callerKey), "Table should flow to caller.");
        Assert.IsTrue(visitor.Graph[callerKey].InNodes.Contains(tableKey), "Caller should read from table.");
        Assert.IsTrue(visitor.Graph[callerKey].OutNodes.Contains(procKey), "Caller should call SP.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(callerKey), "SP should be called by caller.");
    }

    // Visit_InvalidDynamicSql_ShouldNotThrow remains the same but will use new SetupVisitor

    [TestMethod]
    public void Visit_CaseInsensitiveObjectNames_ShouldMatchCorrectly()
    {
        // Arrange
         string callerName = "callerproc";
         string tableName = "mytable";
         string procName = "mystoredprocedure";
         string callerKey = GetUniqueKey(callerName);
         string tableKey = GetUniqueKey(tableName);
         string procKey = GetUniqueKey(procName);

        var (visitor, currentNode) = SetupVisitor(
             sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new StoredProcedure { Name = procName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: callerName
        );
        var sql = "EXEC MyStoredProcedure; SELECT * FROM MyTable;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing (case-insensitive).");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "SP node missing (case-insensitive).");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller node missing (case-insensitive).");
        Assert.IsTrue(visitor.Graph[tableKey].OutNodes.Contains(callerKey));
        Assert.IsTrue(visitor.Graph[callerKey].InNodes.Contains(tableKey));
        Assert.IsTrue(visitor.Graph[callerKey].OutNodes.Contains(procKey));
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(callerKey));
    }

    [TestMethod]
    public void Visit_InsertStatement_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "MyProcedure";
        string tableName = "MyTable";
        string procKey = GetUniqueKey(procName);
        string tableKey = GetUniqueKey(tableName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $"INSERT INTO {tableName} (Column1) VALUES ('Value1');";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(tableKey), "Procedure should write TO table.");
        Assert.IsTrue(visitor.Graph[tableKey].InNodes.Contains(procKey), "Table should receive write FROM procedure.");
    }

    [TestMethod]
    public void Visit_UpdateStatement_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "MyProcedure";
        string tableName = "MyTable";
        string procKey = GetUniqueKey(procName);
        string tableKey = GetUniqueKey(tableName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $"UPDATE {tableName} SET Column1 = 'Value1' WHERE Column2 = 'Value2';";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(tableKey), "Procedure should write TO table.");
        Assert.IsTrue(visitor.Graph[tableKey].InNodes.Contains(procKey), "Table should receive write FROM procedure.");
    }

    [TestMethod]
    public void Visit_DeleteStatement_ShouldLinkCorrectly()
    {
        // Arrange
         string procName = "MyProcedure";
        string tableName = "MyTable";
        string procKey = GetUniqueKey(procName);
        string tableKey = GetUniqueKey(tableName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $"DELETE FROM {tableName} WHERE Column1 = 'Value1';";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(tableKey), "Procedure should write TO table.");
        Assert.IsTrue(visitor.Graph[tableKey].InNodes.Contains(procKey), "Table should receive write FROM procedure.");
    }

    [TestMethod]
    public void Visit_MergeStatement_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "MyProcedure";
        string targetTableName = "TargetTable";
        string sourceTableName = "SourceTable";
        string procKey = GetUniqueKey(procName);
        string targetTableKey = GetUniqueKey(targetTableName);
        string sourceTableKey = GetUniqueKey(sourceTableName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = targetTableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new SqlTable { Name = sourceTableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $@"
            MERGE INTO {targetTableName} AS Target
            USING {sourceTableName} AS Source
            ON Target.Id = Source.Id
            WHEN MATCHED THEN UPDATE SET Target.Column1 = Source.Column1
            WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Column1) VALUES (Source.Id, Source.Column1);";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(targetTableKey), "TargetTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(sourceTableKey), "SourceTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(targetTableKey), "Procedure should write TO TargetTable.");
        Assert.IsTrue(visitor.Graph[targetTableKey].InNodes.Contains(procKey), "TargetTable should receive write FROM procedure.");
        Assert.IsTrue(visitor.Graph[sourceTableKey].OutNodes.Contains(procKey), "SourceTable should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(sourceTableKey), "Procedure should receive data FROM SourceTable.");
    }

     [TestMethod]
    public void Visit_InsertStatement_WithFunctionCallInValues_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "MyProcedure";
        string tableName = "Logs";
        string funcName = "fn_GetTimestamp";
        string procKey = GetUniqueKey(procName);
        string tableKey = GetUniqueKey(tableName);
        string funcKey = GetUniqueKey(funcName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new SqlFunction { Name = funcName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $@"INSERT INTO {tableName} (LogTime, Message) VALUES ({DEFAULT_SCHEMA}.{funcName}(), 'Test Log');";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey), "Procedure should receive data FROM function.");
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(tableKey), "Procedure should write data TO table.");
        Assert.IsTrue(visitor.Graph[tableKey].InNodes.Contains(procKey), "Table should receive write FROM procedure.");
    }

     [TestMethod]
    public void Visit_InsertStatement_WithTVFInSelectSource_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "MyProcedure";
        string tableName = "TargetTable";
        string funcName = "fn_GetSourceData";
        string procKey = GetUniqueKey(procName);
        string tableKey = GetUniqueKey(tableName);
        string funcKey = GetUniqueKey(funcName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new SqlFunction { Name = funcName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $@"INSERT INTO {tableName} (Col1, Col2) SELECT SourceCol1, SourceCol2 FROM {DEFAULT_SCHEMA}.{funcName}();";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey), "Procedure should receive data FROM function.");
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(tableKey), "Procedure should write data TO table.");
        Assert.IsTrue(visitor.Graph[tableKey].InNodes.Contains(procKey), "Table should receive write FROM procedure.");
    }

    [TestMethod]
    public void Visit_FunctionCall_InWhereClause_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "TestProc";
        string tableName = "Orders";
        string funcName = "fn_CalculateDiscount";
        string procKey = GetUniqueKey(procName);
        string tableKey = GetUniqueKey(tableName);
        string funcKey = GetUniqueKey(funcName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new SqlFunction { Name = funcName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $@"SELECT OrderID FROM {tableName} WHERE {DEFAULT_SCHEMA}.{funcName}(Price) > 10;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey), "Procedure should receive data FROM function.");
        Assert.IsTrue(visitor.Graph[tableKey].OutNodes.Contains(procKey), "Table should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(tableKey), "Procedure should receive data FROM table.");
    }

     [TestMethod]
    public void Visit_FunctionCall_InSetClause_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "TestProc";
        string tableName = "Products";
        string funcName = "fn_GetNewPrice";
        string procKey = GetUniqueKey(procName);
        string tableKey = GetUniqueKey(tableName);
        string funcKey = GetUniqueKey(funcName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new SqlFunction { Name = funcName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
             currentObjectName: procName
        );
        var sql = $@"UPDATE {tableName} SET Price = {DEFAULT_SCHEMA}.{funcName}(CategoryID) WHERE ProductID = 1;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey), "Procedure should receive data FROM function.");
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(tableKey), "Procedure should write data TO table.");
        Assert.IsTrue(visitor.Graph[tableKey].InNodes.Contains(procKey), "Table should receive write FROM procedure.");
    }

    [TestMethod]
    public void Visit_TableRead_InUpdateFromClause_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "TestProc";
        string targetTableName = "TargetTable";
        string sourceTableName = "SourceTable";
        string procKey = GetUniqueKey(procName);
        string targetTableKey = GetUniqueKey(targetTableName);
        string sourceTableKey = GetUniqueKey(sourceTableName);

        var (visitor, currentNode) = SetupVisitor(
             sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = targetTableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new SqlTable { Name = sourceTableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $@"UPDATE t SET t.Col1 = s.Col1 FROM {targetTableName} t JOIN {sourceTableName} s ON t.ID = s.ID;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(targetTableKey), "TargetTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(sourceTableKey), "SourceTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(targetTableKey), "Procedure should write TO TargetTable.");
        Assert.IsTrue(visitor.Graph[targetTableKey].InNodes.Contains(procKey), "TargetTable should receive write FROM procedure.");
        Assert.IsTrue(visitor.Graph[sourceTableKey].OutNodes.Contains(procKey), "SourceTable should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(sourceTableKey), "Procedure should receive data FROM SourceTable.");
        Assert.IsFalse(visitor.Graph[targetTableKey].OutNodes.Contains(procKey), "TargetTable should not have OutNode to procedure (read dependency).");

    }

     [TestMethod]
    public void Visit_TableRead_InMergeUsingClause_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "TestProc";
        string targetTableName = "TargetTable";
        string sourceTableName = "SourceTable";
        string procKey = GetUniqueKey(procName);
        string targetTableKey = GetUniqueKey(targetTableName);
        string sourceTableKey = GetUniqueKey(sourceTableName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = targetTableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new SqlTable { Name = sourceTableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $@"
            MERGE {targetTableName} AS T
            USING {sourceTableName} AS S
            ON (T.ID = S.ID)
            WHEN MATCHED THEN UPDATE SET T.Name = S.Name
            WHEN NOT MATCHED BY TARGET THEN INSERT (ID, Name) VALUES (S.ID, S.Name);";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(targetTableKey), "TargetTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(sourceTableKey), "SourceTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(targetTableKey), "Procedure should write TO TargetTable.");
        Assert.IsTrue(visitor.Graph[targetTableKey].InNodes.Contains(procKey), "TargetTable should receive write FROM procedure.");
        Assert.IsTrue(visitor.Graph[sourceTableKey].OutNodes.Contains(procKey), "SourceTable should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(sourceTableKey), "Procedure should receive data FROM SourceTable.");
    }

     [TestMethod]
    public void Visit_MultipleDependencies_ShouldLinkAll()
    {
        // Arrange
        string procName = "TestProc";
        string tableAName = "TableA";
        string tableBName = "TableB";
        string funcName = "fn_Func1";
        string proc2Name = "sp_Proc2";
        string procKey = GetUniqueKey(procName);
        string tableAKey = GetUniqueKey(tableAName);
        string tableBKey = GetUniqueKey(tableBName);
        string funcKey = GetUniqueKey(funcName);
        string proc2Key = GetUniqueKey(proc2Name);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = tableAName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new SqlTable { Name = tableBName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new SqlFunction { Name = funcName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA },
                new StoredProcedure { Name = proc2Name, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $@"
            SELECT a.*, {DEFAULT_SCHEMA}.{funcName}(b.ID)
            FROM {tableAName} a JOIN {tableBName} b ON a.ID = b.RefID;
            EXEC {proc2Name} @Param = 1;
            INSERT INTO {tableAName} (Col) VALUES (1);";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(tableAKey), "TableA missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(tableBKey), "TableB missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "fn_Func1 missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(proc2Key), "sp_Proc2 missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "TestProc missing.");

        // Data flow: TableA -> TestProc (SELECT)
        Assert.IsTrue(visitor.Graph[tableAKey].OutNodes.Contains(procKey));
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(tableAKey));
        // Data flow: TableB -> TestProc (JOIN)
        Assert.IsTrue(visitor.Graph[tableBKey].OutNodes.Contains(procKey));
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(tableBKey));
         // Data flow: fn_Func1 -> TestProc (SELECT list)
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey));
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey));
        // Dependency: TestProc -> sp_Proc2 (EXEC)
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(proc2Key));
        Assert.IsTrue(visitor.Graph[proc2Key].InNodes.Contains(procKey));
         // Data write: TestProc -> TableA (INSERT)
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(tableAKey));
        Assert.IsTrue(visitor.Graph[tableAKey].InNodes.Contains(procKey));

        // Check counts (ensure relationships aren't duplicated incorrectly)
        Assert.AreEqual(2, visitor.Graph[procKey].OutNodes.Count, "TestProc OutNodes count incorrect."); // To TableA (write), To sp_Proc2 (call)
        Assert.AreEqual(3, visitor.Graph[procKey].InNodes.Count, "TestProc InNodes count incorrect."); // From TableA, TableB, fn_Func1
        Assert.AreEqual(1, visitor.Graph[tableAKey].OutNodes.Count, "TableA OutNodes count incorrect."); // To TestProc (read)
        Assert.AreEqual(1, visitor.Graph[tableAKey].InNodes.Count, "TableA InNodes count incorrect."); // From TestProc (write)
    }

     [TestMethod]
    public void Visit_TableAlias_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "TestProc";
        string tableName = "Customers";
        string procKey = GetUniqueKey(procName);
        string tableKey = GetUniqueKey(tableName);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                 new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: procName
        );
        var sql = $@"SELECT c.Name FROM {tableName} c WHERE c.City = 'London';";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Customers node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data flow: Customers -> TestProc
        Assert.IsTrue(visitor.Graph[tableKey].OutNodes.Contains(procKey), "Table should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(tableKey), "Procedure should receive data FROM table.");
    }

     [TestMethod]
    public void Visit_SchemaQualifiedFunction_ShouldLinkCorrectly()
    {
        // Arrange
        string procName = "TestProc";
        string funcName = "MyFunction";
        string procKey = GetUniqueKey(procName);
        string funcKey = GetUniqueKey(funcName); // Assumes default schema

        var (visitor, currentNode) = SetupVisitor(
             sqlObjects: new List<ISqlObject> {
                 new SqlFunction { Name = funcName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
             },
             currentObjectName: procName
        );
        // Use schema qualification in SQL
        var sql = $@"SELECT OtherColumn FROM AnotherTable WHERE SomeColumn = {DEFAULT_SCHEMA}.{funcName}();"; // AnotherTable is unknown
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Assert.IsFalse(visitor.Graph.ContainsKey(GetUniqueKey("AnotherTable")), "Unknown table should not be added."); // Check using key if needed
        // Data flow: MyFunction -> TestProc
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey));
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey));
    }

    // *** NEW TEST CASE for Cross-Catalog and Multiple Joins ***
    [TestMethod]
    public void Visit_SelectWithMultipleJoinsAndCrossCatalog_ShouldLinkCorrectly()
    {
        // Arrange
        string currentCatalog = "Nova";
        string currentSchema = "B2B2";
        string currentProcName = "up_GetControlAccounts";
        string currentProcKey = GetUniqueKey(currentProcName, currentSchema, currentCatalog);

        string table1Name = "oas_grplist";
        string table1Schema = "dbo";
        string table1Catalog = "CFLIVE";
        string table1Key = GetUniqueKey(table1Name, table1Schema, table1Catalog);

        string table2Name = "napSupplierControlAccount";
        string table2Schema = "dbo";
        string table2Catalog = currentCatalog; // Same catalog as SP
        string table2Key = GetUniqueKey(table2Name, table2Schema, table2Catalog);

        string table3Name = "napControlAccount";
        string table3Schema = "dbo";
        string table3Catalog = currentCatalog; // Same catalog as SP
        string table3Key = GetUniqueKey(table3Name, table3Schema, table3Catalog);

        var (visitor, currentNode) = SetupVisitor(
            sqlObjects: new List<ISqlObject> {
                new SqlTable { Name = table1Name, Catalog = table1Catalog, Schema = table1Schema },
                new SqlTable { Name = table2Name, Catalog = table2Catalog, Schema = table2Schema },
                new SqlTable { Name = table3Name, Catalog = table3Catalog, Schema = table3Schema }
            },
            currentObjectName: currentProcName,
            currentNodeType: NodeType.Procedure,
            currentObjectCatalog: currentCatalog,
            currentObjectSchema: currentSchema
        );

        var sql = $@"
            SELECT
                G.[grpcode] as GroupID,
                G.[code] as SupplierRef,
                G.[elmlevel] as SupplierLevel,
                SCA.[MaintainIND],
                SCA.[CtrlAccKey],
                CtrlA.[AccountRef] as Account,
                CtrlA.[GLBusUnitRef] as GLBusUnit,
                SCA.[VATAccKey],
                VATA.[AccountRef] as VATAccount,
                VATA.[GLBusUnitRef] as VATGLBusUnit,
                VATA.VAT as VAT
            FROM
                [{table1Catalog}].[{table1Schema}].[{table1Name}] as G WITH (NOLOCK)
                INNER JOIN [{table2Schema}].[{table2Name}] as SCA WITH (NOLOCK)
                    ON SCA.SupplierGroupRef = G.GrpCode
                INNER JOIN [{table3Schema}].[{table3Name}] as CtrlA WITH (NOLOCK)
                    ON SCA.CtrlAccKey = CtrlA.ControlAccRef
                        AND SCA.Entity = CtrlA.Entity
                INNER JOIN [{table3Schema}].[{table3Name}] as VATA WITH (NOLOCK)
                    ON SCA.VATAccKey = VATA.ControlAccRef
                        AND SCA.Entity = VATA.Entity
                INNER JOIN #Output on SupplierCode = G.Code  -- #Output is a temp table, not tracked
            WHERE UPPER(G.cmpcode) = 'NEXT'
            AND     UPPER(G.grpcode) like  'AP_CTL%'
        ";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        // Check all nodes exist
        Assert.IsTrue(visitor.Graph.ContainsKey(currentProcKey), $"Node {currentProcKey} missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(table1Key), $"Node {table1Key} missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(table2Key), $"Node {table2Key} missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(table3Key), $"Node {table3Key} missing.");
        Assert.IsFalse(visitor.Graph.ContainsKey(GetUniqueKey("#Output", currentSchema, currentCatalog)), "Temp table #Output should not be in the graph.");

        // Verify catalogs are correctly stored
        Assert.AreEqual(currentCatalog, visitor.Graph[currentProcKey].Catalog);
        Assert.AreEqual(table1Catalog, visitor.Graph[table1Key].Catalog);
        Assert.AreEqual(table2Catalog, visitor.Graph[table2Key].Catalog);
        Assert.AreEqual(table3Catalog, visitor.Graph[table3Key].Catalog);

        // Data Flow: Table -> Procedure (current node)
        Assert.IsTrue(visitor.Graph[table1Key].OutNodes.Contains(currentProcKey), $"{table1Key} should flow data TO {currentProcKey}");
        Assert.IsTrue(visitor.Graph[currentProcKey].InNodes.Contains(table1Key), $"{currentProcKey} should receive data FROM {table1Key}");

        Assert.IsTrue(visitor.Graph[table2Key].OutNodes.Contains(currentProcKey), $"{table2Key} should flow data TO {currentProcKey}");
        Assert.IsTrue(visitor.Graph[currentProcKey].InNodes.Contains(table2Key), $"{currentProcKey} should receive data FROM {table2Key}");

        Assert.IsTrue(visitor.Graph[table3Key].OutNodes.Contains(currentProcKey), $"{table3Key} should flow data TO {currentProcKey}");
        Assert.IsTrue(visitor.Graph[currentProcKey].InNodes.Contains(table3Key), $"{currentProcKey} should receive data FROM {table3Key}");

        // Check InNodes count for the procedure (should contain the 3 tables)
        Assert.AreEqual(3, visitor.Graph[currentProcKey].InNodes.Count, $"Incorrect number of InNodes for {currentProcKey}");
    }

}
