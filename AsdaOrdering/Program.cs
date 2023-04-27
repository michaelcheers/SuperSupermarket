using OpenAI_API.Chat;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

// Use gpt-3.5-turbo
OpenAI_API.OpenAIAPI api = new OpenAI_API.OpenAIAPI(new OpenAI_API.APIAuthentication
{
    ApiKey = File.ReadAllText("openai.key")
});