namespace SqlVisualiserWebApp.Models
{
    public class AnalysisRequest
    {
        public string DataSource
        {
            get; set;
        }
        public List<string> Catalogs
        {
            get; set;
        }
    }
}
