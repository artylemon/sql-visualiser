namespace SqlVisualiser;
using System.Collections.Generic;

class ProcedureUsage
{
    public string ProcedureName { get; set; }
    public List<string> CalledProcedures { get; set; } = new List<string>();
    public List<string> CallingProcedures { get; set; } = new List<string>();
}
