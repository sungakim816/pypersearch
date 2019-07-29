using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
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
            InitializeTrie();
        }

        private void InitializeTrie()
        {
            // connect to a storage account
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("StorageConnectionString"));
            // create a blob client
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            // access blob storage container
            CloudBlobContainer pyperSearchContainer = blobClient.GetContainerReference("pypersearch");
            // create if does not exists
            pyperSearchContainer.CreateIfNotExistsAsync();
            // access the actual file needed to be access
            CloudBlockBlob pageCounts = pyperSearchContainer.GetBlockBlobReference("pagecount.csv");
            
            Dictionary<string, ushort> suggestions = new Dictionary<string, ushort>();
            // string filePath = Server.MapPath("~/App_Data/pagecount.csv");
            // FileStream fs = new FileStream(pageCounts.Uri.ToString(), FileMode.Open, FileAccess.Read);
            StreamReader streamReader = new StreamReader(pageCounts.OpenRead());
            string line;
            while ((line = streamReader.ReadLine()) != null)
            {
                string[] line_arr = line.Split(':');
                string title = line_arr.FirstOrDefault();
                ushort count = Convert.ToUInt16(line.LastOrDefault());
                suggestions.Add(title, count);
            }
            // fs.Close();
            HttpRuntime.Cache.Insert("trie", suggestions, null,
                Cache.NoAbsoluteExpiration, Cache.NoSlidingExpiration, CacheItemPriority.NotRemovable, null);
        }
    }
}
