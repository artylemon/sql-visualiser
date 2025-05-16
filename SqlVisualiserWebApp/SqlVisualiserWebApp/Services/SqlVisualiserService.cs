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
        private readonly DirectedCombinedVisitor _visitor;

        public SqlVisualiserService(ILogger<SqlVisualiserService> logger, DirectedCombinedVisitor visitor)
        {
            this._logger = logger;
            this._visitor = visitor;
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
                ConnectTimeout = 10
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
                        LEFT JOIN sys.sql_modules m ON p.object_id = m.object_id
                        JOIN sys.schemas s ON p.schema_id = s.schema_id";

                    var command = new SqlCommand(query, connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string schemaName = reader.GetString(0);
                            string procName = reader.GetString(1);
                            string definition = reader.IsDBNull(2) ? null : reader.GetString(2);

                            if (string.IsNullOrWhiteSpace(definition))
                            {
                                this._logger.LogWarning("Stored procedure {Schema}.{Procedure} in catalog {Catalog} has no definition and will be skipped.",
                                    schemaName, procName, catalog);
                                continue; // Skip this stored procedure
                            }

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
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error parsing stored procedures from the database.");
                throw;
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
                    string query = @$"
                SELECT
                s.name AS SchemaName,
                t.name AS TableName,
                (
                    SELECT
                        CAST('CREATE TABLE [' AS NVARCHAR(MAX)) + s.name + '].[' + t.name + '] (' + CHAR(13) + CHAR(10) + ' ' +
                        STRING_AGG(
                            -- Cast each part of the column definition to NVARCHAR(MAX)
                            -- This is crucial because STRING_AGG will use the data type of its input expressions
                            CAST('[' AS NVARCHAR(MAX)) + c.name + '] ' +
                            TYPE_NAME(c.user_type_id) +
                            CASE
                                WHEN TYPE_NAME(c.user_type_id) IN ('varchar', 'char', 'varbinary', 'binary', 'nvarchar', 'nchar')
                                THEN '(' +
                                    CASE
                                        WHEN c.max_length = -1 THEN 'MAX'
                                        ELSE CAST(
                                            CASE
                                                WHEN TYPE_NAME(c.user_type_id) IN ('nchar', 'nvarchar')
                                                THEN c.max_length / 2
                                                ELSE c.max_length
                                            END AS VARCHAR(10) -- This cast is for the length value itself, not the whole string
                                        )
                                    END + ')'
                                ELSE ''
                            END +
                            CASE WHEN c.is_nullable = 0 THEN ' NOT NULL' ELSE ' NULL' END,
                            ',' + CHAR(13) + CHAR(10) + ' ' -- Delimiter with newline
                        ) WITHIN GROUP (ORDER BY c.column_id) -- It's good practice to order columns
                        + CHAR(13) + CHAR(10) + ')' -- Closing parenthesis for CREATE TABLE
                    FROM sys.columns c
                    WHERE c.object_id = t.object_id
                ) AS TableDefinition
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id;
            ";

                    var command = new SqlCommand(query, connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string schemaName = reader.GetString(0);
                            string tableName = reader.GetString(1);
                            string definition = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);

                            tables.Add(new SqlTable
                            {
                                Name = tableName,
                                Schema = schemaName,
                                Catalog = catalog,
                                Definition = definition
                            });
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

        public Dictionary<string, DirectedGraphNode> BuildDirectedDatabaseGraph(string dataSource, List<string> catalogs)
        {
            try
            {
                _logger.LogInformation($"Starting directed database analysis for catalogs: {string.Join(", ", catalogs)}");

                // Step 1: Retrieve all SQL objects from all catalogs
                var allSqlObjects = new List<ISqlObject>();
                foreach (var catalog in catalogs)
                {
                    var connectionString = BuildConnectionString(dataSource, catalog);
                    var sqlObjects = GetSqlObjects(connectionString, catalog);
                    allSqlObjects.AddRange(sqlObjects);
                }

                // Step 2: Use the injected DirectedCombinedVisitor and set up the graph
                _visitor.SetupGraph(allSqlObjects);

                // Step 3: Create a single instance of TSql150Parser
                var parser = new TSql150Parser(false);

                // Step 4: Parse each SQL object and build the directed graph
                foreach (var sqlObject in allSqlObjects)
                {
                    _visitor.SetCurrentNode(sqlObject);

                    using (var reader = new StringReader(sqlObject.Definition))
                    {
                        var fragment = parser.Parse(reader, out var errors);

                        if (errors != null && errors.Count > 0)
                        {
                            _logger.LogWarning("Parsing errors in {ObjectType} {ObjectName}: {Errors}",
                                sqlObject.Type, sqlObject.Name, string.Join(", ", errors.Select(e => e.Message)));
                            continue; // Skip this object if there are parsing errors
                        }

                        fragment.Accept(_visitor);
                    }
                }

                _logger.LogInformation($"Directed database analysis for catalogs {string.Join(", ", catalogs)} completed successfully.");
                return _visitor.Graph;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred during directed database analysis for catalogs {string.Join(", ", catalogs)}.");
                throw new InvalidOperationException($"Directed database analysis for catalogs {string.Join(", ", catalogs)} failed.", ex);
            }
        }
    }
}
