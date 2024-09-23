using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Data.Types;

public class PrinterJob
{
    public int Id { get; set; }

    public PrinterJobState State { get; set; }

    public string Name { get; set; }

    public string DocAttr { get; set; }

    public string DocNewAttr { get; set; }

    public byte[] DocData { get; set; }

    public DateTime Created { get; set; }

    public DateTime? Processed { get; set; }

    public DateTime? Completed { get; set; }
}
