using PyperSearchMvcWebRole.Models;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Gma.DataStructures.StringSearch;
using PagedList;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PyperSearchMvcWebRole.Controllers
{
    public class SearchController : Controller
    {
        // GET: Search
        private PatriciaTrie<string> trie;
        private readonly char[] disallowedCharacters;
        private readonly List<string> stopwords;
        private readonly CloudStorageAccount storageAccount;
        private readonly CloudTableClient tableClient;

        private readonly CloudTable websitePageMasterTable;
        private readonly CloudTable domainTable;


        public SearchController()
        {
            storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("StorageConnectionString"));
            tableClient = storageAccount.CreateCloudTableClient();
            domainTable = tableClient.GetTableReference("DomainTable");
            websitePageMasterTable = tableClient.GetTableReference("WebsitePageMasterTable");

            domainTable.CreateIfNotExistsAsync(); // create if not exists
            websitePageMasterTable.CreateIfNotExistsAsync(); // create if not exists

            trie = (PatriciaTrie<string>)HttpRuntime.Cache.Get("trie");
            disallowedCharacters = new[] { '?', ',', ':', ';', '!', '&', '(', ')', '"' };
            stopwords = (List<string>)HttpRuntime.Cache["stopwords"];
        }

        /// <summary>
        /// Method to generate valid keywords from a 'query'
        /// </summary>
        /// <param name="query">string</param>
        /// <returns>List<string></returns>
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
        public async Task<ActionResult> Index(string query, int? pageNumber)
        {
            int pageSize = 10; // items per pages
            if (!pageNumber.HasValue)
            {
                pageNumber = 1;
            }
            ViewBag.Query = query;
            List<string> keywords = GetValidKeywords(query);
            // get all domain names
            var domainNames = domainTable.ExecuteQuery(
                    new TableQuery<DynamicTableEntity>()
                    .Select(new List<string> { "PartitionKey" })
                ).Select(x => x.PartitionKey);
            List<CloudTable> domainTableList = new List<CloudTable>(); // empty list for all possible tables
            // create table from retrieve domain names
            foreach (string name in domainNames)
            {
                CloudTable table = tableClient.GetTableReference(name);
                bool? exists = await table.ExistsAsync();
                if (exists != null && exists.Value == true)
                {
                    domainTableList.Add(table);
                }
            }
            ViewBag.Keywords = keywords;
            IEnumerable<WebsitePage> partialResults = Enumerable.Empty<WebsitePage>();
            foreach (CloudTable table in domainTableList) // now retrieve pages from those tables using the keywords
            {
                foreach (string keyword in keywords)
                {
                    string keywordEncoded = HttpUtility.UrlEncode(keyword);
                    var resultsUsingKeyword = table.ExecuteQuery(
                        new TableQuery<WebsitePage>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, keywordEncoded))
                        .Select(new List<string> { "RowKey", "Domain" })
                        );
                    partialResults = partialResults.Concat(resultsUsingKeyword);
                }
            }
            // order by row key frequency (based on keywords)
            partialResults = partialResults.GroupBy(r => r.RowKey).OrderByDescending(r => r.Count())
                .SelectMany(r => r)
                .GroupBy(r => r.RowKey)
                .Select(r => r.First());
            List<WebsitePage> finalResult = new List<WebsitePage>(); // final query result
            foreach (var page in partialResults)
            {
                TableOperation single = TableOperation.Retrieve<WebsitePage>(page.Domain, page.RowKey);
                var result = await websitePageMasterTable.ExecuteAsync(single);
                if (result.Result != null)
                {
                    finalResult.Add((WebsitePage)result.Result);
                }
            }
            return View(finalResult.ToPagedList((int)pageNumber, pageSize));
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