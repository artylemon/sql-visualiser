namespace SqlVisualiserWebApp.Services
{
    using SqlVisualiserWebApp.Models;
    using System.Collections.Generic;
    using Microsoft.Data.SqlClient;
    using System;
    using Microsoft.SqlServer.TransactSql.ScriptDom;
    using SqlVisualiserWebApp.Models.Interfaces;

    public class SqlVisualiserService
    {
        private readonly ILogger<SqlVisualiserService> _logger;

        public SqlVisualiserService(ILogger<SqlVisualiserService> logger)
        {
            this._logger = logger;
        }

        public string BuildConnectionString(string dataSource, string initialCatalog)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                throw new ArgumentException("Data source cannot be null or empty.", nameof(dataSource));
            }

            if (string.IsNullOrWhiteSpace(initialCatalog))
            {
                throw new ArgumentException("Initial catalog cannot be null or empty.", nameof(initialCatalog));
            }

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = dataSource,
                InitialCatalog = initialCatalog,
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                PersistSecurityInfo = true,
                MinPoolSize = 5,
                MaxPoolSize = 120,
                ConnectTimeout = 5
            };

            return builder.ConnectionString;
        }

        public List<string> GetCatalogs(string connectionString)
        {
            var catalogs = new List<string>();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = "SELECT name FROM sys.databases";

                    var command = new SqlCommand(query, connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            catalogs.Add(reader.GetString(0));
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                this._logger.LogError(ex, "Error retrieving catalogs from the database.");
                throw;
            }

            return catalogs;
        }

        public List<StoredProcedure> GetStoredProcedures(string connectionString, string catalog)
        {
            var procedures = new List<StoredProcedure>();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = @"
                        SELECT s.name AS SchemaName, p.name AS ProcedureName, m.definition
                        FROM sys.procedures p
                        JOIN sys.sql_modules m ON p.object_id = m.object_id
                        JOIN sys.schemas s ON p.schema_id = s.schema_id";

                    var command = new SqlCommand(query, connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string schemaName = reader.GetString(0);
                            string procName = reader.GetString(1);
                            string definition = reader.GetString(2);

                            // Create a new StoredProcedure object and set the catalog
                            procedures.Add(new StoredProcedure
                            {
                                Schema = schemaName,
                                Name = procName,
                                Definition = definition,
                                Catalog = catalog
                            });
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                this._logger.LogError(ex, "Error retrieving stored procedures from the database.");
                throw new InvalidOperationException("Failed to retrieve stored procedures.", ex);
            }

            return procedures;
        }

        public List<SqlTable> GetTables(string connectionString, string catalog)
        {
            var tables = new List<SqlTable>();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = @"
                        SELECT s.name AS SchemaName, t.name AS TableName
                        FROM sys.tables t
                        JOIN sys.schemas s ON t.schema_id = s.schema_id";

                    var command = new SqlCommand(query, connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string schemaName = reader.GetString(0);
                            string tableName = reader.GetString(1);

                            // Create a new Table object and add it to the list
                            tables.Add(new SqlTable { Name = tableName, Schema = schemaName, Catalog = catalog });
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                this._logger.LogError(ex, "Error retrieving tables from the database.");
                throw new InvalidOperationException("Failed to retrieve tables.", ex);
            }

            return tables;
        }

        public List<SqlFunction> GetFunctions(string connectionString, string catalog)
        {
            var functions = new List<SqlFunction>();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = @"
                        SELECT s.name AS SchemaName, o.name AS FunctionName, m.definition
                        FROM sys.objects o
                        JOIN sys.sql_modules m ON o.object_id = m.object_id
                        JOIN sys.schemas s ON o.schema_id = s.schema_id
                        WHERE o.type IN ('FN', 'TF')";

                    var command = new SqlCommand(query, connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string schemaName = reader.GetString(0);
                            string functionName = reader.GetString(1);
                            string definition = reader.GetString(2);

                            // Create a new SqlFunction object and set the catalog
                            functions.Add(new SqlFunction
                            {
                                Schema = schemaName,
                                Name = functionName,
                                Definition = definition,
                                Catalog = catalog
                            });
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                this._logger.LogError(ex, "Error retrieving functions from the database.");
                throw new InvalidOperationException("Failed to retrieve functions.", ex);
            }

            return functions;
        }

        public List<ISqlObject> GetSqlObjects(string connectionString, string catalog)
        {
            var sqlObjects = new List<ISqlObject>();

            // Retrieve stored procedures and assign the catalog
            sqlObjects.AddRange(this.GetStoredProcedures(connectionString, catalog));

            // Retrieve functions and assign the catalog
            sqlObjects.AddRange(this.GetFunctions(connectionString, catalog));

            // Retrieve tables and assign the catalog
            sqlObjects.AddRange(this.GetTables(connectionString, catalog));

            return sqlObjects;
        }

        public Dictionary<string, DirectedGraphNode> BuildDirectedDatabaseGraph(string connectionString, string catalog)
        {
            try
            {
                this._logger.LogInformation($"Starting directed database analysis for catalog: {catalog}");

                // Step 1: Retrieve all SQL objects (tables, stored procedures, and functions)
                var sqlObjects = this.GetSqlObjects(connectionString, catalog);

                // Step 2: Initialize the DirectedCombinedVisitor
                var directedVisitor = new DirectedCombinedVisitor(sqlObjects);

                // Step 3: Create a single instance of TSql150Parser
                var parser = new TSql150Parser(false);

                // Step 4: Parse each SQL object and build the directed graph
                foreach (var sqlObject in sqlObjects)
                {
                    directedVisitor.SetCurrentNode(sqlObject);

                    using (var reader = new StringReader(sqlObject.Definition))
                    {
                        var fragment = parser.Parse(reader, out var errors);

                        if (errors != null && errors.Count > 0)
                        {
                            this._logger.LogWarning("Parsing errors in {ObjectType} {ObjectName}: {Errors}",
                                sqlObject.Type, sqlObject.Name, string.Join(", ", errors.Select(e => e.Message)));
                            continue; // Skip this object if there are parsing errors
                        }

                        fragment.Accept(directedVisitor);
                    }
                }

                this._logger.LogInformation($"Directed database analysis for catalog {catalog} completed successfully.");
                return directedVisitor.Graph;
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, $"An error occurred during directed database analysis for catalog {catalog}.");
                throw new InvalidOperationException($"Directed database analysis for catalog {catalog} failed.", ex);
            }
        }
    }
}
