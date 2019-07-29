using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace PyperSearchMvcWebRole.Controllers
{
    public class SearchController : Controller
    {
        // GET: Search
        private Dictionary<string, ushort> trie; 
        public SearchController()
        {
            trie = (Dictionary<string, ushort>)HttpRuntime.Cache.Get("trie");
        }

        [HttpGet]
        [OutputCache(Duration = 30, VaryByParam = "query")]
        [Route("Search/Index")]
        [Route("Search")]
        [Route("Search/{query}/{pageNumber:regex(^[1-9]{0, 4}$)}")]
        public ActionResult Index(string query, int? pageNumber)
        {
            if (!pageNumber.HasValue)
            {
                pageNumber = 1;
            }
            // remove all 'stop words'
            // split the query string 
            // use linq to finally built the query
            // return a result to the view
            ViewBag.Query = query;

            return View();
        }

        [HttpGet]
        [OutputCache(Duration = 30, VaryByParam = "query")]
        [Route("Search/Autocomplete")]
        [Route("Search/Autocomplete/{query}")]
        public ActionResult Autocomplete(string query)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrWhiteSpace(query))
            {
                return View(Enumerable.Empty<string>());
            }
            ViewBag.Query = query;
            var suggestions = trie
                .Where(node => node.Key.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(node => node.Value)
                .Take(10)
                .Select(node => node.Key);
            return View(suggestions);
        }
    }
}