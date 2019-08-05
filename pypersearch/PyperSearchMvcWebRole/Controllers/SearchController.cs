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
        private PatriciaTrie<string> trie;
        private List<string> stopwords;
        private readonly char[] disallowedCharacters;
        private readonly CloudStorageAccount storageAccount;
        private readonly CloudTableClient tableClient;
        private readonly CloudTable websitePageMasterTable;
        private readonly CloudTable domainTable;

        /// <summary>
        /// Controller
        /// </summary>
        public SearchController()
        {
            storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("StorageConnectionString"));
            tableClient = storageAccount.CreateCloudTableClient();
            domainTable = tableClient.GetTableReference("DomainTable");
            websitePageMasterTable = tableClient.GetTableReference("WebsitePageMasterTable");

            domainTable.CreateIfNotExistsAsync(); // create if not exists
            websitePageMasterTable.CreateIfNotExistsAsync(); // create if not exists
            disallowedCharacters = new[] { '?', ',', ':', ';', '!', '&', '(', ')', '"' };
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
            stopwords = (List<string>)HttpRuntime.Cache["stopwords"];
            IEnumerable<string> keywords = query.ToLower().Split(' ').AsEnumerable(); // split 'query' into 'keywords'
            if (keywords.Count() == 1)
            {
                return keywords.ToList();
            }
            keywords = keywords
                .Where(k => k.Length >= 2 && !stopwords.Contains(k.Trim()) && k.Any(c => char.IsLetter(c)))
                .Select(k => k.Trim('\'').Trim('"').Trim()); // remove unnecessary characters
            List<string> filteredKeywords = new List<string>(); // create an empty list for filtered keywords
            foreach (var word in keywords)
            {
                string keyword = word;
                keyword = new string(keyword.ToCharArray().Where(c => !disallowedCharacters.Contains(c)).ToArray()); // check for disallowed characters
                while (keyword.IndexOf("'s") >= 0)
                {
                    keyword = keyword.Remove(keyword.IndexOf("'s"), 2);  // remove 's
                }
                keyword = keyword.Trim('\'').Trim('"').Trim('?').TrimEnd('.'); // remove unncessary characters again
                if (keyword.Length < 2 || stopwords.Contains(keyword)) // check for length and for 'stopwords'
                {
                    continue; // skip
                }
                filteredKeywords.Add(keyword); // add to list
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
            // url to PA 1 (api)
            string url = string.Format("http://ec2-54-254-229-239.ap-southeast-1.compute.amazonaws.com/api/player/{0}/{1}", firstName, lastName);
            var result = client.DownloadString(url);
            client.Dispose();
            if (string.IsNullOrEmpty(result) || string.IsNullOrWhiteSpace(result))
            {
                return null;
            }
            var playerJson = JsonConvert.DeserializeObject<NbaStatistics>(result); // create a json object from api response     
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
            pageNumber = pageNumber.HasValue ? pageNumber : 1; // page number
            ViewBag.Query = query; // save raw query 
            List<string> keywords = GetValidKeywords(query); // get valid keywords
            if (!keywords.Any()) // check if empty
            {
                keywords = query.ToLower().Split(' ').ToList(); // use the query as is.
            }
            if (keywords.Count() >= 2)  // try to get nba player stats
            {
                ViewBag.NbaPlayer = GetNbaPlayerStats(keywords.First(), keywords[1]);
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
                        CloudTable table = tableClient.GetTableReference(tableName); // get table reference
                        // segmented query
                        do
                        {
                            TableQuerySegment<WebsitePage> segmentResult = await table
                                .ExecuteQuerySegmentedAsync(q, continuationToken);
                            continuationToken = segmentResult.ContinuationToken;
                            partialResults.AddRange(segmentResult.Results);
                        } while (continuationToken != null);
                    }
                    catch (Exception)
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
                // query to retrieve single website page object from database with specific columns to improve performance
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
            trie = (PatriciaTrie<string>)HttpRuntime.Cache.Get("trie"); // retrieve trie from cache
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
            TableOperation single = TableOperation.Retrieve<WebsitePage>(partitionkey, rowkey); // retrieve element using partitionKey and rowKey
            var page = await websitePageMasterTable.ExecuteAsync(single); // execute command/query
            if (page == null)
            {
                return new EmptyResult(); // return empty result
            }
            var result = page.Result as WebsitePage; // cast Result Object to WebsitePage
            result.Clicks += 1; // increment Click Count
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

        /// <summary>
        /// Method for Google Like Instant Result
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("Search/InstantResult")]
        [Route("Search/Instant/Result/{query}")]
        public async Task<ActionResult> InstantResult(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return View(Enumerable.Empty<WebsitePage>());
            }
            List<string> keywords = GetValidKeywords(query); // get valid keywords
            if (!keywords.Any()) // check if empty
            {
                keywords = query.ToLower().Split(' ').ToList(); // use the query as is.
            }
            if (keywords.Count() >= 2)  // try to get nba player stats
            {
                ViewBag.NbaPlayer = GetNbaPlayerStats(keywords.First(), keywords[1]);
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
                        CloudTable table = tableClient.GetTableReference(tableName); // get table reference
                        do
                        {
                            TableQuerySegment<WebsitePage> segmentResult = await table
                                .ExecuteQuerySegmentedAsync(q, continuationToken);
                            continuationToken = segmentResult.ContinuationToken;
                            partialResults.AddRange(segmentResult.Results);
                        } while (continuationToken != null);
                    }
                    catch (Exception)
                    {
                        break; // table does not exists
                    }
                }
            }
            var partial = partialResults // ranking based on keyword matches (keyword = partitionKey)
                .GroupBy(r => r.RowKey) // group by row key
                .OrderByDescending(r => r.Count()) // order based on frequency
                .Select(r => r.FirstOrDefault()).Take(50); // get distinct values and take 'n' elements
            List<WebsitePage> finalResult = new List<WebsitePage>(); // final result
            foreach (var page in partial)
            {
                // query to return single website page object using partitionkey and rowkey
                TableQuery<WebsitePage> single = new TableQuery<WebsitePage>()
                    .Where(TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, page.Domain),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, page.RowKey)))
                    .Select(new string[] { "PartitionKey", "RowKey", "Url", "Title", "Content", "PublishDate", "Clicks" });
                var element = websitePageMasterTable.ExecuteQuery(single).First();
                if (element != null) // check if Website Page Object Exists
                {
                    finalResult.Add(element); // add to final list
                }
            }
            return View(finalResult.OrderByDescending(x => x.Clicks).Take(15)); // limit result to 15 only
        }
    }
}