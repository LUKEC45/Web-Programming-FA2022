﻿using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using CloudStorage.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage;
using Azure.Storage.Blobs.Models;
using Azure.Data.Tables;
using Azure;

namespace CloudStorage.Services
{
    public class ImageTableStorage : IImageTableStorage
    {
        private readonly IUserNameProvider userNameProvider;
        private readonly IConnectionStringProvider connectionStringProvider;
        private readonly TableClient imageTable;
        private readonly BlobServiceClient blobServiceClient;
        private readonly BlobContainerClient blobContainerClient;

        public ImageTableStorage(IUserNameProvider userNameProvider,
            IBlobServiceClientProvider blobServiceClientProvider,
            IConnectionStringProvider connectionStringProvider)
        {
            this.userNameProvider = userNameProvider;
            this.connectionStringProvider = connectionStringProvider;
            imageTable = new(connectionStringProvider.ConnectionString, userNameProvider.UserName);

            blobServiceClient = blobServiceClientProvider.BlobServiceClient;
            blobContainerClient = blobServiceClient.GetBlobContainerClient(userNameProvider.UserName);
        }

        public async Task StartupAsync()
        {
            await imageTable.CreateIfNotExistsAsync();
            await blobContainerClient.CreateIfNotExistsAsync();
        }
        
        public async Task<ImageTableEntity> GetAsync(string id)
        {
            return await imageTable.GetEntityAsync<ImageTableEntity>(this.userNameProvider.UserName, id);
        }

        public async Task<ImageTableEntity> AddOrUpdateAsync(ImageTableEntity image)
        {
            if (string.IsNullOrWhiteSpace(image.Id))
            {
                image.Id = Guid.NewGuid().ToString();
                image.UserName = this.userNameProvider.UserName;
            }
            await imageTable.UpsertEntityAsync(image);
            return image;
        }

        public async Task DeleteAsync(string id)
        {
            await imageTable.DeleteEntityAsync(this.userNameProvider.UserName, id);
        }

        public string GetBlobUrl()
        {
            return blobServiceClient.Uri.ToString();
        }

        public string GetUploadUrl(string fileName)
        {
            // Create a SAS token that's valid for one hour.
            BlobSasBuilder sasBuilderBlob = new()
            {
                BlobContainerName = blobContainerClient.Name,
                BlobName = fileName,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(15)),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
            };
            sasBuilderBlob.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Add | BlobSasPermissions.Create);

            // Use the key to get the SAS token.
            var sasToken = sasBuilderBlob.ToSasQueryParameters(new StorageSharedKeyCredential(blobServiceClient.AccountName, connectionStringProvider.AccountKey)).ToString();

            return blobContainerClient.GetBlockBlobClient(fileName).Uri + "?" + sasToken;
        }

        public string GetDownloadUrl(ImageTableEntity image)
        {
            // Create a SAS token for an image
            BlobSasBuilder sasBuilderBlob = new()
            {
                BlobContainerName = blobContainerClient.Name,
                BlobName = image.Id,
                Resource = "b",
                StartsOn = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(15)),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(8)
            };
            sasBuilderBlob.SetPermissions(BlobSasPermissions.Read);

            // Use the key to get the SAS token.
            var sasToken = sasBuilderBlob.ToSasQueryParameters(new StorageSharedKeyCredential(blobServiceClient.AccountName, connectionStringProvider.AccountKey)).ToString();

            return blobContainerClient.GetBlockBlobClient(image.Id).Uri + "?" + sasToken;
        }

        public async IAsyncEnumerable<ImageTableEntity> GetAllImagesAsync()
        {
            AsyncPageable<ImageTableEntity> queryResults =
                imageTable.QueryAsync<ImageTableEntity>(filter: $"PartitionKey eq '{this.userNameProvider.UserName}'");

            await foreach (var imageResult in queryResults)
            {
                if (imageResult.UploadComplete)
                {
                    yield return imageResult;
                }
            }
        }

        public async Task PurgeAsync()
        {
            AsyncPageable<ImageTableEntity> queryResults =
                imageTable.QueryAsync<ImageTableEntity>(filter: $"PartitionKey eq '{this.userNameProvider.UserName}'");

            await foreach (var imageResult in queryResults)
            {
                _ = imageTable.DeleteEntityAsync(imageResult.PartitionKey, imageResult.RowKey);
            }

            string continuationToken = null;

            do
            {
                var resultSegment = blobContainerClient.GetBlobs().AsPages(continuationToken);

                foreach (Azure.Page<BlobItem> blobPage in resultSegment)
                {
                    foreach (BlobItem blobItem in blobPage.Values)
                    {
                        await blobContainerClient.DeleteBlobIfExistsAsync(blobItem.Name);
                    }

                    continuationToken = blobPage.ContinuationToken;
                }

            } while (continuationToken != "");
        }
    }
}
