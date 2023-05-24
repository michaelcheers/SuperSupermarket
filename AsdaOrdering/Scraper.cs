using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AI.Dev.OpenAI.GPT;
using Microsoft.Playwright;
using OpenAI_API.Chat;
using OpenAI_API.Models;

namespace AsdaOrdering
{
    internal class Scraper
    {
        // Aldi, Asda, Ocado, Sainsbury's, Tesco, Waitrose, Coop
        public static readonly List<Supermarket> supermarkets = new()
        {
            // https://groceries.aldi.co.uk/en-GB/Search?keywords=green+lentils&sortBy=DisplayPrice&sortDirection=asc
            //new Supermarket("https://groceries.aldi.co.uk/en-GB/Search?keywords={0}&sortBy=DisplayPrice&sortDirection=asc", "+"),
            // https://groceries.asda.com/search/green%20lentils/products?sort=price+asc
            new Supermarket("https://groceries.asda.com/search/{0}/products?sort=price+asc", "%20"),
            // https://www.ocado.com/search?entry=green%20lentils&sort=PRICE_PER_ASC
            new Supermarket("https://www.ocado.com/search?entry={0}&sort=PRICE_PER_ASC", "%20"),
            // https://www.sainsburys.co.uk/gol-ui/SearchResults/green%20lentils/category:/sort:price
            new Supermarket("https://www.sainsburys.co.uk/gol-ui/SearchResults/{0}/category:/sort:price", "%20"),
            // https://www.tesco.com/groceries/en-GB/search?query=green%20lentils&sortBy=price-ascending
            new Supermarket("https://www.tesco.com/groceries/en-GB/search?query={0}&sortBy=price-ascending", "%20"),
            // https://www.waitrose.com/ecom/shop/search?searchTerm=strawberries&sortBy=PRICE_LOW_2_HIGH
            new Supermarket("https://www.waitrose.com/ecom/shop/search?searchTerm={0}&sortBy=PRICE_LOW_2_HIGH", "%20"),
            // https://shop.coop.co.uk/search?term=green%20lentils
            //new Supermarket("https://shop.coop.co.uk/search?term={0}", "%20")
        };

        public static async Task<List<Product>> Search (string query, Supermarket supermarket)
        {
            string url = string.Format(supermarket.searchFormatString, query.Replace(" ", supermarket.spaceCharacter));
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync();
            var page = await browser.NewPageAsync(new()
            {
                Permissions = new[] { "clipboard-read", "clipboard-write" },
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36"
            });

            IResponse response = await page.GotoAsync(url, new()
            {
                Timeout = 120000
            });
            Console.WriteLine(response.Status);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Simulate Ctrl+A (Select All)
            await page.Keyboard.DownAsync("Control");
            await page.Keyboard.PressAsync("a");
            await page.Keyboard.UpAsync("Control");

            // Simulate Ctrl+C (Copy)
            await page.Keyboard.DownAsync("Control");
            await page.Keyboard.PressAsync("c");
            await page.Keyboard.UpAsync("Control");

            string innerText = await page.EvaluateAsync<string>("document.body.innerHTML");

            // Get clipboard content
            string clipboardContent = await page.EvaluateAsync<string>("navigator.clipboard.readText()");

            // Here you would send the clipboardContent to ChatGPT and ask it to convert it to a list with quantity, item, and price.
            // Assuming you have a function called ProcessWithChatGPT that does this, you might do something like:
            List<Product> processedProducts = await ProcessWithChatGPT(clipboardContent, query);

            return processedProducts;
        }

        public static async Task<List<Product>> ProcessWithChatGPT (string clipboardContent, string productName)
        {
            string prompt = @"You are Data-Entry-GPT, designed for the sole purpose of converting a list of items into a list of items with quantities and prices.
If you see an item, you make sure to put it in the format, you enter the quantity, item, and price of each item in the format ""- {quantity} {item name} {price} ({multi-buy number} for {multi-buy price})"" and include nothing else to avoid any issues.

Example:
""- 1kg rice £1.50"".

As Data-Entry-GPT, you never put the quantity after the name of the item (eg. ""- rice 1kg £1.50"") because you know automated system wouldn't understand that.

As Data-Entry-GPT, if you see a multi-buy offer, you put it in parentheses (eg. ""- 1kg rice £1.50 (2 for £2.50)"").

As Data-Entry-GPT, if you see a price per unit, you put in parentheses. (eg. ""- 1kg rice £1.50 (£1.50/kg)"").

As Data-Entry-GPT, if you see a weight or volume in g or ml, you convert it to kg or l respectively (eg. Data-Entry-GPT enters 500g as 0.5kg) because you know the automated system wouldn't understand it if you didn't.

As Data-Entry-GPT, you will ignore all products unrelated to """ + productName + @""".

".Replace(Environment.NewLine, "\n");
            int promptTokens = GetNumberOfTokens(prompt);

            // Split clipboard content into chunks of 4000 tokens
            string[] clipboardChunks = SplitByTokenLimit(clipboardContent, promptTokens);

            // Process each chunk with ChatGPT
            List<Product> processedProducts = new();
            foreach (string chunk in clipboardChunks)
            {
                for (int n = 0; n < 3; n++)
                {
                    try
                    {
                        string result = await RunGPT("", prompt + chunk);
                        ParseChatGPTResult(result, processedProducts);
                    }
                    catch { continue; }
                    break;
                }
            }
            return processedProducts;
        }

        public static void ParseChatGPTResult (string result, List<Product> processedProducts)
        {
            foreach (string rawLine in result.Split('\n'))
            {
                if (!rawLine.StartsWith("- ")) continue;
                if (rawLine.Contains("out of stock", StringComparison.InvariantCultureIgnoreCase)) continue;

                List<string> paranthesized = new();
                string line = rawLine;
                while (line.IndexOf('(') is int i and not -1)
                {
                    int i2 = line.IndexOf(')', i + 1);
                    if (i2 == -1) break;
                    paranthesized.Add(result[(i + 1)..i2]);
                    line = line[..i] + line[(i2 + 1)..];
                }
                line = line[2..].Trim();
                int firstSpace = line.IndexOf(' '), lastSpace = line.LastIndexOf(' ');
                if (firstSpace == -1 || lastSpace == -1) throw new Exception();
                string amount = line[0..firstSpace];
                if (!char.IsDigit(amount[0])) throw new Exception();
                string itemName = line[(firstSpace + 1)..lastSpace];
                string price = line[(lastSpace + 1)..];

                // Split amount into number and unit
                // Get first non-number character
                int unitIndex = amount.IndexOf(amount.FirstOrDefault(c => !char.IsDigit(c)));
                string unit = "";
                if (unitIndex != -1)
                {
                    unit = amount[unitIndex..];
                    amount = amount[..unitIndex];
                }

                // Convert amount to decimal
                decimal amountDecimal = decimal.Parse(amount);

                static decimal ParsePrice(string price)
                {
                    decimal priceMultiplier = 1;
                    // Remove currency symbol if first item not digit
                    if (!char.IsDigit(price[0])) price = price[1..];
                    // Get pence
                    else if (price.EndsWith("p"))
                    {
                        price = price[..^1];
                        priceMultiplier = 0.01m;
                    }

                    // Convert price to decimal
                    return priceMultiplier * decimal.Parse(price);
                }

                decimal priceDecimal = ParsePrice(price);

                // Add to list
                processedProducts.Add(new(itemName, amountDecimal, unit, priceDecimal));

                foreach (string item in paranthesized)
                {
                    int i = item.IndexOf(" for ");
                    if (i == -1) continue;
                    string offerAmount = item[..i];
                    string offerPrice = item[(i + " for ".Length)..];
                    if (char.IsDigit(offerPrice[0]) && !offerPrice.EndsWith("p") && !char.IsDigit(offerAmount[0]))
                    {
                        string offerPriceR = offerAmount[1..];
                        offerAmount = offerPrice;
                        offerPrice = offerPriceR;
                    }
                    decimal offerAmountDecimal = decimal.Parse(offerAmount);
                    decimal offerPriceDecimal = ParsePrice(offerPrice);
                    if (offerPriceDecimal / offerAmountDecimal >= priceDecimal / amountDecimal) continue;
                    processedProducts.Add(new(itemName, offerAmountDecimal * amountDecimal, unit, offerPriceDecimal));
                }
            }
        }

        public static int GetNumberOfTokens(string text) => GPT3Tokenizer.Encode(text).Count;

        public const int maxTokens = 4000;

        public static string[] SplitByTokenLimit(string text, int buffer, int extra = 100) =>
            GetNumberOfTokens(text) <= (maxTokens - buffer)
                ? (new[] { text })
                : Enumerable.Concat(
                    SplitByTokenLimit(text.Substring(0, text.Length / 2 + extra), maxTokens - buffer),
                    SplitByTokenLimit(text.Substring(text.Length / 2 - extra), maxTokens - buffer)
                ).ToArray();

        public static async Task<string> RunGPT(string systemText, string userText)
        {
            OpenAI_API.OpenAIAPI openAIAPI = new OpenAI_API.OpenAIAPI(new(File.ReadAllText("openai.apikey")));
            var chat = openAIAPI.Chat.CreateConversation(new ChatRequest
            {
                Model = Model.ChatGPTTurbo,
                MaxTokens = 1000
            });
            chat.AppendSystemMessage(systemText);
            chat.AppendUserInput(userText);
            StringBuilder msg = new();
            await foreach (string result in chat.StreamResponseEnumerableFromChatbotAsync())
            {
                msg.Append(result);
            }
            return msg.ToString();
        }
    }
}
