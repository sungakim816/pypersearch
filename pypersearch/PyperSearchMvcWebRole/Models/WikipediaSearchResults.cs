using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace PyperSearchMvcWebRole.Models
{
    public class WikipediaSearchResults
    {
        private readonly List<WikipediaPage> pages;
        public string Query { get; set; }
        public String Format { get; set; }
        public String NameSpace { get; set; }
        public int Limit { get; set; }

        public WikipediaSearchResults()
        {
            Limit = 30;
            Format = "json";
            NameSpace = "*";
            Query = "Wikipedia";
            pages = new List<WikipediaPage>();
        }
        public WikipediaSearchResults(string query, int limit = 30, string format = "json", string nameSpace = "*")
        {
            if (format.ToLower() != "json")
            {
                Console.WriteLine("'json' is the only supported format.");
                throw new FormatException();
            }

            Limit = limit;
            Format = format;
            NameSpace = nameSpace;
            Query = query;
            pages = new List<WikipediaPage>();
        }

        public List<WikipediaPage> RetrievePages()
        {
            return RetrievePages(Query);
        }

        protected List<WikipediaPage> RetrievePages(string query)
        {
            var webClient = new System.Net.WebClient();
            string encodedUrlQuery = HttpUtility.UrlEncode(query);

            string url = String.Format(
                "https://en.wikipedia.org/w/api.php?action=opensearch&search={0}&limit={1}&format={2}&formatversion=2&namespace={3}",
                encodedUrlQuery, Limit, Format, NameSpace
                );

            var wikipediaResults = webClient.DownloadString(url);

            ArrayList queryResult = JsonConvert.DeserializeObject<ArrayList>(wikipediaResults);
            // query[0] is only the query (string) sent using wikipedia api
            JArray titles = (JArray)queryResult[1]; // Array of titles
            JArray descriptions = (JArray)queryResult[2];// corresponding descriptions of each titles, respectively
            JArray links = (JArray)queryResult[3]; // corresponding links each titles, respectively
            for (int i = 0; i < titles.Count; i++)
            {
                pages.Add(new WikipediaPage { Title = titles[i].ToString(), Description = descriptions[i].ToString(), Link = links[i].ToString() });
            }
            return pages;
        }


    }
}