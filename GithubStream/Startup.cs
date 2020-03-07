using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

[assembly: FunctionsStartup(typeof(GithubStream.Startup))]

namespace GithubStream
{
    public class Startup : FunctionsStartup
    {
        public Startup() { }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            // all of this to get configuration in Startup :/
            string currentDirectory = builder.Services
                .BuildServiceProvider()
                .GetService<IOptions<ExecutionContextOptions>>()
                .Value.AppDirectory;
            var config = new ConfigurationBuilder()
               .SetBasePath(currentDirectory)
               .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
               .AddEnvironmentVariables()
               .Build();

            builder.Services.AddSingleton((s) =>
            {
                var http = new HttpClient();

                // User-Agent header
                http.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue(new ProductHeaderValue("DanielLarsenNZ-GithubStream")));

                // Authorization header
                if (!string.IsNullOrEmpty(config["GitHubAppClientId"]))
                {
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue(
                            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(
                                   $"{config["GitHubAppClientId"]}:{config["GitHubAppClientSecret"]}")));
                }

                return http;
            });

            builder.Services.AddSingleton((s) =>
            {
                return new CosmosClient(config["CosmosConnectionString"]);
            });
        }
    }
}