namespace SqlVisualiser.Models;

using System.Collections.Generic;

class ProcedureUsage
{
    public string ProcedureName { get; set; }
    public List<string> CalledProcedures { get; set; } = [];
    public List<string> CallingProcedures { get; set; } = [];
}
