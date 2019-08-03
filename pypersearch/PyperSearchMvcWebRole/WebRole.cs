using System.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace PyperSearchMvcWebRole
{
    public class WebRole : RoleEntryPoint
    {
        public override bool OnStart()
        {
            // For information on handling configuration changes
            // see the MSDN topic at https://go.microsoft.com/fwlink/?LinkId=166357.
            RoleEnvironment.TraceSource.Switch.Level = SourceLevels.Information;
            return base.OnStart();
        }
    }
}
