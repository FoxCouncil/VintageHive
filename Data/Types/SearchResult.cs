using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Data.Types;

internal class SearchResult
{
    public string Title { get; set; }

    public string Abstract { get; set; }

    public Uri Uri { get; set; }

    public string UriDisplay { get; set; }
}
