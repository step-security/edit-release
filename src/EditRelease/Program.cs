using CommandLine;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EditRelease;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        return await Parser.Default.ParseArguments<Options>(args)
                                   .MapResult(o => RunActionAsync(o),
                                              _ => Task.FromResult(-1)) // invalid arguments
                                   .ConfigureAwait(false);
    }

    private static async Task<int> RunActionAsync(Options options)
    {
        Console.WriteLine($"{Assembly.GetExecutingAssembly().GetName().Name} v{Assembly.GetExecutingAssembly().GetName().Version} started...");

        string repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/'))
        {
            Console.WriteLine("Error: GITHUB_REPOSITORY environment variable not found.");
            return -2;
        }

        // Validate subscription
        bool? repoPrivate = null;
        string eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        if (!string.IsNullOrEmpty(eventPath) && File.Exists(eventPath))
        {
            try
            {
                using var eventDoc = JsonDocument.Parse(File.ReadAllText(eventPath));
                if (eventDoc.RootElement.TryGetProperty("repository", out var repoElement)
                    && repoElement.ValueKind == JsonValueKind.Object
                    && repoElement.TryGetProperty("private", out var privateElement)
                    && (privateElement.ValueKind == JsonValueKind.True || privateElement.ValueKind == JsonValueKind.False))
                {
                    repoPrivate = privateElement.GetBoolean();
                }
            }
            catch
            {
                // ignore malformed event payloads
            }
        }

        const string upstream = "irongut/EditRelease";
        string actionRepo = Environment.GetEnvironmentVariable("GITHUB_ACTION_REPOSITORY") ?? string.Empty;
        const string docsUrl = "https://docs.stepsecurity.io/actions/stepsecurity-maintained-actions";

        Console.WriteLine(string.Empty);
        Console.WriteLine("\u001b[1;36mStepSecurity Maintained Action\u001b[0m");
        Console.WriteLine($"Secure drop-in replacement for {upstream}");
        if (repoPrivate == false)
            Console.WriteLine("\u001b[32m✓ Free for public repositories\u001b[0m");
        Console.WriteLine($"\u001b[36mLearn more:\u001b[0m {docsUrl}");
        Console.WriteLine(string.Empty);

        if (repoPrivate != false)
        {
            string serverUrl = Environment.GetEnvironmentVariable("GITHUB_SERVER_URL") ?? "https://github.com";
            var body = new Dictionary<string, string> { { "action", actionRepo } };
            if (serverUrl != "https://github.com") body["ghes_server"] = serverUrl;

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var apiUrl = $"https://agent.api.stepsecurity.io/v1/github/{repo}/actions/maintained-actions-subscription";
                var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(apiUrl, content).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    Console.WriteLine("\u001b[1;31mThis action requires a StepSecurity subscription for private repositories.\u001b[0m");
                    Console.WriteLine($"\u001b[31mLearn how to enable a subscription: {docsUrl}\u001b[0m");
                    return -2;
                }
            }
            catch
            {
                Console.WriteLine("Timeout or API not reachable. Continuing to next step.");
            }
        }

#if DEBUG
        if (string.IsNullOrWhiteSpace(options.Token))
            options.Token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
#endif

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            Console.WriteLine("Error: Authentication token not found, use either GITHUB_TOKEN or a Personal Access Token.");
            return -2;
        }

        Console.WriteLine($"Repository: {repo}, Release Id: {options.ReleaseId}");

        try
        {
            string owner = repo.Split("/")[0];
            string repoName = repo.Split("/")[1];

            GitHubClient client = new(new ProductHeaderValue("step-security-edit-release-action"))
            {
                Credentials = new Credentials(options.Token)
            };

            Release release = await client.Repository.Release.Get(owner, repoName, options.ReleaseId).ConfigureAwait(false);

            if (release == null)
            {
                Console.WriteLine($"Error: Unable to find Release Id {options.ReleaseId} in {repo}.");
                return -2;
            }

            Console.WriteLine($"Release Found - Id: {release.Id}, Tag: {release.TagName}, Author: {release.Author.Login}.");

            ReleaseUpdate updateRelease = release.ToUpdate();

            if (options.Draft.HasValue)
                updateRelease.Draft = options.Draft.Value;

            if (options.Prerelease.HasValue)
                updateRelease.Prerelease = options.Prerelease.Value;

            if (!string.IsNullOrWhiteSpace(options.Name))
                updateRelease.Name = options.ReplaceName ? options.Name : $"{release.Name} {options.Name}";

            if (options.ReplaceBody)
                updateRelease.Body = string.Empty;

            if (!string.IsNullOrWhiteSpace(options.Body))
            {
                updateRelease.Body = string.IsNullOrWhiteSpace(updateRelease.Body)
                    ? $"{options.Body}{Environment.NewLine}"
                    : $"{updateRelease.Body}{BodySpacing(options.Spacing)}{options.Body}{Environment.NewLine}";
            }

            if (options.Files?.Any() == true && !string.IsNullOrWhiteSpace(options.Files.First()))
                updateRelease.Body = AddFilesToBody(updateRelease.Body, options);

            var result = await client.Repository.Release.Edit(owner, repoName, options.ReleaseId, updateRelease).ConfigureAwait(false);
            if (result != null)
            {
                Console.WriteLine($"Release Updated - Id: {release.Id}, Tag: {release.TagName}, Author: {release.Author.Login}.");
            }
            else
            {
                Console.WriteLine($"Error: Unable to update Release Id: {release.Id}.");
                return -2;
            }

            return 0; // success
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.GetType()} - {ex.Message}");
            return -3; // unhandled error
        }
    }

    private static string BodySpacing(int spacing)
    {
        string value = string.Empty;
        while (spacing > 0)
        {
            value = $"{value}{Environment.NewLine}";
            spacing--;
        }
        return value;
    }

    private static string AddFilesToBody(string body, Options options)
    {
        string spacing = BodySpacing(options.Spacing);
        StringBuilder bodyText = new(body);
        foreach (string filename in options.Files)
        {
            if (bodyText.Length > 0)
                bodyText.Append(spacing);
            bodyText.Append(File.ReadAllText(filename));
        }
        return bodyText.ToString();
    }
}
