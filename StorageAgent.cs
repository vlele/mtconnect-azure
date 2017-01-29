using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MTConnectWebJob
{
    internal class StorageAgent
    {
        internal static IDictionary<string, string> GenerateBlobs(string conString, string deviceName, string[] tags)
        {
            Dictionary<string, string> blobUri = new Dictionary<string, string>();
            var storageAccount = CloudStorageAccount.Parse(conString);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var blobContainer = blobClient.GetContainerReference(String.Format("{0}-{1}{2}{3}-{4}{5}",
                deviceName.ToLower(), 
                DateTime.Now.Year, DateTime.Now.Month.ToString("00"), DateTime.Now.Day.ToString("00"),
                DateTime.Now.Hour.ToString("00"), DateTime.Now.Minute.ToString("00")));
            blobContainer.CreateIfNotExists();

            for (int i = 0; i < tags.Length; i++)
            {
                CloudBlockBlob blob = blobContainer.GetBlockBlobReference(tags[i]);

                SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
                sasConstraints.SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-5);
                sasConstraints.SharedAccessExpiryTime = DateTime.UtcNow.AddHours(512);
                sasConstraints.Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write;
                string sasBlobToken = blob.GetSharedAccessSignature(sasConstraints);

                blobUri.Add(tags[i], blob.Uri + sasBlobToken);
            }

            return blobUri;
        }

        internal static async Task UploadBlockAsync(string blobUri, string headerFormat, string data)
        {
            var blob = new CloudBlockBlob(new Uri(blobUri));

            string id = String.Empty;
            int blockCount = 0;

            byte[] byteArray = Encoding.UTF8.GetBytes(data);
            MemoryStream stream = new MemoryStream(byteArray);

            List<string> blockIdList = new List<string>();
            if (blob.Exists())
            {
                var blockList = blob.DownloadBlockList();
                blockCount = blockList.Count();

                foreach (ListBlockItem item in blockList)
                {
                    blockIdList.Add(item.Name);
                }
            }
            else
            {
                var headerId = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(string.Format("BlockId{0}", (++blockCount).ToString("0000000"))));
                await blob.PutBlockAsync(headerId, new MemoryStream(Encoding.UTF8.GetBytes(headerFormat + "\r\n")), null);
                blockIdList.Add(headerId);
            }

            id = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(string.Format("BlockId{0}", (++blockCount).ToString("0000000"))));
            await blob.PutBlockAsync(id, stream, null);
            blockIdList.Add(id);
            await blob.PutBlockListAsync(blockIdList);
        }
    }
}
