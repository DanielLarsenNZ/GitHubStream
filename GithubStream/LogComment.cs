using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;

namespace GithubStream
{
    public static class LogComment
    {
        [FunctionName("LogComment")]
        public static void Run(
            [ServiceBusTrigger("comments", "CommentLogger", Connection = "ServiceBusConnectionString")]Message message,
            ILogger log)
        {
            var deserialized = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(message.Body)) as JObject;
            if (deserialized == null) throw new InvalidOperationException($"Failed to deserialize response as JSON.");
            var payload = deserialized["payload"].ToObject<Payload>();

            log.LogInformation($"Comment {payload.Comment.CreatedAt}: @{payload.Comment.User.Login}: {payload.Comment.Body.Replace("\r", " ").Replace("\n", " ").Substring(0, Math.Min(100, payload.Comment.Body.Length))}");
        }
    }
}
