namespace SqlVisualiser;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.IO;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: SqlVisualiser <connectionString>");
            return;
        }

        var connectionString = args[0];

        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            connection.Open();

            // Retrieve all stored procedures
            string queryStoredProcedures = @"
                SELECT p.name, m.definition
                FROM sys.procedures p
                JOIN sys.sql_modules m ON p.object_id = m.object_id";

            SqlCommand command = new SqlCommand(queryStoredProcedures, connection);
            SqlDataReader reader = command.ExecuteReader();

            var procedures = new List<StoredProcedure>();

            while (reader.Read())
            {
                string procName = reader.GetString(0);
                string definition = reader.GetString(1);
                procedures.Add(new StoredProcedure { Name = procName, Definition = definition });
                Console.WriteLine($"Reading {procName}...");
            }

            reader.Close();

            // Retrieve all tables
            string queryTables = @"
                SELECT name
                FROM sys.tables";

            command = new SqlCommand(queryTables, connection);
            reader = command.ExecuteReader();

            var tables = new List<string>();

            while (reader.Read())
            {
                string tableName = reader.GetString(0);
                tables.Add(tableName);
                Console.WriteLine($"Reading {tableName}...");
            }

            reader.Close();

            // Analyze stored procedures to find which ones read or write to tables
            Dictionary<string, TableUsage> graph = new Dictionary<string, TableUsage>();

            Dictionary<string, ProcedureUsage> procedureGraph = new Dictionary<string, ProcedureUsage>();

            foreach (var procedure in procedures)
            {
                Console.WriteLine($"Analysing {procedure.Name}...");
                foreach (var table in tables)
                {
                    if (DoesProcedureInteractWithTable(procedure.Definition, table, out bool isRead, out bool isWrite))
                    {
                        if (!graph.ContainsKey(table))
                        {
                            graph[table] = new TableUsage { TableName = table };
                        }

                        if (isRead)
                            graph[table].Readers.Add(procedure.Name);

                        if (isWrite)
                            graph[table].Writers.Add(procedure.Name);
                    }
                }

                // Analyze procedure calls (detect if one procedure calls another)
                foreach (var targetProcedure in procedures)
                {
                    if (DoesProcedureCallAnother(procedure.Definition, targetProcedure.Name))
                    {
                        if (!procedureGraph.ContainsKey(procedure.Name))
                        {
                            procedureGraph[procedure.Name] = new ProcedureUsage { ProcedureName = procedure.Name };
                        }

                        if (!procedureGraph.ContainsKey(targetProcedure.Name))
                        {
                            procedureGraph[targetProcedure.Name] = new ProcedureUsage { ProcedureName = targetProcedure.Name };
                        }

                        procedureGraph[procedure.Name].CalledProcedures.Add(targetProcedure.Name);
                        procedureGraph[targetProcedure.Name].CallingProcedures.Add(procedure.Name);
                    }
                }
            }

            // Output the graph
            Console.WriteLine("Table Interactions:");
            foreach (var tableUsage in graph.Values)
            {
                Console.WriteLine($"Table: {tableUsage.TableName}");
                Console.WriteLine($"  Readers: {string.Join(", ", tableUsage.Readers)}");
                Console.WriteLine($"  Writers: {string.Join(", ", tableUsage.Writers)}");
                Console.WriteLine();
            }

            // Output the graph for procedure calls
            Console.WriteLine("Procedure Calls:");
            foreach (var procUsage in procedureGraph.Values)
            {
                Console.WriteLine($"Procedure: {procUsage.ProcedureName}");
                Console.WriteLine($"  Calls: {string.Join(", ", procUsage.CalledProcedures)}");
                Console.WriteLine($"  Called By: {string.Join(", ", procUsage.CallingProcedures)}");
                Console.WriteLine();
            }
        }
    }

    static bool DoesProcedureInteractWithTable(string procedureDefinition, string tableName, out bool isRead, out bool isWrite)
    {
        isRead = false;
        isWrite = false;

        TSql130Parser parser = new TSql130Parser(true);
        IList<ParseError> errors;
        TSqlFragment fragment = parser.Parse(new StringReader(procedureDefinition), out errors);

        if (errors != null && errors.Count > 0)
        {
            Console.WriteLine($"Parsing errors: {string.Join(", ", errors.Select(e => e.Message))}");
            return false;
        }

        var visitor = new TableUsageVisitor(tableName);
        fragment.Accept(visitor);

        isRead = visitor.IsRead;
        isWrite = visitor.IsWrite;

        return isRead || isWrite;
    }

    static bool DoesProcedureCallAnother(string procedureDefinition, string targetProcedure)
    {
        TSql130Parser parser = new TSql130Parser(true);
        IList<ParseError> errors;
        TSqlFragment fragment = parser.Parse(new StringReader(procedureDefinition), out errors);

        if (errors != null && errors.Count > 0)
        {
            Console.WriteLine($"Parsing errors: {string.Join(", ", errors.Select(e => e.Message))}");
            return false;
        }

        var visitor = new ProcedureCallVisitor(targetProcedure);
        fragment.Accept(visitor);

        return visitor.IsCalled;
    }
}