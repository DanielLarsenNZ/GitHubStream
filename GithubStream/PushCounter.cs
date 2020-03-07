using Microsoft.Azure.Cosmos;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace GithubStream
{
    public class PushCounter
    {
        private readonly Container _container;
        private const string IncrementPushCountSProc = "IncrementPushCount";

        public PushCounter(IConfiguration config, CosmosClient cosmos)
        {
            _container = cosmos.GetContainer(config["CosmosDbName"], config["CosmosCollection"]);
        }

        [FunctionName("PushCounter")]
        public async Task Run([ServiceBusTrigger("pushes", "PushCounter", Connection = "ServiceBusConnectionString")]Message message,
            ILogger log)
        {
            var payload = JsonHelper.DeserializePayload(message);

            log.LogInformation($"Push {payload.Head}");

            var createdAt = DateTime.Parse((string)message.UserProperties["created_at"]);
            string timestamp = createdAt.ToUniversalTime().ToString("yyyyMMddHHmm");
            string pk = timestamp.Substring(0, 10);

            try
            {
                // Execute a stored procedure that increments the PushCount inside an implicit transaction
                var response = await _container.Scripts.ExecuteStoredProcedureAsync<dynamic>(
                    IncrementPushCountSProc,
                    new PartitionKey(pk),
                    new dynamic[] { timestamp });
                log.LogInformation($"Cosmos: id = {response.Resource.id}, pushCount = {response.Resource.pushCount}");
            }
            catch (CosmosException ex) when ((int)ex.StatusCode == 449)
            {
                // https://docs.microsoft.com/en-us/rest/api/cosmos-db/http-status-codes-for-cosmosdb
                // The operation encountered a transient error. This code only occurs on write operations. 
                // It is safe to retry the operation.
                // The PushCount has not been incremented, throw here so that this message is retried.
                log.LogWarning($"HTTP STATUS 449 RETRY WITH: {ex.Message}");
                log.LogError(ex, ex.Message);
                throw;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
            {
                // https://docs.microsoft.com/en-us/rest/api/cosmos-db/http-status-codes-for-cosmosdb
                // The operation did not complete within the allotted amount of time. This code 
                // is returned when a stored procedure, trigger, or UDF (within a query) does not 
                // complete execution within the maximum execution time.
                // The PushCount has not been incremented, throw here so that this message is retried.
                log.LogWarning($"HTTP STATUS 408 REQUEST TIMEOUT: {ex.Message}");
                log.LogError(ex, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                log.LogError(ex, ex.Message);
                throw;
            }
        }
    }
}
