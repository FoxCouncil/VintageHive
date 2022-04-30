using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Processors.Intranet
{
    public class Headlines
    {
        public string Id { get; set; }

        public string Source { get; set; }

        public string Title { get; set; }

        public string Summary { get; set; }

        public DateTimeOffset Published { get; set; }
    }
}
