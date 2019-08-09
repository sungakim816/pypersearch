using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace PyperSearchMvcWebRole.Models
{
    /// <summary>
    /// Class To implement Wikipedia Json data fetch using an API to an C# Object 
    /// </summary>
    public class WikipediaPage
    {
        public WikipediaPage()
        { }

        public string Title { get; set; }

        public string Description { get; set; }

        public string Link { get; set; }
    }
}