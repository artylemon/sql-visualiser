using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using SqlVisualiserWebApp.Services;
using SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.IO;
using System.Linq; // Required for First()

namespace SqlVisualiserWebAppTest;

[TestClass]
public class DirectedCombinedVisitorMoreTests // New class or add to existing
{
    // Helper to parse SQL - Assuming you have this in your test class
    private TSqlFragment ParseSql(string sql)
    {
        var parser = new TSql150Parser(false); // Or true depending on your SQL default
        using (var reader = new StringReader(sql))
        {
            // Using ParseScript instead of Parse might be better for multi-statement SQL
            var fragment = parser.Parse(reader, out var errors);
            Assert.AreEqual(0, errors.Count, $"SQL Parsing failed: {string.Join(", ", errors.Select(e => e.Message))}");
            return fragment;
        }
    }

    // --- Test Setup Helper ---
    private (DirectedCombinedVisitor visitor, ISqlObject currentNode) SetupVisitor(
        List<string>? tables = null,
        List<ISqlObject>? sqlObjects = null,
        string currentObjectName = "TestProc",
        NodeType currentNodeType = NodeType.Procedure)
    {
        tables ??= new List<string>();
        sqlObjects ??= new List<ISqlObject>();

        // Ensure the current node object exists in the list if not already present
        var currentNode = sqlObjects.FirstOrDefault(o => o.Name.Equals(currentObjectName, System.StringComparison.OrdinalIgnoreCase));
        if (currentNode == null)
        {
            if (currentNodeType == NodeType.Procedure)
            {
                currentNode = new StoredProcedure { Name = currentObjectName, Definition = "-- Test Proc" };
            }
            else // Assuming Function otherwise
            {
                 currentNode = new SqlFunction { Name = currentObjectName, Definition = "-- Test Func" };
            }

            sqlObjects.Insert(0, currentNode); // Add to the list
        }

        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(currentNode);
        return (visitor, currentNode);
    }

    
    [TestMethod]
    public void Visit_ExecuteStatement_StoredProcedureReference_ShouldAddToGraph()
    {
        // Arrange
        var tables = new List<string>();
        var sqlObjects = new List<ISqlObject>
        {
            new StoredProcedure { Name = "MyStoredProcedure", Definition = "..." }
        };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "EXEC MyStoredProcedure;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyStoredProcedure"));
        Assert.IsTrue(visitor.Graph[visitor.Graph.Keys.First()].OutNodes.Contains("MyStoredProcedure"));
    }

    [TestMethod]
    public void Visit_ExecuteStatement_FunctionReference_ShouldAddToGraph()
    {
        // Arrange
        var tables = new List<string>();
        var sqlObjects = new List<ISqlObject>
        {
            new SqlFunction { Name = "MyFunction", Definition = "..." }
        };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "SELECT dbo.MyFunction();";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyFunction"));
        Assert.IsTrue(visitor.Graph[visitor.Graph.Keys.First()].InNodes.Contains("MyFunction"));
    }

    [TestMethod]
    public void Visit_NamedTableReference_ShouldAddTableToGraph()
    {
        // Arrange
        var tables = new List<string> { "MyTable" };
        var sqlObjects = new List<ISqlObject>
        {
            new StoredProcedure { Name = "MyProcedure", Definition = "..." }
        };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "SELECT * FROM MyTable;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyTable"));
        Assert.IsTrue(visitor.Graph[visitor.Graph.Keys.First()].InNodes.Contains("MyTable"));
    }

    [TestMethod]
    public void Visit_DynamicSql_ShouldParseAndAddReferences()
    {
        // Arrange
        var tables = new List<string> { "MyTable" };
        var sqlObjects = new List<ISqlObject>
        {
            new StoredProcedure { Name = "MyStoredProcedure", Definition = "..." }
        };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "EXEC('SELECT * FROM MyTable; EXEC MyStoredProcedure;');";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyTable"));
        Assert.IsTrue(visitor.Graph.ContainsKey("MyStoredProcedure"));
        Assert.IsTrue(visitor.Graph[visitor.Graph.Keys.First()].InNodes.Contains("MyTable"));
        Assert.IsTrue(visitor.Graph[visitor.Graph.Keys.First()].OutNodes.Contains("MyStoredProcedure"));
    }

    [TestMethod]
    public void Visit_InvalidDynamicSql_ShouldNotThrow()
    {
        // Arrange
        var tables = new List<string>();
        var sqlObjects = new List<ISqlObject>();
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        var sql = "EXEC('INVALID SQL');";
        var fragment = ParseSql(sql);

        // Act & Assert
        try
        {
            fragment.Accept(visitor);
        }
        catch
        {
            Assert.Fail("Exception was thrown for invalid dynamic SQL.");
        }
    }

    [TestMethod]
    public void Visit_CaseInsensitiveObjectNames_ShouldMatchCorrectly()
    {
        // Arrange
        var tables = new List<string> { "mytable" };
        var sqlObjects = new List<ISqlObject>
        {
            new StoredProcedure { Name = "mystoredprocedure", Definition = "..." }
        };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "EXEC MyStoredProcedure; SELECT * FROM MyTable;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("mytable"));
        Assert.IsTrue(visitor.Graph.ContainsKey("mystoredprocedure"));
    }

    [TestMethod]
    public void Visit_InsertStatement_ShouldAddTableToGraph()
    {
        // Arrange
        var tables = new List<string> { "MyTable" };
        var sqlObjects = new List<ISqlObject>
    {
        new StoredProcedure { Name = "MyProcedure", Definition = "..." }
    };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "INSERT INTO MyTable (Column1) VALUES ('Value1');";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyTable"));
        Assert.IsTrue(visitor.Graph["MyTable"].InNodes.Contains("MyProcedure"));
        Assert.IsTrue(visitor.Graph["MyProcedure"].OutNodes.Contains("MyTable"));
    }

    [TestMethod]
    public void Visit_UpdateStatement_ShouldAddTableToGraph()
    {
        // Arrange
        var tables = new List<string> { "MyTable" };
        var sqlObjects = new List<ISqlObject>
    {
        new StoredProcedure { Name = "MyProcedure", Definition = "..." }
    };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "UPDATE MyTable SET Column1 = 'Value1' WHERE Column2 = 'Value2';";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyTable"));
        Assert.IsTrue(visitor.Graph["MyTable"].OutNodes.Contains("MyProcedure"));
        Assert.IsTrue(visitor.Graph["MyProcedure"].InNodes.Contains("MyTable"));
    }

    [TestMethod]
    public void Visit_DeleteStatement_ShouldAddTableToGraph()
    {
        // Arrange
        var tables = new List<string> { "MyTable" };
        var sqlObjects = new List<ISqlObject>
    {
        new StoredProcedure { Name = "MyProcedure", Definition = "..." }
    };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "DELETE FROM MyTable WHERE Column1 = 'Value1';";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyTable"));
        Assert.IsTrue(visitor.Graph["MyTable"].OutNodes.Contains("MyProcedure"));
        Assert.IsTrue(visitor.Graph["MyProcedure"].InNodes.Contains("MyTable"));
    }

    [TestMethod]
    public void Visit_MergeStatement_ShouldAddTableToGraph()
    {
        // Arrange
        var tables = new List<string> { "MyTable" };
        var sqlObjects = new List<ISqlObject>
    {
        new StoredProcedure { Name = "MyProcedure", Definition = "..." }
    };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "MERGE INTO MyTable AS Target USING AnotherTable AS Source ON Target.Id = Source.Id WHEN MATCHED THEN UPDATE SET Target.Column1 = Source.Column1;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyTable"));
        Assert.IsTrue(visitor.Graph["MyTable"].OutNodes.Contains("MyProcedure"));
        Assert.IsTrue(visitor.Graph["MyProcedure"].InNodes.Contains("MyTable"));
    }

    [TestMethod]
    public void Visit_SelectStatement_WithFunctionCall_ShouldAddFunctionToGraph()
    {
        // Arrange
        var tables = new List<string>();
        var sqlObjects = new List<ISqlObject>
    {
        new SqlFunction { Name = "MyFunction", Definition = "..." }
    };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First());
        var sql = "SELECT dbo.MyFunction();";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyFunction"));
        Assert.IsTrue(visitor.Graph["MyFunction"].OutNodes.Contains("MyFunction"));
    }

    [TestMethod]
    public void Visit_EmptyGraph_ShouldNotThrow()
    {
        // Arrange
        var tables = new List<string>();
        var sqlObjects = new List<ISqlObject>();
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        var sql = "";

        // Act & Assert
        try
        {
            var fragment = ParseSql(sql);
            fragment.Accept(visitor);
        }
        catch
        {
            Assert.Fail("Exception was thrown for an empty SQL fragment.");
        }
    }

    [TestMethod]
    public void Visit_InvalidSql_ShouldNotThrow()
    {
        // Arrange
        var tables = new List<string>();
        var sqlObjects = new List<ISqlObject>();
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        var sql = "INVALID SQL";

        // Act & Assert
        try
        {
            var fragment = ParseSql(sql);
            fragment.Accept(visitor);
        }
        catch
        {
            Assert.Fail("Exception was thrown for invalid SQL.");
        }
    }

    [TestMethod]
    public void Visit_InsertStatement_WithFunctionCall_ShouldLinkSprocToFunction()
    {
        // Arrange
        var tables = new List<string>(); // No tables are referenced in this test
        var sqlObjects = new List<ISqlObject>
        {
            new StoredProcedure { Name = "MyProcedure", Definition = "..." },
            new SqlFunction { Name = "fn_GetUnwantedRequestStages", Definition = "..." }
        };
        var visitor = new DirectedCombinedVisitor(tables, sqlObjects);
        visitor.SetCurrentNode(sqlObjects.First()); // Set the current node to the stored procedure
        var sql = @"
            INSERT INTO @UnwantedStages ([ID]) 
            SELECT [ID] 
            FROM [hub2].[fn_GetUnwantedRequestStages];
        ";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("fn_GetUnwantedRequestStages"), "Function should be added to the graph.");
        Assert.IsTrue(visitor.Graph["fn_GetUnwantedRequestStages"].OutNodes.Contains("MyProcedure"), "Function should have an incoming relationship from the stored procedure.");
        Assert.IsTrue(visitor.Graph["MyProcedure"].InNodes.Contains("fn_GetUnwantedRequestStages"), "Stored procedure should have an outgoing relationship to the function.");
    }

    // --- New Test Cases ---

    [TestMethod]
    public void Visit_FunctionCall_InWhereClause_ShouldLinkCorrectly()
    {
        // Arrange
        var (visitor, currentNode) = SetupVisitor(
            tables: new List<string> { "Orders" },
            sqlObjects: new List<ISqlObject> { new SqlFunction { Name = "fn_CalculateDiscount", Definition = "..." } }
        );
        var sql = @"SELECT OrderID FROM Orders WHERE dbo.fn_CalculateDiscount(Price) > 10;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        // Data flow: fn_CalculateDiscount -> TestProc
        Assert.IsTrue(visitor.Graph.ContainsKey("fn_CalculateDiscount"), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey("Orders"), "Table node missing.");
        Assert.IsTrue(visitor.Graph["fn_CalculateDiscount"].OutNodes.Contains(currentNode.Name), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("fn_CalculateDiscount"), "Procedure should receive data FROM function.");
        // Also check table read: Orders -> TestProc
        Assert.IsTrue(visitor.Graph["Orders"].OutNodes.Contains(currentNode.Name), "Table should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("Orders"), "Procedure should receive data FROM table.");
    }

     [TestMethod]
    public void Visit_FunctionCall_InSetClause_ShouldLinkCorrectly()
    {
        // Arrange
        var (visitor, currentNode) = SetupVisitor(
            tables: new List<string> { "Products" },
            sqlObjects: new List<ISqlObject> { new SqlFunction { Name = "fn_GetNewPrice", Definition = "..." } }
        );
        var sql = @"UPDATE Products SET Price = dbo.fn_GetNewPrice(CategoryID) WHERE ProductID = 1;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        // Data flow: fn_GetNewPrice -> TestProc
        Assert.IsTrue(visitor.Graph.ContainsKey("fn_GetNewPrice"), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey("Products"), "Table node missing.");
        Assert.IsTrue(visitor.Graph["fn_GetNewPrice"].OutNodes.Contains(currentNode.Name), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("fn_GetNewPrice"), "Procedure should receive data FROM function.");
        // Data write: TestProc -> Products
        Assert.IsTrue(visitor.Graph[currentNode.Name].OutNodes.Contains("Products"), "Procedure should write data TO table.");
        Assert.IsTrue(visitor.Graph["Products"].InNodes.Contains(currentNode.Name), "Table should receive write FROM procedure.");
    }

     [TestMethod]
    public void Visit_FunctionCall_InValuesClause_ShouldLinkCorrectly()
    {
        // Arrange
        var (visitor, currentNode) = SetupVisitor(
            tables: new List<string> { "Logs" },
            sqlObjects: new List<ISqlObject> { new SqlFunction { Name = "fn_GetTimestamp", Definition = "..." } }
        );
        var sql = @"INSERT INTO Logs (LogTime, Message) VALUES (dbo.fn_GetTimestamp(), 'Test Log');";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        // Data flow: fn_GetTimestamp -> TestProc
        Assert.IsTrue(visitor.Graph.ContainsKey("fn_GetTimestamp"), "Function node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey("Logs"), "Table node missing.");
        Assert.IsTrue(visitor.Graph["fn_GetTimestamp"].OutNodes.Contains(currentNode.Name), "Function should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("fn_GetTimestamp"), "Procedure should receive data FROM function.");
        // Data write: TestProc -> Logs
        Assert.IsTrue(visitor.Graph[currentNode.Name].OutNodes.Contains("Logs"), "Procedure should write data TO table.");
        Assert.IsTrue(visitor.Graph["Logs"].InNodes.Contains(currentNode.Name), "Table should receive write FROM procedure.");
    }

    [TestMethod]
    public void Visit_TableRead_InUpdateFromClause_ShouldLinkCorrectly()
    {
        // Arrange
        var (visitor, currentNode) = SetupVisitor(
            tables: new List<string> { "TargetTable", "SourceTable" }
        );
        var sql = @"UPDATE t SET t.Col1 = s.Col1 FROM TargetTable t JOIN SourceTable s ON t.ID = s.ID;";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("TargetTable"), "TargetTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey("SourceTable"), "SourceTable node missing.");
        // Data write: TestProc -> TargetTable
        Assert.IsTrue(visitor.Graph[currentNode.Name].OutNodes.Contains("TargetTable"), "Procedure should write TO TargetTable.");
        Assert.IsTrue(visitor.Graph["TargetTable"].InNodes.Contains(currentNode.Name), "TargetTable should receive write FROM procedure.");
        // Data flow: SourceTable -> TestProc
        Assert.IsTrue(visitor.Graph["SourceTable"].OutNodes.Contains(currentNode.Name), "SourceTable should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("SourceTable"), "Procedure should receive data FROM SourceTable.");
    }

     [TestMethod]
    public void Visit_TableRead_InMergeUsingClause_ShouldLinkCorrectly()
    {
        // Arrange
        var (visitor, currentNode) = SetupVisitor(
            tables: new List<string> { "TargetTable", "SourceTable" }
        );
        var sql = @"
            MERGE TargetTable AS T
            USING SourceTable AS S
            ON (T.ID = S.ID)
            WHEN MATCHED THEN UPDATE SET T.Name = S.Name
            WHEN NOT MATCHED BY TARGET THEN INSERT (ID, Name) VALUES (S.ID, S.Name);";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("TargetTable"), "TargetTable node missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey("SourceTable"), "SourceTable node missing.");
        // Data write: TestProc -> TargetTable
        Assert.IsTrue(visitor.Graph[currentNode.Name].OutNodes.Contains("TargetTable"), "Procedure should write TO TargetTable.");
        Assert.IsTrue(visitor.Graph["TargetTable"].InNodes.Contains(currentNode.Name), "TargetTable should receive write FROM procedure.");
        // Data flow: SourceTable -> TestProc
        Assert.IsTrue(visitor.Graph["SourceTable"].OutNodes.Contains(currentNode.Name), "SourceTable should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("SourceTable"), "Procedure should receive data FROM SourceTable.");
    }

     [TestMethod]
    public void Visit_MultipleDependencies_ShouldLinkAll()
    {
        // Arrange
        var (visitor, currentNode) = SetupVisitor(
            tables: new List<string> { "TableA", "TableB" },
            sqlObjects: new List<ISqlObject> {
                new SqlFunction { Name = "fn_Func1", Definition = "..." },
                new StoredProcedure { Name = "sp_Proc2", Definition = "..." }
            }
        );
        var sql = @"
            SELECT a.*, dbo.fn_Func1(b.ID)
            FROM TableA a JOIN TableB b ON a.ID = b.RefID;
            EXEC sp_Proc2 @Param = 1;
            INSERT INTO TableA (Col) VALUES (1);";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("TableA"), "TableA missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey("TableB"), "TableB missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey("fn_Func1"), "fn_Func1 missing.");
        Assert.IsTrue(visitor.Graph.ContainsKey("sp_Proc2"), "sp_Proc2 missing.");

        // Data flow: TableA -> TestProc
        Assert.IsTrue(visitor.Graph["TableA"].OutNodes.Contains(currentNode.Name));
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("TableA"));
        // Data flow: TableB -> TestProc
        Assert.IsTrue(visitor.Graph["TableB"].OutNodes.Contains(currentNode.Name));
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("TableB"));
         // Data flow: fn_Func1 -> TestProc
        Assert.IsTrue(visitor.Graph["fn_Func1"].OutNodes.Contains(currentNode.Name));
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("fn_Func1"));
        // Dependency: TestProc -> sp_Proc2
        Assert.IsTrue(visitor.Graph[currentNode.Name].OutNodes.Contains("sp_Proc2"));
        Assert.IsTrue(visitor.Graph["sp_Proc2"].InNodes.Contains(currentNode.Name));
         // Data write: TestProc -> TableA
        Assert.IsTrue(visitor.Graph[currentNode.Name].OutNodes.Contains("TableA"));
        Assert.IsTrue(visitor.Graph["TableA"].InNodes.Contains(currentNode.Name));

        // Check counts (adjust based on exact logic for duplicates)
        Assert.AreEqual(2, visitor.Graph[currentNode.Name].OutNodes.Count, "TestProc OutNodes count incorrect."); // To TableA (write), To sp_Proc2 (call)
        Assert.AreEqual(3, visitor.Graph[currentNode.Name].InNodes.Count, "TestProc InNodes count incorrect."); // From TableA, TableB, fn_Func1
        Assert.AreEqual(1, visitor.Graph["TableA"].OutNodes.Count, "TableA OutNodes count incorrect."); // To TestProc
        Assert.AreEqual(1, visitor.Graph["TableA"].InNodes.Count, "TableA InNodes count incorrect."); // From TestProc (write)

    }

     [TestMethod]
    public void Visit_TableAlias_ShouldLinkCorrectly()
    {
        // Arrange
        var (visitor, currentNode) = SetupVisitor(
            tables: new List<string> { "Customers" }
        );
        var sql = @"SELECT c.Name FROM Customers c WHERE c.City = 'London';";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("Customers"), "Customers node missing.");
        // Data flow: Customers -> TestProc
        Assert.IsTrue(visitor.Graph["Customers"].OutNodes.Contains(currentNode.Name), "Table should flow data TO procedure.");
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("Customers"), "Procedure should receive data FROM table.");
    }

     [TestMethod]
    public void Visit_SchemaQualifiedFunction_ShouldLinkCorrectly()
    {
        // Arrange
        var (visitor, currentNode) = SetupVisitor(
             sqlObjects: new List<ISqlObject> { new SqlFunction { Name = "MyFunction", Definition = "..." } }
        );
        // Assuming GetSqlObjectByName ignores schema, this should still match "MyFunction"
        var sql = @"SELECT OtherColumn FROM AnotherTable WHERE SomeColumn = dbo.MyFunction();";
        var fragment = ParseSql(sql);

        // Act
        fragment.Accept(visitor);

        // Assert
        Assert.IsTrue(visitor.Graph.ContainsKey("MyFunction"), "Function node missing.");
        // Data flow: MyFunction -> TestProc
        Assert.IsTrue(visitor.Graph["MyFunction"].OutNodes.Contains(currentNode.Name));
        Assert.IsTrue(visitor.Graph[currentNode.Name].InNodes.Contains("MyFunction"));
    }

     // --- Review and potentially fix assertions in existing tests ---
     // Example: Reviewing Visit_UpdateStatement_ShouldAddTableToGraph
     [TestMethod]
     public void Visit_UpdateStatement_ShouldLinkCorrectly_ReviewAssertion()
     {
         // Arrange
         var (visitor, currentNode) = SetupVisitor(
             tables: new List<string> { "MyTable" }
         );
         var sql = "UPDATE MyTable SET Column1 = 'Value1' WHERE Column2 = 'Value2';";
         var fragment = ParseSql(sql);

         // Act
         fragment.Accept(visitor);

         // Assert
         Assert.IsTrue(visitor.Graph.ContainsKey("MyTable"));
         // Correct Assertion based on AddDataWriteDependency: Modifier -> Target
         Assert.IsTrue(visitor.Graph[currentNode.Name].OutNodes.Contains("MyTable"), "Procedure should write data TO table.");
         Assert.IsTrue(visitor.Graph["MyTable"].InNodes.Contains(currentNode.Name), "Table should receive write FROM procedure.");
         // The original assertions were likely based on a different dependency interpretation
         // Assert.IsTrue(visitor.Graph["MyTable"].OutNodes.Contains("MyProcedure")); // Incorrect for Write
         // Assert.IsTrue(visitor.Graph["MyProcedure"].InNodes.Contains("MyTable")); // Incorrect for Write
     }
}
