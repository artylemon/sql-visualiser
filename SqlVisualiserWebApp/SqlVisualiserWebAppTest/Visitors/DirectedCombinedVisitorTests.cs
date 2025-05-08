using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using SqlVisualiserWebApp.Services;
using SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.IO;
using System.Linq;
using System; // For StringComparison

namespace SqlVisualiserWebAppTest;

[TestClass]
public class DirectedCombinedVisitorTests // Renamed for clarity
{
    private const string DEFAULT_CATALOG = "TestCatalog"; // Define a default catalog for tests
    private const string DEFAULT_SCHEMA = "dbo"; // Define a default schema

    // Helper to parse SQL
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

    // Helper to create unique key consistently
    private string GetUniqueKey(string name, string schema = DEFAULT_SCHEMA, string catalog = DEFAULT_CATALOG)
    {
        return $"[{catalog}].[{schema}].[{name}]";
    }

    // --- Test Setup Helper ---
    private (DirectedCombinedVisitor visitor, ISqlObject currentNode) SetupVisitor(
        List<ISqlObject>? sqlObjects = null, // Now contains tables, functions, sprocs
        string currentObjectName = "TestProc",
        NodeType currentNodeType = NodeType.Procedure,
        string currentObjectCatalog = DEFAULT_CATALOG,
        string currentObjectSchema = DEFAULT_SCHEMA)
    {
        sqlObjects ??= new List<ISqlObject>();

        // Ensure all provided objects have a catalog and schema
        foreach (var obj in sqlObjects)
        {
            obj.Catalog ??= DEFAULT_CATALOG;
            obj.Schema ??= DEFAULT_SCHEMA;
        }

        // Ensure the current node object exists in the list if not already present
        var currentNode = sqlObjects.FirstOrDefault(o =>
                            o.Name.Equals(currentObjectName, StringComparison.OrdinalIgnoreCase) &&
                            (o.Schema?.Equals(currentObjectSchema, StringComparison.OrdinalIgnoreCase) ?? (currentObjectSchema == DEFAULT_SCHEMA)) && // Handle potential null schema
                            (o.Catalog?.Equals(currentObjectCatalog, StringComparison.OrdinalIgnoreCase) ?? (currentObjectCatalog == DEFAULT_CATALOG)) // Handle potential null catalog
                           );

        if (currentNode == null)
        {
            // Create the appropriate ISqlObject based on type
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
            sqlObjects.Insert(0, currentNode); // Add to the list
        }
        else
        {
            // Ensure existing current node has catalog/schema if they were somehow null
            currentNode.Catalog ??= currentObjectCatalog;
            currentNode.Schema ??= currentObjectSchema;
        }


        // Pass the unified list to the constructor
        var visitor = new DirectedCombinedVisitor(sqlObjects);
        visitor.SetCurrentNode(currentNode); // Set the context AFTER creating the visitor
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
                new StoredProcedure { Name = calleeName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }
            },
            currentObjectName: callerName
        );
        var sql = $"EXEC {calleeName};"; // Use canonical name in SQL for simplicity, visitor handles casing
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(calleeKey), "Callee SP node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller SP node missing.");
        // Dependency: Caller -> Callee
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
        var sql = $"SELECT {DEFAULT_SCHEMA}.{funcName}();"; // Use qualified name
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller SP node missing.");
        // Data Flow: Function -> Caller
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
        var sql = $"SELECT T.Col FROM {DEFAULT_SCHEMA}.{funcName}() AS T;"; // Use qualified name
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "TVF node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller SP node missing.");
        // Data Flow: TVF -> Caller
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data Flow: Table -> Procedure
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
        // Use canonical names in dynamic SQL for simplicity in this test
        var sql = $"EXEC('SELECT * FROM {tableName}; EXEC {procName};');";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "SP node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller node missing.");
        // Data Flow: MyTable -> CallerProc (from dynamic SELECT)
        Assert.IsTrue(visitor.Graph[tableKey].OutNodes.Contains(callerKey), "Table should flow to caller.");
        Assert.IsTrue(visitor.Graph[callerKey].InNodes.Contains(tableKey), "Caller should read from table.");
        // Dependency: CallerProc -> MyStoredProcedure (from dynamic EXEC)
        Assert.IsTrue(visitor.Graph[callerKey].OutNodes.Contains(procKey), "Caller should call SP.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(callerKey), "SP should be called by caller.");
    }

    // Visit_InvalidDynamicSql_ShouldNotThrow remains the same

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
                new SqlTable { Name = tableName, Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA }, // Use canonical lowercase name
                new StoredProcedure { Name = procName, Definition = "...", Catalog = DEFAULT_CATALOG, Schema=DEFAULT_SCHEMA } // Use canonical lowercase name
            },
            currentObjectName: callerName // Use canonical lowercase name
        );
        // SQL uses different casing
        var sql = "EXEC MyStoredProcedure; SELECT * FROM MyTable;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert - Use canonical (lowercase) names for assertions via keys
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing (case-insensitive).");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "SP node missing (case-insensitive).");
        Assert.IsTrue(visitor.Graph.ContainsKey(callerKey), "Caller node missing (case-insensitive).");
        // Data Flow: mytable -> callerproc
        Assert.IsTrue(visitor.Graph[tableKey].OutNodes.Contains(callerKey));
        Assert.IsTrue(visitor.Graph[callerKey].InNodes.Contains(tableKey));
        // Dependency: callerproc -> mystoredprocedure
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data Write: MyProcedure -> MyTable
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data Write: MyProcedure -> MyTable
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
         // Data Write: MyProcedure -> MyTable
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(targetTableKey), "TargetTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(sourceTableKey), "SourceTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data Write: MyProcedure -> TargetTable
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(targetTableKey), "Procedure should write TO TargetTable.");
        Assert.IsTrue(visitor.Graph[targetTableKey].InNodes.Contains(procKey), "TargetTable should receive write FROM procedure.");
        // Data Flow: SourceTable -> MyProcedure
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data flow: fn_GetTimestamp -> MyProcedure
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey), "Procedure should receive data FROM function.");
        // Data write: MyProcedure -> Logs
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data flow: fn_GetSourceData -> MyProcedure
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey), "Procedure should receive data FROM function.");
        // Data write: MyProcedure -> TargetTable
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data flow: fn_CalculateDiscount -> TestProc
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey), "Procedure should receive data FROM function.");
        // Data flow: Orders -> TestProc
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(tableKey), "Table node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data flow: fn_GetNewPrice -> TestProc
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey), "Procedure should receive data FROM function.");
        // Data write: TestProc -> Products
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(targetTableKey), "TargetTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(sourceTableKey), "SourceTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data write: TestProc -> TargetTable
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(targetTableKey), "Procedure should write TO TargetTable.");
        Assert.IsTrue(visitor.Graph[targetTableKey].InNodes.Contains(procKey), "TargetTable should receive write FROM procedure.");
        // Data flow: SourceTable -> TestProc
        Assert.IsTrue(visitor.Graph[sourceTableKey].OutNodes.Contains(procKey), "SourceTable should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(sourceTableKey), "Procedure should receive data FROM SourceTable.");
        // Ensure TargetTable does NOT have an OutNode to TestProc (no read dependency added for DML target)
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(targetTableKey), "TargetTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(sourceTableKey), "SourceTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Data write: TestProc -> TargetTable
        Assert.IsTrue(visitor.Graph[procKey].OutNodes.Contains(targetTableKey), "Procedure should write TO TargetTable.");
        Assert.IsTrue(visitor.Graph[targetTableKey].InNodes.Contains(procKey), "TargetTable should receive write FROM procedure.");
        // Data flow: SourceTable -> TestProc
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

        // Assert - Use unique keys
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

        // Assert - Use unique keys
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

        // Assert - Use unique keys
        Assert.IsTrue(visitor.Graph.ContainsKey(funcKey), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey(procKey), "Procedure node missing.");
        // Assert.IsFalse(visitor.Graph.ContainsKey(GetUniqueKey("AnotherTable")), "Unknown table should not be added."); // Check using key if needed
        // Data flow: MyFunction -> TestProc
        Assert.IsTrue(visitor.Graph[funcKey].OutNodes.Contains(procKey));
        Assert.IsTrue(visitor.Graph[procKey].InNodes.Contains(funcKey));
    }

}
