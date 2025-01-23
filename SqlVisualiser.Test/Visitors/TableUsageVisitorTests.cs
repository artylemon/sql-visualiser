namespace SqlVisualiser.Test.Visitors
{
    using System.Collections.Generic;
    using System.IO;
    using Microsoft.SqlServer.TransactSql.ScriptDom;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SqlVisualiser.Visitors;

    [TestClass]
    public class TableUsageVisitorTests
    {
        private TSqlFragment ParseSql(string sql)
        {
            TSql130Parser parser = new TSql130Parser(true);
            IList<ParseError> errors;
            TSqlFragment fragment = parser.Parse(new StringReader(sql), out errors);

            if (errors != null && errors.Count > 0)
            {
                Assert.Fail($"Parsing errors: {string.Join(", ", errors.Select(e => e.Message))}");
            }

            return fragment;
        }

        [TestMethod]
        public void Test_SelectStatement_ReadsTable()
        {
            string sql = "SELECT * FROM dbo.MyTable";
            var fragment = this.ParseSql(sql);

            var visitor = new TableUsageVisitor(new[] { "MyTable" });
            fragment.Accept(visitor);

            Assert.IsTrue(visitor.TableUsages.ContainsKey("MyTable"));
            Assert.IsTrue(visitor.TableUsages["MyTable"].IsRead);
            Assert.IsFalse(visitor.TableUsages["MyTable"].IsWrite);
        }

        [TestMethod]
        public void Test_InsertStatement_WritesTable()
        {
            string sql = "INSERT INTO dbo.MyTable (Column1) VALUES (1)";
            var fragment = this.ParseSql(sql);

            var visitor = new TableUsageVisitor(new[] { "MyTable" });
            fragment.Accept(visitor);

            Assert.IsTrue(visitor.TableUsages.ContainsKey("MyTable"));
            Assert.IsFalse(visitor.TableUsages["MyTable"].IsRead);
            Assert.IsTrue(visitor.TableUsages["MyTable"].IsWrite);
        }

        [TestMethod]
        public void Test_UpdateStatement_WritesTable()
        {
            string sql = "UPDATE dbo.MyTable SET Column1 = 1";
            var fragment = this.ParseSql(sql);

            var visitor = new TableUsageVisitor(new[] { "MyTable" });
            fragment.Accept(visitor);

            Assert.IsTrue(visitor.TableUsages.ContainsKey("MyTable"));
            Assert.IsFalse(visitor.TableUsages["MyTable"].IsRead);
            Assert.IsTrue(visitor.TableUsages["MyTable"].IsWrite);
        }

        [TestMethod]
        public void Test_DeleteStatement_WritesTable()
        {
            string sql = "DELETE FROM dbo.MyTable";
            var fragment = this.ParseSql(sql);

            var visitor = new TableUsageVisitor(new[] { "MyTable" });
            fragment.Accept(visitor);

            Assert.IsTrue(visitor.TableUsages.ContainsKey("MyTable"));
            Assert.IsFalse(visitor.TableUsages["MyTable"].IsRead);
            Assert.IsTrue(visitor.TableUsages["MyTable"].IsWrite);
        }

        [TestMethod]
        public void Test_MergeStatement_WritesTable()
        {
            string sql = @"
                MERGE INTO dbo.MyTable AS target
                USING (SELECT 1 AS Column1) AS source
                ON (target.Column1 = source.Column1)
                WHEN MATCHED THEN
                    UPDATE SET Column1 = source.Column1
                WHEN NOT MATCHED THEN
                    INSERT (Column1) VALUES (source.Column1);";
            var fragment = this.ParseSql(sql);

            var visitor = new TableUsageVisitor(new[] { "MyTable" });
            fragment.Accept(visitor);

            Assert.IsTrue(visitor.TableUsages.ContainsKey("MyTable"));
            Assert.IsFalse(visitor.TableUsages["MyTable"].IsRead);
            Assert.IsTrue(visitor.TableUsages["MyTable"].IsWrite);
        }

        [TestMethod]
        public void Test_FunctionTableReference_ReadsTable()
        {
            string sql = "SELECT * FROM dbo.MyFunction()";
            var fragment = this.ParseSql(sql);

            var visitor = new TableUsageVisitor(new[] { "MyTable" });
            fragment.Accept(visitor);

            Assert.IsFalse(visitor.TableUsages.ContainsKey("MyTable"));
        }
    }
}
