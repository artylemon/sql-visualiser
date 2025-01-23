namespace SqlVisualiser;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.IO;
using System.Linq;
using Models;
using Visitors;

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

            // Create a single parser instance
            TSql130Parser parser = new TSql130Parser(true);

            // Analyze stored procedures to find which ones read or write to tables and call other procedures
            Dictionary<string, TableUsage> graph = new Dictionary<string, TableUsage>();
            Dictionary<string, ProcedureUsage> procedureGraph = new Dictionary<string, ProcedureUsage>();

            foreach (var procedure in procedures)
            {
                Console.WriteLine($"Analysing {procedure.Name}...");

                IList<ParseError> errors;
                TSqlFragment fragment = parser.Parse(new StringReader(procedure.Definition), out errors);

                if (errors != null && errors.Count > 0)
                {
                    Console.WriteLine($"Parsing errors: {string.Join(", ", errors.Select(e => e.Message))}");
                    continue;
                }

                var tableUsageVisitor = new TableUsageVisitor(tables);
                var procedureCallVisitor = new ProcedureCallVisitor(procedures.Select(p => p.Name).ToList());

                fragment.Accept(tableUsageVisitor);
                fragment.Accept(procedureCallVisitor);

                foreach (var table in tableUsageVisitor.TableUsages)
                {
                    if (!graph.ContainsKey(table.Key))
                    {
                        graph[table.Key] = new TableUsage { TableName = table.Key };
                    }

                    if (table.Value.IsRead)
                        graph[table.Key].Readers.Add(procedure.Name);

                    if (table.Value.IsWrite)
                        graph[table.Key].Writers.Add(procedure.Name);
                }

                foreach (var calledProcedure in procedureCallVisitor.CalledProcedures)
                {
                    if (!procedureGraph.ContainsKey(procedure.Name))
                    {
                        procedureGraph[procedure.Name] = new ProcedureUsage { ProcedureName = procedure.Name };
                    }

                    if (!procedureGraph.ContainsKey(calledProcedure))
                    {
                        procedureGraph[calledProcedure] = new ProcedureUsage { ProcedureName = calledProcedure };
                    }

                    procedureGraph[procedure.Name].CalledProcedures.Add(calledProcedure);
                    procedureGraph[calledProcedure].CallingProcedures.Add(procedure.Name);
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
}
