using System;
using System.Text;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GithubStream
{
    public static class LogComment
    {
        [FunctionName("LogComment")]
        public static void Run(
            [ServiceBusTrigger("comments", "LogComment", Connection = "ServiceBusConnectionString")]Message message, 
            ILogger log)
        {
            //log.LogInformation($"C# ServiceBus topic trigger function processed message: {mySbMsg}");
            //log.LogInformation(message.UserProperties["GithubEventType"]?.ToString());

            var deserialized = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(message.Body)) as JObject;
            if (deserialized == null) throw new InvalidOperationException($"Failed to deserialize response as JSON.");
            var payload = deserialized["payload"].ToObject<Payload>();

            log.LogInformation($"Comment {payload.Comment.CreatedAt}: @{payload.Comment.User.Login}: {payload.Comment.Body.Replace("\r", " ").Replace("\n", " ").Substring(0, Math.Min(100, payload.Comment.Body.Length))}");
        }
    }
}
