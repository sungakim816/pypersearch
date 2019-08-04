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

        /// <summary>
        /// Method to get NBA Player Statistics From PA 1
        /// </summary>
        /// <param name="firstName"></param>
        /// <param name="lastName"></param>
        /// <returns></returns>
        private NbaStatistics GetNbaPlayerStats(string firstName, string lastName)
        {
            WebClient client = new WebClient();
            string url = string.Format("http://ec2-54-254-229-239.ap-southeast-1.compute.amazonaws.com/api/player/{0}/{1}", firstName, lastName);
            var result = client.DownloadString(url);
            if (string.IsNullOrEmpty(result) || string.IsNullOrWhiteSpace(result))
            {
                return null;
            }
            var playerJson = JsonConvert.DeserializeObject<NbaStatistics>(result);
            client.Dispose();
            return playerJson;
        }

        /// <summary>
        /// Method for Searching and displaying Results
        /// </summary>
        /// <param name="query"></param>
        /// <param name="pageNumber"></param>
        /// <returns></returns>
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
            if (queryList.Count() >= 2)  // try to get nba player stats
            {
                ViewBag.NbaPlayer = GetNbaPlayerStats(queryList.First(), queryList[1]);
            }
            if (!keywords.Any()) // check if empty
            {
                keywords = query.ToLower().Split(' ').ToList(); // use the query as is.
            }
            ViewBag.Keywords = keywords; // store query for body snippet highligthing 
            // get all indexed domain names (just select partition key for better performance)
            var domainNames = domainTable.ExecuteQuery(new TableQuery<DynamicTableEntity>().Select(new List<string> { "PartitionKey" })).Select(x => x.PartitionKey);  
            List<WebsitePage> partialResults = new List<WebsitePage>();
            TableContinuationToken continuationToken = null;
            TableQuery<WebsitePage> tableQuery = new TableQuery<WebsitePage>().Select(new string[] { "Rowkey", "Domain" });
            foreach (string tableName in domainNames) // retrieve page objects from those tables using keyword as partition key
            {
                foreach (string keyword in keywords.Select(r => HttpUtility.UrlEncode(r))) // retrieve pages from those tables using keyword as partition key
                {
                    TableQuery<WebsitePage> q = tableQuery.Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, keyword));
                    try
                    {
                        CloudTable table = tableClient.GetTableReference(tableName);
                        do
                        {
                            TableQuerySegment<WebsitePage> segmentResult = await table
                                .ExecuteQuerySegmentedAsync(q, continuationToken);
                            continuationToken = segmentResult.ContinuationToken;
                            partialResults.AddRange(segmentResult.Results);
                        } while (continuationToken != null);
                    }
                    catch(Exception)
                    {
                        Trace.TraceInformation("Table '" + tableName + "' does not exist");
                        break;
                    }   
                }
            }
            var partial = partialResults // ranking based on keyword matches (keyword = partitionKey)
                .GroupBy(r => r.RowKey) // group by row key
                .OrderByDescending(r => r.Count()) // order based on frequency
                .Select(r => r.FirstOrDefault()).Take(100); // get distinct values and take 'n' elements
            List<WebsitePage> finalResult = new List<WebsitePage>(); // final result
            foreach (var page in partial)
            {
                TableQuery<WebsitePage> single = new TableQuery<WebsitePage>()
                    .Where(TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, page.Domain),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, page.RowKey)))
                    .Select(new string[] { "PartitionKey", "RowKey", "Url", "Title", "Content", "PublishDate", "Clicks" });
                var element = websitePageMasterTable.ExecuteQuery(single).First();
                if (element != null)
                {
                    finalResult.Add(element);
                }
            }
            return View(finalResult.OrderByDescending(x => x.Clicks).ToPagedList((int)pageNumber, pageSize));
        }

        /// <summary>
        /// Method for Autocomplete Functionality
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("Search/Autocomplete")]
        [Route("Search/Autocomplete/{query}")]
        public ActionResult Autocomplete(string query)
        {
            trie = (PatriciaTrie<string>)HttpRuntime.Cache.Get("trie");
            if (string.IsNullOrEmpty(query) || string.IsNullOrWhiteSpace(query))
            {
                return View(Enumerable.Empty<string>());
            }
            ViewBag.Query = query;
            var suggestions = trie.Retrieve(query.ToLower()).Take(10);
            return View(suggestions);
        }

        /// <summary>
        /// Method for Increasing Article/Page Rank Based on User Clicks on URL
        /// </summary>
        /// <param name="partitionkey"></param>
        /// <param name="rowkey"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Update Query Suggestions (Adding New Suggestions based on user searches)
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("Search/UpdateQuerySuggestions")]
        [Route("Search/Update/Suggestions/{query}")]
        public ActionResult UpdateQuerySuggestions(string query)
        {
            trie = (PatriciaTrie<string>)HttpRuntime.Cache.Get("trie");
            if (!trie.Retrieve(query.ToLower()).Any())
            {
                trie.Add(query.ToLower(), query);
                HttpRuntime.Cache["trie"] = trie;
            }
            return new EmptyResult();
        }
    }
}