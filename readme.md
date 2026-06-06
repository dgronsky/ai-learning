# Anthropic Topic Summarisation

A .NET 10 console app that scrapes the [Claude Certification Guide](https://claudecertificationguide.com), fetches lesson metadata, and uses the **Anthropic Claude API** to generate exam-focused summaries.

## Setup

1. Copy the template config and fill in your API key:

   ```bash
   cp appsettings.template.json appsettings.local.json
   ```

2. Edit `appsettings.local.json` and replace the placeholder with your real key:

   ```json
   {
	 "Anthropic": {
	   "ApiKey": "sk-ant-api03-..."
	 }
   }
   ```

   > `appsettings.local.json` is listed in `.gitignore` and will never be committed.

3. Build and run:

   ```bash
   dotnet run
   ```

## Output

- `claude_certification_summaries.json` — full structured data
- `claude_certification_summaries.md` — markdown study guide
