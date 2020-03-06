using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

[assembly: FunctionsStartup(typeof(GithubStream.Startup))]

namespace GithubStream
{
    public class Startup : FunctionsStartup
    {
        public Startup()
        {
        }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            var executioncontextoptions = builder.Services.BuildServiceProvider()
                .GetService<IOptions<ExecutionContextOptions>>().Value;
            var currentDirectory = executioncontextoptions.AppDirectory;

            builder.Services.AddOptions<GitHubOptions>()
                .Configure<IConfiguration>((settings, configuration) =>
                {
                    configuration.GetSection(nameof(GitHubOptions)).Bind(settings);
                });

            var config = new ConfigurationBuilder()
               .SetBasePath(currentDirectory)
               .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
               .AddEnvironmentVariables()
               .Build();

            builder.Services.AddSingleton((s) =>
            {
                return new CosmosClient(config["CosmosConnectionString"]);
            });
        }
    }

    public class CosmosOptions
    {
        public string CosmosConnectionString { get; set; }
        public string CosmosDbName { get; set; }
        public string CosmosCollection { get; set; }
    }

    public class GitHubOptions
    {
        public string GitHubAppClientId { get; set; }
        public string GitHubAppClientSecret { get; set; }
    }

}