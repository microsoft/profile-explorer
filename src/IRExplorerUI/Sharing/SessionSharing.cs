// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using IRExplorerCore;

namespace IRExplorerUI {
    public class SharedSessionInfo {
        public string ContainerName { get; set; }
        public string BlobName { get; set; }
        public int BlobSize { get; set; }
        public byte[] EncryptionKey { get; set; }
    }

    public class SessionSharing {
        private const string DefaultLocation = "https://irexplorer.blob.core.windows.net";

        private string connectionString_;

        public SessionSharing(string connectionString) {
            connectionString_ = connectionString;
        }

        public async Task<SharedSessionInfo> UploadSession(string sessionFilePath, string containerName) {
            var result = new SharedSessionInfo() {
                ContainerName = containerName,
                BlobName = Guid.NewGuid().ToString("N"),
                EncryptionKey = EncryptionUtils.CreateNewKey()
            };

            var data = await File.ReadAllBytesAsync(sessionFilePath);
            var encryptedData = await Task.Run(() => EncryptionUtils.Encrypt(data, result.EncryptionKey));
            result.BlobSize = encryptedData.Length;

            var blobClient = CreateBlobClient(result.BlobName, containerName);
            await blobClient.UploadAsync(new MemoryStream(encryptedData));
            return result;
        }

        public async Task<string> DownloadSession(SharedSessionInfo sessionInfo) {
            var downloadFilePath = Path.GetTempFileName();
            var blobClient = CreateBlobClient(sessionInfo.BlobName, sessionInfo.ContainerName);
            var download = await blobClient.DownloadAsync();

            using (FileStream downloadFileStream = File.OpenWrite(downloadFilePath)) {
                await download.Value.Content.CopyToAsync(downloadFileStream);
            }

            var data = await File.ReadAllBytesAsync(downloadFilePath);
            var decryptedData = EncryptionUtils.Decrypt(data, sessionInfo.EncryptionKey);
            await File.WriteAllBytesAsync(downloadFilePath, decryptedData);
            return downloadFilePath;
        }

        public string ToSharingLink(SharedSessionInfo sessionInfo) {
            // Base64 uses /, which is an issue for URLs.
            var keyString = Convert.ToBase64String(sessionInfo.EncryptionKey).Replace('/', '_');
            return $"{DefaultLocation}/{sessionInfo.ContainerName}/{sessionInfo.BlobName}?key={keyString}";
        }

        public SharedSessionInfo FromSharingLink(string link) {
            var parts = link.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4) {
                return null;
            }

            var containerName = parts[2];
            var keyIndex = parts[3].IndexOf("?key=");

            if (keyIndex == -1) {
                return null;
            }

            var key = parts[3].Substring(keyIndex + "?key=".Length).Replace('_', '/');
            var blobName = parts[3].Substring(0, keyIndex);

            return new SharedSessionInfo() {
                ContainerName = containerName,
                BlobName = blobName,
                EncryptionKey = Convert.FromBase64String(key)
            };
        }

        private BlobClient CreateBlobClient(string fileName, string containerName) {
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString_);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            return containerClient.GetBlobClient(fileName);
        }
    }
}
