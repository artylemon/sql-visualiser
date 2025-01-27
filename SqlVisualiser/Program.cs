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

            // Create a combined visitor instance
            var combinedVisitor = new CombinedVisitor(tables, procedures.Select(p => p.Name).ToList());

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

                combinedVisitor.SetCurrentProcedure(procedure.Name);
                fragment.Accept(combinedVisitor);
            }

            // Output the graph
            Console.WriteLine("Graph:");
            foreach (var node in combinedVisitor.Graph.Values)
            {
                Console.WriteLine($"{node.Type}: {node.Name}");
                Console.WriteLine("  Adjacent Nodes: \n\t" + string.Join(",\n\t ", node.AdjacentNodes.Select(adjNode =>
                {
                    var adjNodeType = combinedVisitor.Graph[adjNode].Type;
                    return $"{adjNodeType}: {adjNode}";
                })));
                Console.WriteLine();
            }
        }
    }
}
