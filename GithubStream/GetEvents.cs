using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace GithubStream
{
    public class GetEvents
    {
        private const int MaxPages = 10;
        private readonly HttpClient _http;
        private readonly EntityTagHeaderValue[] _eTags = new EntityTagHeaderValue[MaxPages];

        // GetEvents is a Singleton due to the Timer trigger

        // The number of calls to GitHub API remaining until _rateLimitResetDateTime
        private int _rateLimitRemaining;
        // The DateTime that the GitHub API rate limit resets
        private DateTimeOffset _rateLimitResetDateTime = DateTime.UtcNow;

        private ILogger _log;

        public GetEvents(IConfiguration config, HttpClient httpClient)
        {
            _http = httpClient;

            // User-Agent header
            _http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(new ProductHeaderValue("DanielLarsenNZ-GithubStream")));

            // Authorization header
            if (!string.IsNullOrEmpty(config["GitHubAppClientId"]))
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(
                        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(
                               $"{config["GitHubAppClientId"]}:{config["GitHubAppClientSecret"]}")));

                _log.LogInformation($"Authorization: Basic {config["GitHubAppClientId"]}...");
            }
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
                _log.LogInformation($"Received Page {page} containing {result.Events.Length} events");

                foreach (var @event in result.Events)
                {
                    _log.LogInformation(@event.Type);

                    // send event to event hub
                    var eventData = new EventData(Encoding.UTF8.GetBytes(JsonHelper.SerializeObject(@event)));
                    eventData.Properties.Add("GithubEventType", @event.Type);
                    await outputEvents.AddAsync(eventData);
                }

                if (result.IsLastPageOfEvents) break;

                page++;
                if (page > MaxPages) break;

                if (IsRateLimitExhausted())
                {
                    _log.LogInformation($"Rate limit remaining: {_rateLimitRemaining} resets at {_rateLimitResetDateTime}");
                    break;
                }

                result = await GetPageOfEvents(page);
            }
        }

        private async Task<GetPageOfEventsResult> GetPageOfEvents(int page)
        {
            if (page > MaxPages) throw new ArgumentOutOfRangeException(nameof(page), $"page cannot be greater than {MaxPages}");

            string url = $"https://api.github.com/orgs/microsoft/events?page={page}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            // GetEvents is a singleton. ETags are cached in a class field (Storage or Redis would be better)
            // If we have saved the latest ETag for this page then set If-None-Match header
            if (ETag(page) != null)
            {
                request.Headers.IfNoneMatch.Add(ETag(page));
                _log.LogInformation($"If-None-Match {ETag(page)}");
            }

            var response = await _http.SendAsync(request);

            // HTTP STATUS 304
            // There are no new events since the last request. Return an empty result.
            // ETag does not change. 304 response does not count against rate limit.
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _log.LogInformation($"{url} Not Modified ETag = {ETag(page)}");
                return new GetPageOfEventsResult();
            }

            // HTTP STATUS 403
            // Rate limit has been exceeded. Save the current rate limits and throw
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _log.LogError(await response.Content.ReadAsStringAsync());
                SaveRateLimits(response.Headers);
                response.EnsureSuccessStatusCode(); // throw the 403
            }

            // throws here if not a 2XX HTTP status code
            response.EnsureSuccessStatusCode();

            SaveRateLimits(response.Headers);

            SaveETag(page, response.Headers.ETag);
            _log.LogInformation($"ETag {ETag(page)}");

            return new GetPageOfEventsResult(await JsonHelper.DeserializeEvents(response));
        }

        private EntityTagHeaderValue ETag(int page) => _eTags[page - 1];
        private void SaveETag(int page, EntityTagHeaderValue eTag) => _eTags[page - 1] = eTag;

        private bool IsRateLimitExhausted() => _rateLimitRemaining < 1 && _rateLimitResetDateTime > DateTime.UtcNow;

        private void SaveRateLimits(HttpResponseHeaders headers)
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
        public GetPageOfEventsResult() : this(new Event[0]) { }

        public GetPageOfEventsResult(Event[] events)
        {
            Events = events;
        }

        public Event[] Events { get; set; }

        public bool HasEvents => Events.Any();

        /// <summary>
        /// A full page of events is 30 in GitHub API v2. API does not return a total page count. If 
        /// less than a full page was returned, this is the last page.
        /// </summary>
        public bool IsLastPageOfEvents => Events.Length < 30;
    }
}
