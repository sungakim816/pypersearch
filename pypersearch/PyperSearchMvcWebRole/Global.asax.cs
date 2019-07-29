using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using PyperSearchMvcWebRole.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Gma.DataStructures.StringSearch;

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
            CloudBlockBlob pageCountsBlockBlob = pyperSearchContainer.GetBlockBlobReference("pagecount.csv");
            CloudBlockBlob stopWordsBlockBlob = pyperSearchContainer.GetBlockBlobReference("stopwords.csv");
            var trie = new PatriciaTrie<string>();
            List<string> stopWords = new List<string>();
            // create a stream reader for pageCountBlockBlob
            StreamReader streamReader = new StreamReader(pageCountsBlockBlob.OpenRead());
            string line;
            // save lines to trie

            //while ((line = streamReader.ReadLine()) != null)
            //{
            //    string[] line_arr = line.Split(':');
            //    string title = line_arr.FirstOrDefault();
            //    try
            //    {
            //        trie.Add(title.ToLower(), title);
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine(ex.Message);
            //        continue;
            //    }

            //}
            // save trie to runtime cache
            HttpRuntime.Cache.Insert("trie", trie, null,
                Cache.NoAbsoluteExpiration, Cache.NoSlidingExpiration, CacheItemPriority.NotRemovable, null);
            streamReader.Close();
            // create a stream reader for stopWordsBlockBlob
            streamReader = new StreamReader(stopWordsBlockBlob.OpenRead());
            // add lines to stopWords
            while ((line = streamReader.ReadLine()) != null)
            {
                stopWords.Add(line);
            }
            streamReader.Close();
            // save stopWords list to runtime cache
            HttpRuntime.Cache.Insert("stopwords", stopWords, null,
                Cache.NoAbsoluteExpiration, Cache.NoSlidingExpiration, CacheItemPriority.NotRemovable, null);
        }

    }
}
