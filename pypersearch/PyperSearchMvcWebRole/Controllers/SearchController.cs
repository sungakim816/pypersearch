using PyperSearchMvcWebRole.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Gma.DataStructures.StringSearch;
using PagedList;
using System.Diagnostics;

namespace PyperSearchMvcWebRole.Controllers
{
    public class SearchController : Controller
    {
        // GET: Search
        PatriciaTrie<string> trie;
        readonly char[] disallowedCharacters;
        List<string> stopwords;
        public SearchController()
        {
            trie = (PatriciaTrie<string>)HttpRuntime.Cache.Get("trie");
            disallowedCharacters = new[] { '?', ',', ':', ';', '!', '&', '(', ')', '"' };
            stopwords = (List<string>)HttpRuntime.Cache["stopwords"];
        }

        private List<string> GetValidKeywords(string query)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrWhiteSpace(query))
            {
                return new List<string>();
            }
            IEnumerable<string> keywords = query.ToLower().Split(' ').AsEnumerable(); // split 'query' into 'keywords'
            keywords = keywords
                .Where(k => k.Length >= 2 && !stopwords.Contains(k.Trim()) && k.Any(c => char.IsLetter(c)))
                .Select(k => k.Trim('\'').Trim('"').Trim()); // remove unnecessary characters
            List<string> filteredKeywords = new List<string>(); // create an empty list for filtered keywords
            foreach (var word in keywords)
            {
                string keyword = word;
                keyword = new string(keyword.ToCharArray().Where(c => !disallowedCharacters.Contains(c)).ToArray()); // check for disallowed characters
                if (keyword.IndexOf("'s") >= 0)
                {
                    keyword = keyword.Remove(keyword.IndexOf("'s"), 2);  // remove 's
                }
                keyword = keyword.Trim('\'').Trim('"').Trim('?').TrimEnd('.'); // remove unncessary characters again
                if (keyword.Length < 2 || stopwords.Contains(keyword)) // check for length and for 'stopwords'
                {
                    continue;
                }
                filteredKeywords.Add(keyword);
            }
            return filteredKeywords;
        }

        [HttpGet]
        [OutputCache(Duration = 30)]
        [Route("Search/")]
        [Route("Search/{query}")]
        [Route("Search/{query}/{pageNumber:regex(^[1-9]{0, 4}$)}")]
        public ActionResult Index(string query, int? pageNumber)
        {
            int pageSize = 10; // items per pages
            if (!pageNumber.HasValue)
            {
                pageNumber = 1;
            }
            ViewBag.Query = query;
            List<string> keywords = GetValidKeywords(query);






            WikipediaSearchResults wikiPages = null;
            if (!string.IsNullOrEmpty(query))
            {
                wikiPages = new WikipediaSearchResults(query);
            }
            if (wikiPages == null)
            {
                return View();
            }
            return View(wikiPages.RetrievePages().ToPagedList((int)pageNumber, pageSize));

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
            var suggestions = trie.Retrieve(query.ToLower()).Take(10);
            return View(suggestions);
        }
    }
}