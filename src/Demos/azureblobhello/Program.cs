using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO.Compression;
using Newtonsoft.Json;
using Test;
using Metering.BaseTypes;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
string hubns="eventhubscrvgwm6v7hkku";
string hub="eventhubcrvgwm6v7hkku";
string latest=hubns+".servicebus.windows.net/"+hub+"/0/latest.json.gz";
string connectionString = "DefaultEndpointsProtocol=https;AccountName=storagecrvgwm6v7hkku;AccountKey=9UXJP48imsRGnoge5Bk43LMynIByLRsEqfZR6Xd1EB5jDxGYTJzaYCw8ZLn2sMB9qlZ6tHGE1Wp9eEeokqjeHQ==;EndpointSuffix=core.windows.net";//Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
        BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
        BlobContainerClient containerClient =  blobServiceClient.GetBlobContainerClient("snapshots");
        BlobClient blobClient = containerClient.GetBlobClient(latest);
        string downloadFilePath = @"C:\github\0\latest.json.gz";
        string DecompressedFileName = @"C:\github\0\latest.json";
        string DecompressedFileName1 = @"C:\github\0\latest1.json";
await blobClient.DownloadToAsync(downloadFilePath);

        using FileStream compressedFileStream = File.Open(downloadFilePath, FileMode.Open);
        using FileStream outputFileStream = File.Create(DecompressedFileName);
        using var decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress);
        decompressor.CopyTo(outputFileStream);
        outputFileStream.Close();

//sync version
string jsonString = File.ReadAllText(DecompressedFileName1);

var metercollections = Json.fromStr<MeterCollection>(jsonString);

Console.WriteLine(metercollections.MeterCollection);
