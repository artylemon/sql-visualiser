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
            }

            reader.Close();

            // Analyze stored procedures to find which ones read or write to tables
            Dictionary<string, TableUsage> graph = new Dictionary<string, TableUsage>();
            
            foreach (var procedure in procedures)
            {
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
            }

            // Output the graph
            foreach (var tableUsage in graph.Values)
            {
                Console.WriteLine($"Table: {tableUsage.TableName}");
                Console.WriteLine($"  Readers: {string.Join(", ", tableUsage.Readers)}");
                Console.WriteLine($"  Writers: {string.Join(", ", tableUsage.Writers)}");
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
}


class StoredProcedure
{
    public string Name { get; set; }
    public string Definition { get; set; }
}

class TableUsage
{
    public string TableName { get; set; }
    public List<string> Readers { get; set; } = [];
    public List<string> Writers { get; set; } = [];
}
