// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using IRExplorerCore;

namespace IRExplorerUI;

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
    var result = new SharedSessionInfo {
      ContainerName = containerName,
      BlobName = Guid.NewGuid().ToString("N"),
      EncryptionKey = EncryptionUtils.CreateNewKey()
    };

    byte[] data = await File.ReadAllBytesAsync(sessionFilePath);
    byte[] encryptedData = await Task.Run(() => EncryptionUtils.Encrypt(data, result.EncryptionKey));
    result.BlobSize = encryptedData.Length;

    var blobClient = CreateBlobClient(result.BlobName, containerName);
    await blobClient.UploadAsync(new MemoryStream(encryptedData));
    return result;
  }

  public async Task<string> DownloadSession(SharedSessionInfo sessionInfo) {
    string downloadFilePath = Path.GetTempFileName();
    var blobClient = CreateBlobClient(sessionInfo.BlobName, sessionInfo.ContainerName);
    var download = await blobClient.DownloadAsync();

    using (var downloadFileStream = File.OpenWrite(downloadFilePath)) {
      await download.Value.Content.CopyToAsync(downloadFileStream);
    }

    byte[] data = await File.ReadAllBytesAsync(downloadFilePath);
    byte[] decryptedData = EncryptionUtils.Decrypt(data, sessionInfo.EncryptionKey);
    await File.WriteAllBytesAsync(downloadFilePath, decryptedData);
    return downloadFilePath;
  }

  public string ToSharingLink(SharedSessionInfo sessionInfo) {
    // Base64 uses /, which is an issue for URLs.
    string keyString = Convert.ToBase64String(sessionInfo.EncryptionKey).Replace('/', '_');
    return $"{DefaultLocation}/{sessionInfo.ContainerName}/{sessionInfo.BlobName}?key={keyString}";
  }

  public SharedSessionInfo FromSharingLink(string link) {
    string[] parts = link.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length != 4) {
      return null;
    }

    string containerName = parts[2];
    int keyIndex = parts[3].IndexOf("?key=");

    if (keyIndex == -1) {
      return null;
    }

    string key = parts[3].Substring(keyIndex + "?key=".Length).Replace('_', '/');
    string blobName = parts[3].Substring(0, keyIndex);

    return new SharedSessionInfo {
      ContainerName = containerName,
      BlobName = blobName,
      EncryptionKey = Convert.FromBase64String(key)
    };
  }

  private BlobClient CreateBlobClient(string fileName, string containerName) {
    var blobServiceClient = new BlobServiceClient(connectionString_);
    var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    return containerClient.GetBlobClient(fileName);
  }
}
