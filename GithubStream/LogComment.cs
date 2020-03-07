using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;

namespace GithubStream
{
    public static class LogComment
    {
        [FunctionName("LogComment")]
        public static void Run(
            [ServiceBusTrigger("comments", "CommentLogger", Connection = "ServiceBusConnectionString")]Message message,
            ILogger log)
        {
            var payload = JsonHelper.DeserializePayload(message);
            log.LogInformation($"Comment {payload.Comment.CreatedAt}: @{payload.Comment.User.Login}: {payload.Comment.Body.Replace("\r", " ").Replace("\n", " ").Substring(0, Math.Min(100, payload.Comment.Body.Length))}");

            //TODO: Save to Blob storage
        }
    }
}
