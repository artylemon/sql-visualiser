namespace SqlVisualiserWebAppTest;
using System.Collections.Generic;
using SqlVisualiserWebApp.Services;
using SqlVisualiserWebApp.Models;
using SqlVisualiserWebApp.Models.Enums;
using SqlVisualiserWebApp.Models.Interfaces;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.IO;

[TestClass]
public class DirectedCombinedVisitorTests
{
    private TSqlFragment ParseSql(string sql)
    {
        var parser = new TSql150Parser(false);
        using (var reader = new StringReader(sql))
        {
            return parser.Parse(reader, out var errors);
        }
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

}
