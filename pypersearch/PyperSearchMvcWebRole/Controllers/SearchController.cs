using PyperSearchMvcWebRole.Models;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Net;
using System.Web.Mvc;
using Gma.DataStructures.StringSearch;
using PagedList;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System;

namespace PyperSearchMvcWebRole.Controllers
{
    public class SearchController : Controller
    {
        // GET: Search
        private readonly PatriciaTrie<string> trie;
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

        private NbaStatistics GetNbaPlayerStats(string firstName, string lastName)
        {
            WebClient client = new WebClient();
            string url = string.Format("http://ec2-54-254-229-239.ap-southeast-1.compute.amazonaws.com/api/player/{0}/{1}", firstName, lastName);
            var result = client.DownloadString(url);
            Trace.TraceInformation(result);
            if (string.IsNullOrEmpty(result) || string.IsNullOrWhiteSpace(result))
            {
                return null;
            }
            var playerJson = JsonConvert.DeserializeObject<NbaStatistics>(result);
            return playerJson;
        }

        [HttpGet]
        [OutputCache(Duration = 60, VaryByParam = "*")]
        [Route("Search/")]
        [Route("Search/{query}")]
        [Route("Search/{query}/{pageNumber:regex(^[1-9]{0, 4}$)}")]
        public async Task<ActionResult> Index(string query, int? pageNumber)
        {
            if (string.IsNullOrEmpty(query))
            {
                return View(Enumerable.Empty<WebsitePage>().ToPagedList(1, 1));
            }
            int pageSize = 15; // items per pages
            pageNumber = !pageNumber.HasValue ? 1 : pageNumber; // page number
            ViewBag.Query = query;
            var queryList = query.ToLower().Split(' '); // create an array/list from the words found in the query
            List<string> keywords = GetValidKeywords(query); // get valid keywords
            try
            {
                ViewBag.NbaPlayer = GetNbaPlayerStats(queryList.First(), queryList[1]); // try to get nba player stats
            }
            catch (Exception)
            { }
            if (!keywords.Any()) // check if empty
            {
                keywords = query.ToLower().Split(' ').ToList(); // use the query as is.
            }
            ViewBag.Keywords = keywords; // store query for body snippet highligthing 
            var domainNames = domainTable.ExecuteQuery(  // get all indexed domain names
                    new TableQuery<DynamicTableEntity>()
                    .Select(new List<string> { "PartitionKey" })
                ).Select(x => x.PartitionKey); // select only partition key for better performance
            List<CloudTable> domainTableList = new List<CloudTable>(); // create list for all possible cloud tables
            foreach (string name in domainNames)  // create tables using retrieve domain names
            {
                CloudTable table = tableClient.GetTableReference(name);
                bool? exists = await table.ExistsAsync();
                if (exists != null && exists.Value == true)
                {
                    domainTableList.Add(table);
                }
            }
            List<WebsitePage> partialResults = new List<WebsitePage>();
            foreach (CloudTable table in domainTableList) // retrieve pages from those tables using keyword as partition key
            {
                foreach (string keyword in keywords)
                {
                    string keywordEncoded = HttpUtility.UrlEncode(keyword);
                    TableQuery<WebsitePage> q = new TableQuery<WebsitePage>()
                        .Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, keywordEncoded))
                         .Select(new List<string> { "RowKey", "Domain" });
                    TableContinuationToken continuationToken = null;
                    do
                    {
                        TableQuerySegment<WebsitePage> segmentResult = await table
                            .ExecuteQuerySegmentedAsync(q, continuationToken);
                        continuationToken = segmentResult.ContinuationToken;
                        partialResults.AddRange(segmentResult);
                    } while (continuationToken != null);
                }
            }
            var partial = partialResults // ranking based on keyword matches (keyword = rowKey)
                .GroupBy(r => r.RowKey) // group by row key
                .OrderByDescending(r => r.Count()) // count occurances 
                .SelectMany(r => r) // select all
                .GroupBy(r => r.RowKey) // group again
                .Select(r => r.FirstOrDefault()).Take(50); // select distinct value and limit result to 'n'
            List<WebsitePage> finalResult = new List<WebsitePage>(); // final result
            foreach (var page in partial)
            {
                TableOperation single = TableOperation.Retrieve<WebsitePage>(page.Domain, page.RowKey);
                var result = await websitePageMasterTable.ExecuteAsync(single);
                if (result.Result != null)
                {
                    finalResult.Add((WebsitePage)result.Result);
                }
            }
            return View(finalResult.OrderByDescending(x => x.Clicks).ToPagedList((int)pageNumber, pageSize));
        }

        [HttpGet]
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

        [HttpGet]
        [Route("Search/IncrementClickRank")]
        [Route("Search/Increment/Click/{partitionkey}/{rowkey}")]
        public async Task<ActionResult> IncrementClickRank(string partitionkey, string rowkey)
        {
            TableOperation single = TableOperation.Retrieve<WebsitePage>(partitionkey, rowkey);
            var page = await websitePageMasterTable.ExecuteAsync(single);
            if (page == null)
            {
                return new EmptyResult();
            }
            var result = page.Result as WebsitePage;
            result.Clicks += 1;
            TableOperation update = TableOperation.Merge(result);
            await websitePageMasterTable.ExecuteAsync(update);
            return new EmptyResult();
        }

        [HttpGet]
        [Route("Search/UpdateQuerySuggestions")]
        [Route("Search/Update/Suggestions/{query}")]
        public ActionResult UpdateQuerySuggestions(string query)
        {
            if (!trie.Retrieve(query.ToLower()).Any())
            {
                trie.Add(query.ToLower(), query);
                HttpRuntime.Cache["trie"] = trie;
            }
            return new EmptyResult();
        }
    }
}