using Gma.DataStructures.StringSearch;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace PyperSearchMvcWebRole
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            Initialize();
        }

        private void Initialize()
        {
            // connect to a storage account
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("StorageConnectionString"));
            // create a blob client
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            // access blob storage container
            CloudBlobContainer pyperSearchContainer = blobClient.GetContainerReference("pypersearch");
            // create if does not exists
            pyperSearchContainer.CreateIfNotExistsAsync();
            // access the actual file needed to be access (pagecount.csv, stopwords.csv)
            CloudBlockBlob pageCountsBlockBlob = pyperSearchContainer.GetBlockBlobReference("pagecounts.csv");
            CloudBlockBlob stopWordsBlockBlob = pyperSearchContainer.GetBlockBlobReference("stopwords.csv");
            var trie = new PatriciaTrie<string>();
            List<string> stopWords = new List<string>();
            StreamReader streamReader = new StreamReader(pageCountsBlockBlob.OpenRead());  // create a stream reader for pageCountBlockBlob
            string line;
            while ((line = streamReader.ReadLine()) != null)  // save lines to trie
            {
                string[] line_arr = line.Split(':');
                string title = line_arr.FirstOrDefault();
                try
                {
                    trie.Add(title.ToLower(), title);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    continue;
                }
            }
            HttpRuntime.Cache.Insert("trie", trie, null,
                Cache.NoAbsoluteExpiration, Cache.NoSlidingExpiration, CacheItemPriority.NotRemovable, null); // save trie to runtime cache
            streamReader.Close();
            streamReader = new StreamReader(stopWordsBlockBlob.OpenRead());  // create a stream reader for stopWordsBlockBlob        
            while ((line = streamReader.ReadLine()) != null) // add lines to stopWords
            {
                stopWords.Add(line);
            }
            streamReader.Close();
            HttpRuntime.Cache.Insert("stopwords", stopWords, null,
                Cache.NoAbsoluteExpiration, Cache.NoSlidingExpiration, CacheItemPriority.NotRemovable, null); // save stopWords list to runtime cache
        }
    }
}
