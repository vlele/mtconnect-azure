using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;

namespace MTConnectSharp
{
    //[ComVisible(true)]
    //[ClassInterface(ClassInterfaceType.None)]
    //[ComSourceInterfaces(typeof(IClientEvents))]
    public class MTConnectClient : IMTConnectClient
    {
        #region Consturctors

        public MTConnectClient()
        {
            reset = false;
        }

        public MTConnectClient(String baseUrl, string[] targetTags, int interval, CancellationToken cancellationToken)
            : this()
        {
            this.AgentUrl = baseUrl;
            this.restClient = new RestClient(baseUrl);
            this.targetTags = targetTags;
            this.Interval = interval;
            this.cancellationToken = cancellationToken;
        }

        #endregion

        #region Private Fields

        private List<Device> devices;
        private bool reset;
        string[] targetTags;
        private Dictionary<String, DataItem> dataItemsRef = new Dictionary<string, DataItem>();
        private RestClient restClient;
        private Int64 nextSequence;
        private Int64 instanceId;
        private CancellationToken cancellationToken;

        #endregion

        #region Public Properties
        
		public string AgentUrl { get; set; }
		public Int32 Interval { get; set; }
		public Device[] Devices
		{
			get
			{
				return devices.ToArray<Device>();
			}
		}

        #endregion

        #region Events

        public event EventHandler ProbeCompleted;
        public event EventHandler DataItemsChanged;
        public event EventHandler<DataItemChangedEventArgs> DataItemChanged;

        #endregion

        #region Private Methods

        private void parseProbeResponse(IRestResponse response)
        {
            devices = new List<Device>();
            XDocument xDoc = XDocument.Load(new StringReader(response.Content));

            foreach (var d in xDoc.Descendants().First(d => d.Name.LocalName == "Devices").Elements())
            {
                devices.Add(new Device(d));
            }

            this.FillDataItemRefList();
            this.ProbeCompletedHandler();
        }
        
        private void FillDataItemRefList()
        {
            foreach (Device device in devices)
            {
                List<DataItem> dataItems = new List<DataItem>();
                dataItems.AddRange(device.DataItems);
                dataItems.AddRange(GetDataItems(device.Components));
                foreach (var dataItem in dataItems)
                {
                    dataItemsRef.Add(dataItem.id, dataItem);
                }
            }
        }

        private List<DataItem> GetDataItems(Component[] Components)
        {
            var dataItems = new List<DataItem>();

            foreach (var component in Components)
            {
                dataItems.AddRange(component.DataItems);
                if (component.Components.Length > 0)
                {
                    dataItems.AddRange(GetDataItems(component.Components));
                }
            }
            return dataItems;
        }

        private void ProbeCompletedHandler()
        {
            var args = new EventArgs();
            if (ProbeCompleted != null)
            {
                ProbeCompleted(this, args);
            }
        }

        private void GetCurrent()
        {
            var request = new RestRequest("current", Method.GET);
            var response = restClient.Execute(request);
            parseSampleResponse(response);
        }

        private void GetSample()
        {
            var request = new RestRequest("sample", Method.GET);
            request.AddParameter("from", nextSequence);
            request.AddParameter("count", 1500);
            restClient.ExecuteAsync(request, (r) => parseSampleResponse(r));
        }

        private void parseSampleResponse(IRestResponse response)
        {
            String xmlContent = response.Content;
            if (String.IsNullOrEmpty(xmlContent)) return;

            using (StringReader sr = new StringReader(xmlContent))
            {
                XDocument xDoc = null;
                xDoc = XDocument.Load(sr);
                bool isNextSeqPresent = true;
                
                try 
                {
                    nextSequence = Convert.ToInt64(xDoc.Descendants().First(e => e.Name.LocalName == "Header").Attribute("nextSequence").Value);
                }
                catch 
                {
                    // Error returned
                    isNextSeqPresent = false;
                }

                var curInstanceId = Convert.ToInt64(xDoc.Descendants().First(e => e.Name.LocalName == "Header").Attribute("instanceId").Value);

                if ((isNextSeqPresent = false) || (instanceId != 0 && curInstanceId != instanceId))
                {
                    reset = true;
                    instanceId = 0;
                    return;
                }
                else if (instanceId == 0)
                {
                    instanceId = curInstanceId;
                } 

                if (xDoc.Descendants().Any(e => e.Attributes().Any(a => a.Name.LocalName == "dataItemId")))
                {
                    IEnumerable<XElement> xmlDataItems = xDoc.Descendants()
                        .Where(e => e.Attributes().Any(a => a.Name.LocalName == "dataItemId"));

                    var dataItems = (from e in xmlDataItems
                                     select new
                                     {
                                         id = e.Attribute("dataItemId").Value,
                                         timestamp = e.Attribute("timestamp").Value,
                                         //timestamp = DateTime.Parse(e.Attribute("timestamp").Value, null, System.Globalization.DateTimeStyles.RoundtripKind),
                                         value = e.Value,
                                         sequence = e.Attribute("sequence").Value
                                     }).ToList();

                    foreach (var item in dataItems.OrderBy(i => i.timestamp))
                    {
                        var dataItem = dataItemsRef[item.id];
                        var ts = item.timestamp.Replace('T', ' ').Remove(item.timestamp.Length - 1);
                        dataItem.AddSample(new DataItemSample(item.value.ToString(), DateTime.Parse(ts), item.sequence));

                        DataItemChangedHandler(dataItemsRef[item.id]);
                    }
                    DataItemsChangedHandler();
                }
            }
        }

        private void DataItemChangedHandler(DataItem dataItem)
        {
            if (targetTags.Contains(dataItem.Name.ToLower()))
            {
                var args = new DataItemChangedEventArgs(dataItem);
                DataItemChanged(this, args);
            }
        }

        private void DataItemsChangedHandler()
        {
            var args = new EventArgs();
            if (DataItemsChanged != null)
            {
                DataItemsChanged(this, args);
            }
        }

        #endregion

        #region Public Methods

        public static IList<string> GetDeviceList(string baseURL)
        {
            IList<string> devices = new List<string>();

            var request = new RestRequest("probe", Method.GET);
            var response = new RestClient(baseURL).Execute(request);

            XDocument xDoc = XDocument.Load(new StringReader(response.Content));

            foreach (var deviceElement in xDoc.Descendants().First(d => d.Name.LocalName == "Devices").Elements())
            {
                devices.Add(deviceElement.Attribute("name").Value);
            }

            return devices;
        }

        public void Probe()
        {
            var request = new RestRequest("probe", Method.GET);
            var response = restClient.Execute(request);
            parseProbeResponse(response);
        }

		public void StartStreaming()
		{
            GetCurrent();

            while (true) 
            {
                if (!this.cancellationToken.IsCancellationRequested)
                {
                    if (reset)
                    {
                        GetCurrent();
                        reset = false;
                    }

                    GetSample();
                    Thread.Sleep(this.Interval);
                }
                else 
                {
                    break;
                }
            }
        }

        #endregion
    }
}
