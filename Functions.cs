using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using System.Configuration;
using System.Threading;
using MTConnectSharp;

namespace MTConnectWebJob
{
    public class Functions
    {
        private static CancellationTokenSource tokenSource = new CancellationTokenSource();

        private static string[] GetActiveDevices(string baseUrl, string[] givenDevices)
        {
            IList<string> activeDevices = MTConnectClient.GetDeviceList(baseUrl);
            IList<string> devices = new List<string>();

            foreach (string device in givenDevices)
            {
                if (activeDevices.Contains(device)) devices.Add(device);
            }

            return devices.ToArray();
        }

        private static void CancelDataCollection() 
        {
            System.Console.Out.WriteLine("Cancelling data collection ... \n");
            tokenSource.Cancel();
        }

        [NoAutomaticTrigger]
        public static void CollectData()
        {
            string baseUrl = ConfigurationManager.AppSettings["BaseUrl"].ToString();
            string[] givenDevices = ConfigurationManager.AppSettings["Devices"].ToString().Split(new char[] { ',' }).ToArray();
            string[] givenTags = ConfigurationManager.AppSettings["Tags"].ToString().Split(new char[] { ',' }).ToArray();
            int interval = Convert.ToInt32(ConfigurationManager.AppSettings["SampleInterval"]);
            string storageAccount = ConfigurationManager.AppSettings["StorageAccount"].ToString();

            System.Console.Out.WriteLine("Getting list of active devices ...\n");
            string[] devices = GetActiveDevices(baseUrl, givenDevices);

            for (int i = 0; i < devices.Length; i++)
            {
                System.Console.Out.Write("{0}{1}", devices[i], i == (devices.Length - 1) ?  String.Empty : ", ");
            }

            System.Console.Out.WriteLine("\n\nGetting data from active devices ...\n");

            IList<Task> collectionTasks = new List<Task>();
            CancellationToken cancellationToken = tokenSource.Token;

            foreach (string device in devices)
            {
                collectionTasks.Add(Task.Factory.StartNew(() =>
                {
                    new DataCollector(baseUrl, device, givenTags, interval, storageAccount).Start(cancellationToken);
                }, cancellationToken));
            }
            
            Task.WaitAll(collectionTasks.ToArray());
        }
    }
}
