using System;
using System.IO;
using System.Text;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace OrderProcessor;

public class OrderProcessor
{
    [FunctionName("OrderProcessor")]
    [return: ServiceBus("errors", Connection = "eshop32_SERVICEBUS")]
    public static string Run([ServiceBusTrigger("orders", Connection = "eshop32_SERVICEBUS")] string myQueueItem,
        ILogger log,
        [Blob("outcontainer/{rand-guid}.json", FileAccess.Write, Connection = "AzureWebJobsStorage")]
        Stream outputBlob)
    {
        var retries = 0;
        var rnd = new Random();

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(myQueueItem ?? ""));
        while (retries < 4)
        {
            try
            {
                if (rnd.Next(1, 3) == 2)
                {
                    break; //something happened here
                }

                log.LogInformation("Success");

                ms.CopyTo(outputBlob);
                return null;
            }
            catch (Exception ex)
            {
                log.LogCritical(ex, "Failed to upload to blob");

                log.LogInformation($"Failed to upload to blob, retries left: {3 - retries}");

                retries++;
            }
        }

        log.LogInformation("Failed to upload to blob, sending to email queue");

        return myQueueItem;
    }
}
