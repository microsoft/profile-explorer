using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using IRExplorerCore;

namespace IRExplorer {
    public class SharedSessionInfo {
        public string BlobName { get;set; }
        public byte[] EncryptionKey { get; set; }
    }

    public class SessionSharing {
        private string containerName_;
        private string connectionString_;

        public const string DefaultConnectionString = @"DefaultEndpointsProtocol=https;AccountName=irexplorer;AccountKey=aZu0InDtGTMf4lAhkNJgtqTi3AP8cmfyojE/PNICHcKpT2VaLGgQlfZbMuKicE2dmaXnV7XioHPGtB770aWp/g==;EndpointSuffix=core.windows.net";

        public SessionSharing(string containerName, string connectionString) {
            containerName_ = containerName;
            connectionString_ = connectionString;
        }

        public async Task<SharedSessionInfo> UploadSession(string sessionFilePath) {
            var result = new SharedSessionInfo() {
                BlobName = Guid.NewGuid().ToString("N"),
                EncryptionKey = EncryptionUtils.CreateNewKey()
            };

            var data = await File.ReadAllBytesAsync(sessionFilePath);
            var encryptedData= await Task.Run(() => EncryptionUtils.Encrypt(data, result.EncryptionKey));

            var blobClient = CreateBlobClient(result.BlobName);
            await blobClient.UploadAsync(new MemoryStream(encryptedData));

            return result;
        }

        public async Task<string>> DownloadSession(SharedSessionInfo sessionInfo) {
            var downloadFilePath = Path.GetTempFileName();
            var blobClient = CreateBlobClient(sessionInfo.BlobName);
            var download = await blobClient.DownloadAsync();

            using (FileStream downloadFileStream = File.OpenWrite(downloadFilePath)) {
                await download.Content.CopyToAsync(downloadFileStream);
            }

            var data = await File.ReadAllBytes(downloadFilePath);
            var decryptedData = EncryptionUtils.Decrypt(data, sessionInfo.EncriptionKey);
            await File.WriteAllBytes(downloadFilePath, decryptedData);
            return downloadFilePath;;
        }

        private BlobClient CreateBlobClient(string fileName) {
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString_);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName_);
            return containerClient.GetBlobClient(fileName);
        }
    }
}
