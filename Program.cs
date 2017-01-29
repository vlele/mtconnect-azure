using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;

namespace MTConnectWebJob
{
    class Program
    {
        static void Main()
        {
            JobHost host = new JobHost();
            host.Call(typeof(Functions).GetMethod("CollectData"));
        }
    }
}
