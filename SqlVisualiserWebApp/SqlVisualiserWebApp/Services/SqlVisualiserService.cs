namespace SqlVisualiserWebApp.Services
{
    using SqlVisualiserWebApp.Models;
    using System.Collections.Generic;
    using Microsoft.Data.SqlClient;
    using System;
    using Microsoft.SqlServer.TransactSql.ScriptDom;

    public class SqlVisualiserService
    {
        private readonly ILogger<SqlVisualiserService> _logger;

        public SqlVisualiserService(ILogger<SqlVisualiserService> logger)
        {
            _logger = logger;
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
                _logger.LogError(ex, "Error retrieving catalogs from the database.");
                throw;
            }

            return catalogs;
        }

        public List<StoredProcedure> GetStoredProcedures(string connectionString)
        {
            var procedures = new List<StoredProcedure>();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = @"
                        SELECT p.name, m.definition
                        FROM sys.procedures p
                        JOIN sys.sql_modules m ON p.object_id = m.object_id";

                    var command = new SqlCommand(query, connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string procName = reader.GetString(0);
                            string definition = reader.GetString(1);
                            procedures.Add(new StoredProcedure { Name = procName, Definition = definition });
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error retrieving stored procedures from the database.");
                throw new InvalidOperationException("Failed to retrieve stored procedures.", ex);
            }

            return procedures;
        }

        public List<string> GetTables(string connectionString)
        {
            var tables = new List<string>();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = "SELECT name FROM sys.tables";

                    var command = new SqlCommand(query, connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string tableName = reader.GetString(0);
                            tables.Add(tableName);
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error retrieving tables from the database.");
                throw new InvalidOperationException("Failed to retrieve tables.", ex);
            }

            return tables;
        }

        public Dictionary<string, GraphNode> BuildDatabaseGraph(string connectionString)
        {
            try
            {
                _logger.LogInformation("Starting database analysis.");

                // Step 1: Retrieve stored procedures and tables
                var storedProcedures = GetStoredProcedures(connectionString);
                var tables = GetTables(connectionString);

                // Step 2: Initialize the CombinedVisitor
                var procedureNames = storedProcedures.Select(sp => sp.Name).ToList();
                var combinedVisitor = new CombinedVisitor(tables, procedureNames);

                // Step 3: Create a single instance of TSql150Parser
                var parser = new TSql150Parser(false);

                // Step 4: Parse each stored procedure and build the graph
                foreach (var procedure in storedProcedures)
                {
                    combinedVisitor.SetCurrentProcedure(procedure.Name);

                    using (var reader = new StringReader(procedure.Definition))
                    {
                        var fragment = parser.Parse(reader, out var errors);

                        if (errors != null && errors.Count > 0)
                        {
                            _logger.LogWarning("Parsing errors in procedure {ProcedureName}: {Errors}", procedure.Name, string.Join(", ", errors.Select(e => e.Message)));
                            continue; // Skip this procedure if there are parsing errors
                        }

                        fragment.Accept(combinedVisitor);
                    }
                }

                _logger.LogInformation("Database analysis completed successfully.");
                return combinedVisitor.Graph;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during database analysis.");
                throw new InvalidOperationException("Database analysis failed.", ex);
            }
        }

        public Dictionary<string, DirectedGraphNode> BuildDirectedDatabaseGraph(string connectionString)
        {
            try
            {
                _logger.LogInformation($"Starting directed database analysis. With the conenction string {connectionString}");

                // Step 1: Retrieve stored procedures and tables
                var storedProcedures = GetStoredProcedures(connectionString);
                var tables = GetTables(connectionString);

                // Step 2: Initialize the DirectedCombinedVisitor
                var procedureNames = storedProcedures.Select(sp => sp.Name).ToList();
                var directedVisitor = new DirectedCombinedVisitor(tables, procedureNames);

                // Step 3: Create a single instance of TSql150Parser
                var parser = new TSql150Parser(false);

                // Step 4: Parse each stored procedure and build the directed graph
                foreach (var procedure in storedProcedures)
                {
                    directedVisitor.SetCurrentProcedure(procedure.Name);

                    using (var reader = new StringReader(procedure.Definition))
                    {
                        var fragment = parser.Parse(reader, out var errors);

                        if (errors != null && errors.Count > 0)
                        {
                            _logger.LogWarning("Parsing errors in procedure {ProcedureName}: {Errors}", procedure.Name, string.Join(", ", errors.Select(e => e.Message)));
                            continue; // Skip this procedure if there are parsing errors
                        }

                        fragment.Accept(directedVisitor);
                    }
                }

                _logger.LogInformation("Directed database analysis completed successfully.");
                return directedVisitor.Graph;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during directed database analysis.");
                throw new InvalidOperationException("Directed database analysis failed.", ex);
            }
        }
    }
}
