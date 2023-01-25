using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Configuration;

namespace DeliveryOrderProcessor
{
    public static class DeliveryOrderProcessor
    {
        [FunctionName("DeliveryOrderProcessor")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log, [CosmosDB(databaseName: "delivery", containerName: "delivery",
    Connection = "CosmosDbConnectionString"
    )]IAsyncCollector<dynamic> documentsOut)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");


            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var deliveryInformation = JsonConvert.DeserializeObject(requestBody);
            await documentsOut.AddAsync(deliveryInformation);
            return new OkObjectResult(deliveryInformation);
        }
    }
}
