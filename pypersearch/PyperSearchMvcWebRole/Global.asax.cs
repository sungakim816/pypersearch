using Gma.DataStructures.StringSearch;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        /// <summary>
        /// Initialize trie, stopwords etc. and save to server runtime cache
        /// </summary>
        private void Initialize()
        {
            // connect to a storage account
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("StorageConnectionString"));           
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient(); // create a blob client
            CloudBlobContainer pyperSearchContainer = blobClient.GetContainerReference("pypersearch"); // access blob storage container           
            pyperSearchContainer.CreateIfNotExistsAsync(); // create if does not exists
            // access the actual file needed to be access (pagecount.csv, stopwords.csv)
            CloudBlockBlob pageCountsBlockBlob = pyperSearchContainer.GetBlockBlobReference("pagecounts.csv");
            CloudBlockBlob stopWordsBlockBlob = pyperSearchContainer.GetBlockBlobReference("stopwords.csv");
            var trie = new PatriciaTrie<string>();
            List<string> stopWords = new List<string>();
            StreamReader streamReader = new StreamReader(pageCountsBlockBlob.OpenRead());  // create a stream reader for pageCountBlockBlob
            string line;
            while ((line = streamReader.ReadLine()) != null)  // save lines to trie
            {       
                try
                {
                    string[] line_arr = line.Split(':');
                    string pageTitle = line_arr.FirstOrDefault();
                    trie.Add(pageTitle.ToLower(), pageTitle);
                }
                catch (Exception ex)
                {
                    Trace.TraceInformation(ex.Message + " -TriNet");
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
