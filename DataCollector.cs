using MTConnectSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MTConnectWebJob
{
    internal class DataCollector
    {
        #region Constructors

        public DataCollector(string baseUrl, string device, string[] tags, int interval, string storageAccount)
        {
            this.baseUrl = baseUrl;
            this.deviceName = device;
            this.tags = tags;
            this.interval = interval;
            this.storageAccount = storageAccount;

            dataDictionary = new Dictionary<string, StringBuilder>();

            for (int i = 0; i < tags.Length; i++)
            {
                dataDictionary.Add(tags[i], new StringBuilder());
            }
        }

        #endregion

        #region Private Fields

        private const string dataFormat = "{0},{1},{2},{3}";
        private const string headerFormat = "datetime,unixdatetime,value,sequence";

        private string baseUrl;
        private string deviceName;
        private string[] tags;
        private int interval;
        private string storageAccount;
        private IDictionary<string, StringBuilder> dataDictionary;
        private IDictionary<string, string> blobUrl;
        private MTConnectClient client;

        #endregion

        #region Public Methods

        public void Start(CancellationToken cancellationToken)
        {
            this.blobUrl = StorageAgent.GenerateBlobs(this.storageAccount, this.deviceName, this.tags);
            client = new MTConnectClient(baseUrl + deviceName, tags, interval, cancellationToken);
            
            client.ProbeCompleted += client_ProbeCompleted;
            client.DataItemChanged += client_DataItemChanged;
            client.DataItemsChanged += client_DataItemsChanged;
            client.Probe();
        }

        #endregion

        #region MTConnect Event Handlers

        void client_ProbeCompleted(object sender, EventArgs e)
        {
            var client = sender as MTConnectClient;
            client.StartStreaming();
        }

        void client_DataItemChanged(object sender, DataItemChangedEventArgs e)
        {
            // Calculate UNIX time for the data item timestamp
            DateTime dt = e.DataItem.CurrentSample.TimeStamp;
            DateTime sTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double unixTime = (double)(dt.Subtract(sTime)).TotalSeconds;
            string dtStr = dt.ToString("MM/dd/yyyy hh:mm:ss.fff tt");

            string tagName = e.DataItem.Name.ToString().ToLower();

            if (dataDictionary.ContainsKey(tagName)) 
            {
                dataDictionary[tagName].AppendLine(string.Format(dataFormat, dtStr, unixTime, e.DataItem.CurrentSample.Value, e.DataItem.CurrentSample.Sequence));
            }
        }

        void client_DataItemsChanged(object sender, EventArgs e)
        {
            IList<Task> tasks = new List<Task>();

            foreach (var dataKeyValue in dataDictionary) 
            {
                if (dataKeyValue.Value.Length > 0) 
                {
                    tasks.Add(StorageAgent.UploadBlockAsync(blobUrl[dataKeyValue.Key], headerFormat, dataKeyValue.Value.ToString()));
                    dataKeyValue.Value.Clear();
                }
            }

            Task.WaitAll(tasks.ToArray());
        }

        #endregion
    }
}
