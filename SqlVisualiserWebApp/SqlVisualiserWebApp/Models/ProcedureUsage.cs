namespace SqlVisualiserWebApp.Models;

using System.Collections.Generic;

public class ProcedureUsage
{
    public string ProcedureName { get; set; }
    public List<string> CalledProcedures { get; set; } = [];
    public List<string> CallingProcedures { get; set; } = [];
}
