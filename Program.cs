using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Formatting = Newtonsoft.Json.Formatting;

namespace ClaudeCertificationSummariser
{
    // Data models
    public class Subtopic
    {
        public required string Title { get; set; }
        public required string Url { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    public class SummaryResult
    {
        public DateTime GeneratedAt { get; set; }
        public List<Subtopic> Subtopics { get; set; } = [];
    }

    // Web scraper for Claude Certification Guide
    public class WebScraper
    {
        private readonly HttpClient _client;
        private readonly string _sitemapUrl = "https://claudecertificationguide.com/sitemap.xml";

        public WebScraper()
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public async Task<List<Subtopic>> GetAllSubtopicsAsync()
        {
            var subtopics = new List<Subtopic>();

            try
            {
                var xml = await _client.GetStringAsync(_sitemapUrl);
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);

                var ns = new System.Xml.XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("sm", "http://www.sitemaps.org/schemas/sitemap/0.9");

                var nodes = doc.SelectNodes("//sm:url/sm:loc", ns);
                if (nodes == null) return subtopics;

                foreach (System.Xml.XmlNode node in nodes)
                {
                    var url = node.InnerText.Trim();

                    // Only include individual lesson pages e.g. /learn/1-agentic-architecture/1-1-agentic-loops
                    if (!Regex.IsMatch(url, @"/learn/\d+-[^/]+/\d+-[^/]+$"))
                        continue;

                    // Derive a readable title from the slug
                    var slug = url.Split('/').Last();
                    var title = SlugToTitle(slug);

                    subtopics.Add(new Subtopic { Title = title, Url = url });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching sitemap: {ex.Message}");
            }

            return subtopics;
        }

        public async Task<string> GetPageContentAsync(string url)
        {
            try
            {
                var html = await _client.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Extract meta description — the only reliable content on this Next.js CSR site
                var descNode = doc.DocumentNode
                    .SelectSingleNode("//meta[@name='description']/@content"
                    + " | //meta[@property='og:description']/@content");
                var description = descNode?.GetAttributeValue("content", "") ?? string.Empty;

                // Extract page title
                var titleNode = doc.DocumentNode.SelectSingleNode("//title");
                var pageTitle = titleNode?.InnerText.Trim() ?? string.Empty;

                // Combine into structured content for the summariser
                if (!string.IsNullOrWhiteSpace(description))
                    return $"Title: {pageTitle}\nDescription: {description}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching page {url}: {ex.Message}");
            }

            return string.Empty;
        }

        private static string SlugToTitle(string slug)
        {
            // e.g. "1-1-agentic-loops" -> "1.1 Agentic Loops"
            var match = Regex.Match(slug, @"^(\d+)-(\d+)-(.+)$");
            if (match.Success)
            {
                var major = match.Groups[1].Value;
                var minor = match.Groups[2].Value;
                var words = match.Groups[3].Value.Replace("-", " ");
                var titled = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(words);
                return $"{major}.{minor} {titled}";
            }
            return slug.Replace("-", " ");
        }
    }

    // Claude Summariser
    public class ClaudeSummariser
    {
        private readonly string _apiKey;
        private readonly string _endpoint = "https://api.anthropic.com/v1/messages";
        private readonly HttpClient _client;

        public ClaudeSummariser(string apiKey)
        {
            _apiKey = apiKey;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }

        public async Task<string> SummariseAsync(string title, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "No content available to summarise.";

            var prompt = $@"You are an expert study guide writer for the Claude Certified Architect exam.

Lesson: {title}

Source material:
{content}

Write an exam-preparation summary that includes:
1. **Core Concept** – what this lesson is about in 1-2 sentences
2. **Key Points** – 3-5 bullet points of the most important facts/rules to remember
3. **Exam Traps** – 1-2 common mistakes or misconceptions to watch out for

Be concise and specific. Focus on what a candidate needs to know to answer exam questions correctly.";

            var requestBody = new
            {
                model = "claude-haiku-4-5-20251001",
                max_tokens = 600,
                system = "You are a concise technical writer producing exam study notes. Use markdown formatting.",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            try
            {
                var jsonContent = new StringContent(
                    JsonConvert.SerializeObject(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _client.PostAsync(_endpoint, jsonContent);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Anthropic API Error: {responseString}");
                    return $"Error generating summary: {response.StatusCode}";
                }

                dynamic? json = JsonConvert.DeserializeObject(responseString);
                return json?.content?[0]?.text?.ToString() ?? "No summary generated.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling Anthropic API: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }
    }

    // File writer
    public class SummaryFileWriter
    {
        public void WriteToJson(SummaryResult result, string filePath)
        {
            var json = JsonConvert.SerializeObject(result, Formatting.Indented);
            File.WriteAllText(filePath, json);
            Console.WriteLine($"Summaries saved to: {filePath}");
        }

        public void WriteToMarkdown(SummaryResult result, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Claude Certification Guide - Subtopic Summaries");
            sb.AppendLine();
            sb.AppendLine($"*Generated on: {result.GeneratedAt:yyyy-MM-dd HH:mm:ss}*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            int index = 1;
            foreach (var subtopic in result.Subtopics)
            {
                sb.AppendLine($"## {index}. {subtopic.Title}");
                sb.AppendLine();
                sb.AppendLine($"**URL:** [{subtopic.Url}]({subtopic.Url})");
                sb.AppendLine();
                sb.AppendLine("### Summary");
                sb.AppendLine();
                sb.AppendLine(subtopic.Summary);
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                index++;
            }

            File.WriteAllText(filePath, sb.ToString());
            Console.WriteLine($"Summaries saved to: {filePath}");
        }
    }

    // Coordinator (Orchestrator)
    public class Coordinator
    {
        private readonly WebScraper _scraper;
        private readonly ClaudeSummariser _summariser;
        private readonly SummaryFileWriter _writer;

        public Coordinator(string apiKey)
        {
            _scraper = new WebScraper();
            _summariser = new ClaudeSummariser(apiKey);
            _writer = new SummaryFileWriter();
        }

        public async Task RunAsync()
        {
            Console.WriteLine("=== Claude Certification Guide Summariser ===");
            Console.WriteLine();

            // Step 1: Fetch all subtopics
            Console.WriteLine("Step 1: Fetching subtopics from the website...");
            var subtopics = await _scraper.GetAllSubtopicsAsync();
            Console.WriteLine($"Found {subtopics.Count} subtopics.");
            Console.WriteLine();

            if (subtopics.Count == 0)
            {
                Console.WriteLine("No subtopics found. Please check the website structure.");
                return;
            }

            // Step 2: Fetch content for each subtopic
            Console.WriteLine("Step 2: Fetching content for each subtopic...");
            foreach (var subtopic in subtopics)
            {
                Console.WriteLine($"  Fetching: {subtopic.Title}");
                subtopic.Content = await _scraper.GetPageContentAsync(subtopic.Url);
                await Task.Delay(500); // Rate limiting
            }
            Console.WriteLine();

            // Step 3: Summarise each subtopic
            Console.WriteLine("Step 3: Summarising each subtopic using AI...");
            foreach (var subtopic in subtopics)
            {
                Console.WriteLine($"  Summarising: {subtopic.Title}");
                subtopic.Summary = await _summariser.SummariseAsync(subtopic.Title, subtopic.Content);
                await Task.Delay(1000); // Rate limiting for API
            }
            Console.WriteLine();

            // Step 4: Save results to files
            Console.WriteLine("Step 4: Saving summaries to files...");
            var result = new SummaryResult
            {
                GeneratedAt = DateTime.UtcNow,
                Subtopics = subtopics
            };

            _writer.WriteToJson(result, "claude_certification_summaries.json");
            _writer.WriteToMarkdown(result, "claude_certification_summaries.md");

            Console.WriteLine();
            Console.WriteLine("=== Done! ===");
        }
    }

    // Main program
    class Program
    {
        static async Task Main(string[] args)
        {
            // Load API key from appsettings.local.json
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.local.json", optional: true)
                .Build();

            string? apiKey = config["Anthropic:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Write("Enter your Anthropic API key: ");
                apiKey = Console.ReadLine();
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("Anthropic API key is required. Exiting.");
                return;
            }

            var coordinator = new Coordinator(apiKey);
            await coordinator.RunAsync();
        }
    }
}