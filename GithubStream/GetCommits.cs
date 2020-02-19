using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace GithubStream
{
    public static class GetCommits
    {
        private static HttpClient _http = new HttpClient();
        private static EntityTagHeaderValue _etag = null;

        [FunctionName("GetCommits")]
        public static async Task Run([TimerTrigger("0 */1 * * * *",RunOnStartup = true)]TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/orgs/microsoft/events");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(new ProductHeaderValue("DanielLarsenNZ-GithubStream")));
            
            if (_etag != null) request.Headers.IfNoneMatch.Add(_etag);

            var response = await _http.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                log.LogInformation($"Not Modified ETag = {_etag}");
                return;
            }

            response.EnsureSuccessStatusCode();

            var commits = JsonConvert.DeserializeObject<Commits[]>(await response.Content.ReadAsStringAsync());
            log.LogInformation($"Received {commits.Length} commits");

            foreach (var commit in commits)
            {
                // add an event to event hub
                log.LogInformation(commit?.Commit?.Message);

            }

            // only save ETag on success
            _etag = response.Headers.ETag;
        }
    }
}
