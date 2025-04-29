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
            _sqlVisualiserService = sqlVisualiserService ?? throw new ArgumentNullException(nameof(sqlVisualiserService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IActionResult Index()
        {
            _logger.LogInformation("Rendering the Index view.");
            return View();
        }

        [HttpPost]
        public IActionResult GetCatalogs([FromBody] string dataSource)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                _logger.LogWarning("Data source is empty or null.");
                return Json(new { success = false, message = "Data source cannot be empty." });
            }

            try
            {
                _logger.LogInformation("Fetching catalogs for the provided data source.");
                var connectionString = _sqlVisualiserService.BuildConnectionString(dataSource, "master");
                var catalogs = _sqlVisualiserService.GetCatalogs(connectionString);

                return Json(new { success = true, catalogs });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching catalogs.");
                return Json(new
                {
                    success = false,
                    message = "An error occurred while fetching catalogs.",
                    errorDetails = ex.Message
                });
            }
        }

        [HttpPost]
        public IActionResult ConstructCatalogGraph([FromBody] AnalysisRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.DataSource) || string.IsNullOrWhiteSpace(request.Catalog))
            {
                _logger.LogWarning("Data source or catalog is empty or null.");
                return Json(new { success = false, message = "Data source and catalog cannot be empty." });
            }

            try
            {
                _logger.LogInformation("Starting database analysis for the provided data source and catalog.");
                var connectionString = _sqlVisualiserService.BuildConnectionString(request.DataSource, request.Catalog);
                var analysisResult = _sqlVisualiserService.BuildDatabaseGraph(connectionString);

                _logger.LogInformation("Database analysis completed successfully.");
                return Json(new
                {
                    success = true,
                    message = "Database analysis completed successfully.",
                    graph = analysisResult
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "An error occurred during database analysis.");
                return Json(new
                {
                    success = false,
                    message = "An error occurred during database analysis. Please check the logs for more details.",
                    errorDetails = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "An unexpected error occurred.");
                return Json(new
                {
                    success = false,
                    message = "An unexpected error occurred. Please contact support.",
                    errorDetails = ex.Message
                });
            }
        }

        [HttpPost]
        public IActionResult ConstructDirectedCatalogGraph([FromBody] AnalysisRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.DataSource) || string.IsNullOrWhiteSpace(request.Catalog))
            {
                _logger.LogWarning("Data source or catalog is empty or null.");
                return Json(new { success = false, message = "Data source and catalog cannot be empty." });
            }

            try
            {
                _logger.LogInformation("Starting directed database analysis for the provided data source and catalog.");
                var connectionString = _sqlVisualiserService.BuildConnectionString(request.DataSource, request.Catalog);
                var analysisResult = _sqlVisualiserService.BuildDirectedDatabaseGraph(connectionString);

                _logger.LogInformation("Directed database analysis completed successfully.");
                return Json(new
                {
                    success = true,
                    message = "Directed database analysis completed successfully.",
                    graph = analysisResult
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "An error occurred during directed database analysis.");
                return Json(new
                {
                    success = false,
                    message = "An error occurred during directed database analysis. Please check the logs for more details.",
                    errorDetails = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "An unexpected error occurred.");
                return Json(new
                {
                    success = false,
                    message = "An unexpected error occurred. Please contact support.",
                    errorDetails = ex.Message
                });
            }
        }
    }
}
