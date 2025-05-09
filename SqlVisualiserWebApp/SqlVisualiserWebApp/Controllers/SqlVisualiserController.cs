namespace SqlVisualiserWebApp.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Data.SqlClient;
    using SqlVisualiserWebApp.Models;
    using SqlVisualiserWebApp.Services;

    public class SqlVisualiserController : Controller
    {
        private readonly SqlVisualiserService _sqlVisualiserService;
        private readonly ILogger<SqlVisualiserController> _logger;

        public SqlVisualiserController(SqlVisualiserService sqlVisualiserService, ILogger<SqlVisualiserController> logger)
        {
            this._sqlVisualiserService = sqlVisualiserService ?? throw new ArgumentNullException(nameof(sqlVisualiserService));
            this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IActionResult Index()
        {
            this._logger.LogInformation("Rendering the Index view.");
            return this.View();
        }

        [HttpGet]
        public IActionResult Catalogs([FromQuery] string dataSource)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                this._logger.LogWarning("Data source is empty or null.");
                return this.Json(new { success = false, message = "Data source cannot be empty." });
            }

            try
            {
                this._logger.LogInformation("Fetching catalogs for the provided data source.");
                var connectionString = this._sqlVisualiserService.BuildConnectionString(dataSource, "master");
                var catalogs = this._sqlVisualiserService.GetCatalogs(connectionString);

                // Order catalogs alphabetically
                var orderedCatalogs = catalogs.OrderBy(c => c).ToList();

                return this.Json(new { success = true, catalogs = orderedCatalogs });
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "An error occurred while fetching catalogs.");
                return this.Json(new
                {
                    success = false,
                    message = "An error occurred while fetching catalogs.",
                    errorDetails = ex.Message
                });
            }
        }

        [HttpPost]
        public IActionResult ConstructDirectedCatalogGraph([FromBody] AnalysisRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.DataSource) || request.Catalogs == null || !request.Catalogs.Any())
            {
                this._logger.LogWarning("Data source or catalogs are empty or null.");
                return this.Json(new { success = false, message = "Data source and catalogs cannot be empty." });
            }

            try
            {
                this._logger.LogInformation("Starting directed database analysis for the provided data source and catalogs.");

                // Call the updated BuildDirectedDatabaseGraph method
                var graph = this._sqlVisualiserService.BuildDirectedDatabaseGraph(request.DataSource, request.Catalogs);

                this._logger.LogInformation("Directed database analysis completed successfully.");
                return this.Json(new
                {
                    success = true,
                    message = "Directed database analysis completed successfully.",
                    graph = graph
                });
            }
            catch (InvalidOperationException ex)
            {
                this._logger.LogError(ex, "An error occurred during directed database analysis.");
                return this.Json(new
                {
                    success = false,
                    message = "An error occurred during directed database analysis. Please check the logs for more details.",
                    errorDetails = ex.Message
                });
            }
            catch (Exception ex)
            {
                this._logger.LogCritical(ex, "An unexpected error occurred.");
                return this.Json(new
                {
                    success = false,
                    message = "An unexpected error occurred. Please contact support.",
                    errorDetails = ex.Message
                });
            }
        }
    }
}
