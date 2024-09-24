namespace SqlVisualiser;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: YourProgramName <connectionString> <tableName>");
            return;
        }

        string connectionString = args[0];
        string tableName = args[1];

        try
        {
            Dictionary<string, string> readProcedures;
            Dictionary<string, string> writeProcedures;

            AnalyzeStoredProcedures(connectionString, tableName, out readProcedures, out writeProcedures);

            Console.WriteLine("Stored Procedures that READ from the table:");
            foreach (var proc in readProcedures)
            {
                Console.WriteLine($"Procedure: {proc.Key}, Type of Read: {proc.Value}");
            }

            Console.WriteLine("\nStored Procedures that WRITE to the table:");
            foreach (var proc in writeProcedures)
            {
                Console.WriteLine($"Procedure: {proc.Key}, Type of Write: {proc.Value}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void AnalyzeStoredProcedures(string connectionString, string tableName, out Dictionary<string, string> readProcedures, out Dictionary<string, string> writeProcedures)
    {
        readProcedures = new Dictionary<string, string>();
        writeProcedures = new Dictionary<string, string>();

        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            connection.Open();

            // Query to get all procedures that reference the table in any way
            string query = @"
                SELECT 
                    p.name AS ProcedureName,
                    d.referenced_entity_name AS ReferencedEntity,
                    r.referencing_id,
                    r.referencing_minor_id
                FROM 
                    sys.procedures p
                JOIN 
                    sys.dm_sql_referencing_entities(QUOTENAME(@TableName), 'OBJECT') d ON p.object_id = d.referencing_id
                JOIN 
                    sys.sql_modules m ON p.object_id = m.object_id
                LEFT JOIN 
                    sys.dm_sql_referenced_entities(QUOTENAME(p.name), 'OBJECT') r ON p.object_id = r.referencing_id
                WHERE 
                    d.referenced_class_desc = 'OBJECT'
                ";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@TableName", tableName);

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string procedureName = reader["ProcedureName"].ToString()!;
                        string referencedEntity = reader["ReferencedEntity"].ToString()!;

                        // We can now check how the table is being accessed by analyzing dependency type

                        // For reads (SELECT queries)
                        if (IsReadOperation(referencedEntity))
                        {
                            if (!readProcedures.ContainsKey(procedureName))
                                readProcedures.Add(procedureName, "Read operation");
                        }

                        // For writes (INSERT, UPDATE, DELETE queries)
                        if (IsWriteOperation(referencedEntity))
                        {
                            if (!writeProcedures.ContainsKey(procedureName))
                                writeProcedures.Add(procedureName, "Write operation");
                        }
                    }
                }
            }
        }
    }

    static bool IsReadOperation(string referencedEntity)
    {
        // You can check further for read-specific logic, such as when it's part of SELECT queries.
        // For example, you can check referencing_minor_id for a "SELECT" or simply depend on the referenced entity
        return referencedEntity.ToLower().Contains("select");
    }

    static bool IsWriteOperation(string referencedEntity)
    {
        // You can check for write-specific operations like INSERT, UPDATE, DELETE
        return referencedEntity.ToLower().Contains("insert") ||
               referencedEntity.ToLower().Contains("update") ||
               referencedEntity.ToLower().Contains("delete");
    }
}

