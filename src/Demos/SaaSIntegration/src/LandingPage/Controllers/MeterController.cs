using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Linq;
using Azure.Storage;
using System.Threading;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO.Compression;
using Newtonsoft.Json;
using Metering.BaseTypes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using LandingPage.ViewModels.Meter;
namespace MeteredPage.Controllers
{
    public class MeterController : Controller
    {
        private IConfiguration _configuration;


        public MeterController(

            IConfiguration Configuration)
        {

            _configuration = Configuration;
        }
        // GET: MeterController
        public async Task<ActionResult> IndexAsync()
        {
            var token = HttpContext.Session.GetString("currentToken");
            if (string.IsNullOrEmpty(token))
            {
                this.ModelState.AddModelError(string.Empty, "Token URL parameter cannot be empty");
                this.ViewBag.Message = "Token URL parameter cannot be empty";
                return this.View();
            }

            string hubns = _configuration["AZURE_METERING_INFRA_EVENTHUB_NAMESPACENAME"];
            string hub = _configuration["AZURE_METERING_INFRA_EVENTHUB_INSTANCENAME"];
            string latest = hubns + ".servicebus.windows.net/" + hub + "/0/latest.json.gz";
            string connectionString = _configuration["STORAGE_ACCOUNT_CONNECTION_STRINGS"];
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            var snapshotsBlobName = (_configuration["AZURE_METERING_INFRA_SNAPSHOTS_CONTAINER"]).Split("/");
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(snapshotsBlobName[snapshotsBlobName.Length-1]);
            BlobClient blobClient = containerClient.GetBlobClient(latest);
            string downloadFilePath = _configuration["LATEST_COMPRESSED_LOCATION"];
            string DecompressedFileName = _configuration["LATEST_UNCOMPRESSED_LOCATION"];

            await blobClient.DownloadToAsync(downloadFilePath);

            using FileStream compressedFileStream = System.IO.File.Open(downloadFilePath, FileMode.Open);
            using FileStream outputFileStream = System.IO.File.Create(DecompressedFileName);
            using var decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress);
            decompressor.CopyTo(outputFileStream);
            outputFileStream.Close();
            string jsonString = System.IO.File.ReadAllText(DecompressedFileName);

            //var metercollections = Metering.BaseTypes.Json.fromStr<MeterCollection>(jsonString);
            IndexViewModel model= new IndexViewModel();
            model.Json=jsonString;
            return View(model);
        }

        // GET: MeterController/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

 


  

    }
}
