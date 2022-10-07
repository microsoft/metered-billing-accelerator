namespace MeteredPage.Controllers;

using MeteredPage.ViewModels;
using MeteredPage.ViewModels.Home;
using Metering.ClientSDK;
using Metering.Integration;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

[Authorize(AuthenticationSchemes = OpenIdConnectDefaults.AuthenticationScheme)]
[AuthorizeForScopes(Scopes = new string[] { "user.read" })]
public class HomeController : Controller
{
    private readonly GraphServiceClient _graphServiceClient;
    private IConfiguration _configuration;

    private string subscriptionKey = "";
    private string ocrEndPoint = "";

    public HomeController(
        GraphServiceClient graphServiceClient,
        IConfiguration Configuration)
    {
        _graphServiceClient = graphServiceClient;
        _configuration = Configuration;
    }

    /// <summary>
    /// Shows all information associated with the user, the request, and the subscription.
    /// </summary>
    /// <param name="token">THe marketplace purchase ID token</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns></returns>
    public async Task<IActionResult> IndexAsync(string token, CancellationToken cancellationToken)
    {
        string saasSubscription = _configuration["SaasSubscription"]; 
        if (string.IsNullOrEmpty(saasSubscription))
        {
            this.ModelState.AddModelError(string.Empty, "Subscription Information is missing");
            this.ViewBag.Message = "Subscription Information is missing";
            return this.View();
        }

        string saasDimension = _configuration["SaaSDimension"];
        if (string.IsNullOrEmpty(saasDimension))
        {
            this.ModelState.AddModelError(string.Empty, "Dimension Information is missing");
            this.ViewBag.Message = "Dimension Information is missing";
            return this.View();
        }
        // get graph current user data
        var graphApiUser = await _graphServiceClient.Me.Request().GetAsync();

        return View(new IndexViewModel());
    }


    /// <summary>
    /// OCR Processing this instance.
    /// </summary>
    /// <returns> OCR instance.</returns>
    [HttpPost]
    public async Task<IActionResult> IndexAsync(List<IFormFile> files)
    {
        IndexViewModel view = new();

        foreach (var formFile in files)
        {
            if (formFile.Length > 0)
            {
                // full path to file in temp location
                using MemoryStream ms = new();
                formFile.CopyTo(ms);
                var fileBytes = ms.ToArray();
                view.OcrDetail = await MakeOCRRequest(fileBytes);
                string imgBase64Data = Convert.ToBase64String(fileBytes);
                string imgDataURL = string.Format("data:image/png;base64,{0}", imgBase64Data);
                view.imgDataURL = imgDataURL;
            }
        }

        return this.View(view);
    }

    /// <summary>
    /// Gets the text visible in the specified image file by using
    /// the Computer Vision REST API.
    /// </summary>
    /// <param name="imageFilePath">The image file with printed text.</param>
    public async Task<string> MakeOCRRequest(byte[] byteData)
    {
        this.subscriptionKey = _configuration["subscriptionKey"];
        this.ocrEndPoint = _configuration["ocrEndPoint"];
        if (!String.IsNullOrEmpty(this.subscriptionKey)
            && !String.IsNullOrEmpty(this.ocrEndPoint))
        {
            try
            {
                HttpClient client = new HttpClient();
                string uriBase = this.ocrEndPoint + "vision/v2.1/ocr";
                // Request headers.
                client.DefaultRequestHeaders.Add(
                    "Ocp-Apim-Subscription-Key", this.subscriptionKey);

                string requestParameters = "language=unk&detectOrientation=true";

                // Assemble the URI for the REST API method.
                string uri = uriBase + "?" + requestParameters;

                HttpResponseMessage response;

                // Add the byte array as an octet stream to the request body.
                using (ByteArrayContent content = new(byteData))
                {
                    // This example uses the "application/octet-stream" content type.
                    // The other content types you can use are "application/json"
                    // and "multipart/form-data".
                    content.Headers.ContentType =
                        new MediaTypeHeaderValue("application/octet-stream");

                    // Asynchronously call the REST API method.
                    response = await client.PostAsync(uri, content);
                }

                // Asynchronously get the JSON response.
                string contentString = await response.Content.ReadAsStringAsync();

                // Display the JSON response.
                string result=JToken.Parse(contentString).ToString();

                AzureOcrModel ocrResult = JsonConvert.DeserializeObject<AzureOcrModel>(result);

                List<string> ocrText = ocrResult.regions.SelectMany(r => r.lines.SelectMany(l => l.words).Select(w => w.text)).ToList();
                //CallMeteredAudit(ocrText);

                string finalText = ocrText.Aggregate("", (current, s) => current + (s + ","));
                
                // Take JSON and Return the words only. 
                await SendMeteredQty(ocrText.Count);

                return finalText;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
        else
        {
            return "Missing OCR Configuration Information";
        }
    }

    public async Task SendMeteredQty(double qtny)
    {
        // Subscription ID
        var resourceId = _configuration["SaasSubscription"];
        string dimensions = _configuration["SaaSDimension"];

        // Get Dim
        var eventHubProducerClient = MeteringConnections.createEventHubProducerClientForClientSDK();
        var meters = dimensions.Split(",");

        foreach (var dim in meters)
        {
            await eventHubProducerClient.SubmitMeterAsync(
                resourceId: resourceId,
                applicationInternalMeterName: dim,
                quantity: qtny);
        }
    }
}