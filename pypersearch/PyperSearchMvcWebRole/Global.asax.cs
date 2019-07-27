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
            Dictionary<string, short> suggestions = new Dictionary<string, short>();
            string filePath = Server.MapPath("~/App_Data/pagecount.csv");
            FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            StreamReader streamReader = new StreamReader(fs, Encoding.UTF8);
            string line;
            while ((line = streamReader.ReadLine()) != null)
            {
                string[] line_arr = line.Split(':');
                string title = line_arr.FirstOrDefault();
                short count = Convert.ToInt16(line.LastOrDefault());
                suggestions.Add(title, count);
            }
            fs.Close();
            HttpRuntime.Cache.Insert("suggestions", suggestions, null,
                Cache.NoAbsoluteExpiration, Cache.NoSlidingExpiration, CacheItemPriority.NotRemovable, null);
            
        }
    }
}
