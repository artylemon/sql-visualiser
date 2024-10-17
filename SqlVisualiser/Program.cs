namespace SqlVisualiser;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text.RegularExpressions;

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
        //string tableName = args[1];

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
                            procedureGraph[targetProcedure.Name] = new ProcedureUsage { ProcedureName = targetProcedure.Name};
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

        // Escape the table name to ensure special characters are handled
        string escapedTableName = Regex.Escape(tableName);

        // Define the optional schema pattern: (optional [dbo]. or dbo.)
        string optionalSchemaPattern = @"(\[dbo\]\.|dbo\.)?";

        // Construct regex patterns for read and write operations
        string patternRead = $@"\b(FROM|JOIN)\s+{optionalSchemaPattern}[\[\]]*{escapedTableName}[\[\]]*\b";
        string patternWrite = $@"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+{optionalSchemaPattern}[\[\]]*{escapedTableName}[\[\]]*\b";

        // Check for reading operations (SELECT FROM, JOIN)
        if (Regex.IsMatch(procedureDefinition, patternRead, RegexOptions.IgnoreCase))
        {
            isRead = true;
        }

        // Check for writing operations (INSERT INTO, UPDATE, DELETE FROM)
        if (Regex.IsMatch(procedureDefinition, patternWrite, RegexOptions.IgnoreCase))
        {
            isWrite = true;
        }

        return isRead || isWrite;
    }

    
    // Check if a stored procedure calls another procedure
    static bool DoesProcedureCallAnother(string procedureDefinition, string targetProcedure)
    {
        // Escape the procedure name for safe matching
        string escapedProcedureName = Regex.Escape(targetProcedure);

        // Match EXEC or EXECUTE followed by the procedure name (with optional schema)
        string patternCall = $@"\bEXEC(?:UTE)?\s+{escapedProcedureName}\b";
        
        return Regex.IsMatch(procedureDefinition, patternCall, RegexOptions.IgnoreCase);
    }
}


class StoredProcedure
{
    public string Name { get; set; }
    public string Definition { get; set; }
}


// Helper class to represent procedure interactions (calls to other procedures)
class ProcedureUsage
{
    public string ProcedureName { get; set; }
    public List<string> CalledProcedures { get; set; } = new List<string>();
    public List<string> CallingProcedures { get; set; } = new List<string>();
}

class TableUsage
{
    public string TableName { get; set; }
    public List<string> Readers { get; set; } = [];
    public List<string> Writers { get; set; } = [];
}
