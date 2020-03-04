using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace GithubStream
{
    public static class GetEvents
    {
        private const int MaxPages = 10;
        private static HttpClient _http = new HttpClient();
        private static EntityTagHeaderValue[] _eTags = new EntityTagHeaderValue[MaxPages];
        private static ILogger _log;

        [FunctionName(nameof(GetEvents))]
        public static async Task Run(
            [TimerTrigger("0 */1 * * * *",RunOnStartup = true)]TimerInfo myTimer,
            [EventHub("githubstream", Connection = "EventHubConnectionString")]IAsyncCollector<EventData> outputEvents,
            ILogger log)
        {
            _log = log;
            _log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");


            int page = 1;
            var result = await GetPageOfEvents(page);
            while (result.HasEvents)
            {
                var events = result.Events;
                _log.LogInformation($"Received Page {page} containing {events.Length} events");

                foreach (var @event in events)
                {
                    // add an event to event hub
                    _log.LogInformation(@event.Type);

                    var eventData = new EventData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(@event)));
                    eventData.Properties.Add("GithubEventType", @event.Type);
                    await outputEvents.AddAsync(eventData);
                }

                if (events.Length < 30) break;
                if (result.RateLimitRemaining <= 0) break;

                page++;
                if (page > 10) break;

                result = await GetPageOfEvents(page);
            }
        }

        private static async Task<GetPageOfEventsResult> GetPageOfEvents(int page)
        {
            if (page > MaxPages) throw new ArgumentOutOfRangeException(nameof(page), $"page cannot be greater than {MaxPages}");

            int pageIndex = page - 1;
            string url = $"https://api.github.com/orgs/microsoft/events?page={page}";
            _log.LogInformation($"GET {url}");
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(new ProductHeaderValue("DanielLarsenNZ-GithubStream")));
            if (_eTags[pageIndex] != null)
            {
                request.Headers.IfNoneMatch.Add(_eTags[pageIndex]);
                _log.LogInformation($"If-None-Match {_eTags[pageIndex]}");
            }

            var response = await _http.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _log.LogInformation($"{url} Not Modified ETag = {_eTags[pageIndex]}");
                return new GetPageOfEventsResult();
            }

            response.EnsureSuccessStatusCode();

            _eTags[pageIndex] = response.Headers.ETag;

            // X-RateLimit-Remaining: 45
            int.TryParse(response.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault(), out int rateLimitRemaining);

            _log.LogInformation($"ETag {_eTags[pageIndex]}");
            _log.LogInformation($"X-RateLimit-Remaining {rateLimitRemaining}");

            return new GetPageOfEventsResult(
                JsonConvert.DeserializeObject<Events[]>(await response.Content.ReadAsStringAsync()))
            { RateLimitRemaining = rateLimitRemaining };
        }
    }

    internal class GetPageOfEventsResult
    {
        public GetPageOfEventsResult() : this(new Events[0]) { }

        public GetPageOfEventsResult(Events[] events)
        {
            Events = events;
        }

        public Events[] Events { get; set; }

        public bool HasEvents { get { return Events.Any(); } }

        public int RateLimitRemaining { get; set; }
    }
}
