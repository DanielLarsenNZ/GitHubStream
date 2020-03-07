using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace GithubStream
{
    internal static class JsonHelper
    {
        public static Payload DeserializePayload(Message message)
        {
            var deserialized = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(message.Body)) as JObject;
            if (deserialized == null) throw new InvalidOperationException($"Failed to deserialize response as JSON.");
            return deserialized["payload"].ToObject<Payload>();
        }

        public static async Task<Event[]> DeserializeEvents(HttpResponseMessage response)
            => JsonConvert.DeserializeObject<Event[]>(await response.Content.ReadAsStringAsync());

        public static string SerializeObject(Event @event) => JsonConvert.SerializeObject(@event);
    }
}
