using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace GithubStream
{
    public class GetEvents
    {
        private const int MaxPages = 10;
        private const int MaxRateLimit = 5000;
        private static readonly HttpClient _http = new HttpClient();
        private static readonly EntityTagHeaderValue[] _eTags = new EntityTagHeaderValue[MaxPages];
        private static int _rateLimitRemaining;
        private static DateTimeOffset _rateLimitResetDateTime = DateTime.UtcNow;
        private static ILogger _log;
        private static IOptions<GitHubOptions> _settings;
        private static IConfiguration _config;

        public GetEvents(IOptions<GitHubOptions> settings, IConfiguration config)
        {
            _settings = settings;
            _config = config;
        }

        [FunctionName(nameof(GetEvents))]
        public async Task Run(
            [TimerTrigger("0 */1 * * * *")]TimerInfo timer,
            [EventHub("githubstream", Connection = "EventHubConnectionString")]IAsyncCollector<EventData> outputEvents,
            ILogger log)
        {
            _log = log;

            _log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            if (IsRateLimitExhausted())
            {
                _log.LogInformation($"Rate limit exhausted until {_rateLimitResetDateTime}");
                return;
            }

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

                page++;
                if (page > 10) break;

                if (IsRateLimitExhausted())
                {
                    _log.LogInformation($"Rate limit remaining: {_rateLimitRemaining} resets at {_rateLimitResetDateTime}");
                    break;
                }

                result = await GetPageOfEvents(page);
            }
        }

        private static async Task<GetPageOfEventsResult> GetPageOfEvents(int page)
        {
            if (page > MaxPages) throw new ArgumentOutOfRangeException(nameof(page), $"page cannot be greater than {MaxPages}");

            int pageIndex = page - 1;

            string url = $"https://api.github.com/orgs/microsoft/events?page={page}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            // User-Agent
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(new ProductHeaderValue("DanielLarsenNZ-GithubStream")));

            // Authorization
            if (!string.IsNullOrEmpty(_config["GitHubAppClientId"]))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Basic", Convert.ToBase64String(
                            Encoding.ASCII.GetBytes(
                               $"{_config["GitHubAppClientId"]}:{_config["GitHubAppClientSecret"]}")));

                _log.LogInformation($"Authorization: Basic {_config["GitHubAppClientId"]}...");
            }

            // ETag
            if (_eTags[pageIndex] != null)
            {
                request.Headers.IfNoneMatch.Add(_eTags[pageIndex]);
                _log.LogInformation($"If-None-Match {_eTags[pageIndex]}");
            }

            var response = await _http.SendAsync(request);
            
            // HTTP STATUS 304
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _log.LogInformation($"{url} Not Modified ETag = {_eTags[pageIndex]}");
                return new GetPageOfEventsResult();
            }

            // HTTP STATUS 403
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _log.LogError(await response.Content.ReadAsStringAsync());
                SaveRateLimits(response.Headers);
            }

            response.EnsureSuccessStatusCode();

            _eTags[pageIndex] = response.Headers.ETag;

            SaveRateLimits(response.Headers);

            _log.LogInformation($"ETag {_eTags[pageIndex]}");


            return new GetPageOfEventsResult(
                JsonConvert.DeserializeObject<Events[]>(await response.Content.ReadAsStringAsync()));
        }

        private static bool IsRateLimitExhausted() => _rateLimitRemaining < 1 && _rateLimitResetDateTime > DateTime.UtcNow;

        private static void SaveRateLimits(HttpResponseHeaders headers)
        {
            // X-RateLimit-Remaining: 45
            if (int.TryParse(headers.GetValues("X-RateLimit-Remaining").FirstOrDefault(), out int rateLimitRemaining))
            {
                _rateLimitRemaining = rateLimitRemaining;
                _log.LogInformation($"Rate Limit Remaining: {_rateLimitRemaining}");
            } 
            else
            {
                throw new InvalidOperationException($"Could not parse X-RateLimit-Remaining: {headers.GetValues("X-RateLimit-Remaining").FirstOrDefault()}");
            }

            // X-RateLimit-Reset: 1372700873
            if (int.TryParse(headers.GetValues("X-RateLimit-Reset").FirstOrDefault(), out int rateLimitReset))
            {
                _rateLimitResetDateTime = DateTimeOffset.FromUnixTimeSeconds(rateLimitReset);
                _log.LogInformation($"Rate Limit Reset: {_rateLimitResetDateTime}");
            }
            else
            {
                throw new InvalidOperationException($"Could not parse X-RateLimit-Reset: {headers.GetValues("X-RateLimit-Reset").FirstOrDefault()}");
            }
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
    }
}
