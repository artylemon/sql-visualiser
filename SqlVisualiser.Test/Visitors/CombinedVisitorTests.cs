using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlVisualiser.Visitors;

namespace SqlVisualiser.Tests
{
    [TestClass]
    public class CombinedVisitorTests
    {
        private CombinedVisitor _visitor;
        private TSql130Parser _parser;

        [TestInitialize]
        public void Setup()
        {
            var tables = new List<string> { "Table1", "Table2" };
            var procedures = new List<string> { "Procedure1", "Procedure2" };
            _visitor = new CombinedVisitor(tables, procedures);
            _parser = new TSql130Parser(true);
        }

        [TestMethod]
        public void Visit_NamedTableReference_AddsTableToGraph()
        {
            // Arrange
            var sql = "SELECT * FROM Table1";
            var fragment = ParseSql(sql);

            // Act
            _visitor.SetCurrentProcedure("Procedure1");
            fragment.Accept(_visitor);

            // Assert
            Assert.IsTrue(_visitor.Graph.ContainsKey("Table1"));
            Assert.IsTrue(_visitor.Graph["Table1"].AdjacentNodes.Contains("Procedure1"));
            Assert.IsTrue(_visitor.Graph["Procedure1"].AdjacentNodes.Contains("Table1"));
        }

        [TestMethod]
        public void Visit_InsertStatement_AddsTableToGraph()
        {
            // Arrange
            var sql = "INSERT INTO Table1 (Column1) VALUES (1)";
            var fragment = ParseSql(sql);

            // Act
            _visitor.SetCurrentProcedure("Procedure1");
            fragment.Accept(_visitor);

            // Assert
            Assert.IsTrue(_visitor.Graph.ContainsKey("Table1"));
            Assert.IsTrue(_visitor.Graph["Table1"].AdjacentNodes.Contains("Procedure1"));
            Assert.IsTrue(_visitor.Graph["Procedure1"].AdjacentNodes.Contains("Table1"));
        }

        [TestMethod]
        public void Visit_UpdateStatement_AddsTableToGraph()
        {
            // Arrange
            var sql = "UPDATE Table1 SET Column1 = 1";
            var fragment = ParseSql(sql);

            // Act
            _visitor.SetCurrentProcedure("Procedure1");
            fragment.Accept(_visitor);

            // Assert
            Assert.IsTrue(_visitor.Graph.ContainsKey("Table1"));
            Assert.IsTrue(_visitor.Graph["Table1"].AdjacentNodes.Contains("Procedure1"));
            Assert.IsTrue(_visitor.Graph["Procedure1"].AdjacentNodes.Contains("Table1"));
        }

        [TestMethod]
        public void Visit_DeleteStatement_AddsTableToGraph()
        {
            // Arrange
            var sql = "DELETE FROM Table1 WHERE Column1 = 1";
            var fragment = ParseSql(sql);

            // Act
            _visitor.SetCurrentProcedure("Procedure1");
            fragment.Accept(_visitor);

            // Assert
            Assert.IsTrue(_visitor.Graph.ContainsKey("Table1"));
            Assert.IsTrue(_visitor.Graph["Table1"].AdjacentNodes.Contains("Procedure1"));
            Assert.IsTrue(_visitor.Graph["Procedure1"].AdjacentNodes.Contains("Table1"));
        }

        [TestMethod]
        public void Visit_MergeStatement_AddsTableToGraph()
        {
            // Arrange
            var sql = "MERGE INTO Table1 USING Table2 ON Table1.Id = Table2.Id WHEN MATCHED THEN UPDATE SET Table1.Column1 = Table2.Column1;";
            var fragment = ParseSql(sql);

            // Act
            _visitor.SetCurrentProcedure("Procedure1");
            fragment.Accept(_visitor);

            // Assert
            Assert.IsTrue(_visitor.Graph.ContainsKey("Table1"));
            Assert.IsTrue(_visitor.Graph["Table1"].AdjacentNodes.Contains("Procedure1"));
            Assert.IsTrue(_visitor.Graph["Procedure1"].AdjacentNodes.Contains("Table1"));
        }

        [TestMethod]
        public void Visit_ExecuteStatement_AddsProcedureToGraph()
        {
            // Arrange
            var sql = "EXEC Procedure1";
            var fragment = ParseSql(sql);

            // Act
            _visitor.SetCurrentProcedure("Procedure2");
            fragment.Accept(_visitor);

            // Assert
            Assert.IsTrue(_visitor.Graph.ContainsKey("Procedure1"));
            Assert.IsTrue(_visitor.Graph["Procedure1"].AdjacentNodes.Contains("Procedure2"));
            Assert.IsTrue(_visitor.Graph["Procedure2"].AdjacentNodes.Contains("Procedure1"));
        }

        [TestMethod]
        public void Visit_SelectStatement_AddsProcedureToGraph()
        {
            // Arrange
            var sql = "SELECT * FROM dbo.Procedure1()";
            var fragment = ParseSql(sql);

            // Act
            _visitor.SetCurrentProcedure("Procedure2");
            fragment.Accept(_visitor);

            // Assert
            Assert.IsTrue(_visitor.Graph.ContainsKey("Procedure1"));
            Assert.IsTrue(_visitor.Graph["Procedure1"].AdjacentNodes.Contains("Procedure2"));
            Assert.IsTrue(_visitor.Graph["Procedure2"].AdjacentNodes.Contains("Procedure1"));
        }

        private TSqlFragment ParseSql(string sql)
        {
            IList<ParseError> errors;
            var fragment = _parser.Parse(new StringReader(sql), out errors);
            Assert.IsTrue(errors.Count == 0, "SQL parsing errors: " + string.Join(", ", errors));
            return fragment;
        }
    }
}
