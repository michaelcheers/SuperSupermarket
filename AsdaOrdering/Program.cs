using AsdaOrdering;
using OpenAI_API.Chat;
using OpenAI_API.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

Console.WriteLine("Searching ...");
string query = "strawberries";
Dictionary<string, List<Product>> prices = new();
foreach (Supermarket supermarket in Scraper.supermarkets)
{
    Console.WriteLine($"Searching {supermarket.searchFormatString} ...");
    List<Product> products = await Scraper.Search(query, supermarket);
    Console.WriteLine($"Found {products.Count} products.");
    if (products.Count == 0)
        continue;
    Console.WriteLine("Found products:");
    foreach (Product product in products)
        Console.WriteLine(product);
    prices.Add(supermarket.searchFormatString, products);
}
File.WriteAllText("prices.json", JsonSerializer.Serialize(prices, new JsonSerializerOptions { WriteIndented = true }));
decimal amountWanted = 0.2m;
foreach ((string supermarket, List<Product> product) in prices)
{
    var productsWithAmountsPerKg = product.Select(p => (p, p.GetKgs())).Where(p => p.Item2.HasValue).Select(p => (p.p, kgs: p.Item2!.Value));
    var min = productsWithAmountsPerKg.MinBy(p => p.kgs);
    Console.WriteLine($"{supermarket}: \n" +
        $"- Cheapest per kg: {min.p.name} {min.kgs}/kg");
    var pricesForAmountWanted = productsWithAmountsPerKg.Select(p => (p.p, numberNeeded: Math.Ceiling(amountWanted / p.kgs)));
    var minForAmountWanted = pricesForAmountWanted.MinBy(p => p.numberNeeded * p.p.price);
    Console.WriteLine($"- Cheapest for amount: {minForAmountWanted.numberNeeded} of {minForAmountWanted.p.name} for {minForAmountWanted.numberNeeded * minForAmountWanted.p.price}");
}
return;

Console.WriteLine("Skip asking for recipes. Y/N?");
bool skipRecipes = Console.ReadLine()!.ToLower() == "y";
string[] extractedIngredients;
if (!skipRecipes)
{
    Console.WriteLine("Copy and paste Gousto recipe (Ctrl+A Ctrl+C recipe). Then, write END. When added all recipes, END again.");
    List<string> recipes = new();
    while (true)
    {
        string recipe = ConsoleExtra.ReadUntil();
        if (recipe == string.Empty)
            break;
        recipes.Add(recipe);
    }
    File.WriteAllText("recipes.json", JsonSerializer.Serialize(recipes, new JsonSerializerOptions { WriteIndented = true }));

    //List<string> extractedIngredients = recipes.ConvertAll(recipe => ChatGPTExtra.ExtractIngredients(recipe).Result);
    // Change above line to use async properly
    List<Task<string>> tasks = recipes.ConvertAll(recipe => ChatGPTExtra.ExtractIngredients(recipe));
    extractedIngredients = await Task.WhenAll(tasks);
    File.WriteAllText("ingredients.json", JsonSerializer.Serialize(extractedIngredients, new JsonSerializerOptions { WriteIndented = true }));
}
else
    extractedIngredients = JsonSerializer.Deserialize<string[]>(File.ReadAllText("ingredients.json"))!;

if (skipRecipes)
    Console.WriteLine("Skip converting to orderable ingredients. Y/N?");
string orderableIngredients;
if (skipRecipes && Console.ReadLine()!.ToLower() == "y")
    orderableIngredients = File.ReadAllText("orderableIngredients.txt");
else
{
    Console.WriteLine("Converting to orderable ingredient list.");
    string ingredientsCombined = string.Join("\n\n", extractedIngredients);
    orderableIngredients = await ChatGPTExtra.ConvertIngredientsToAsdaOrder(ingredientsCombined);
    File.WriteAllText("orderableIngredients.txt", orderableIngredients);
}
List<string> urls = new();
foreach (string line in orderableIngredients.Split('\n'))
{
    string l = line.StartsWith("- ") ? line[2..] : line;
    string l2 = l;
    if (line.Contains(" ("))
        l2 = l[..l.IndexOf(" (")];
    Console.WriteLine($"Do you have {l}? Y/N");
    if (Console.ReadLine()!.ToLower() == "n")
        // Example Url: https://groceries.asda.com/search/green%20lentils/products?sort=price+asc
        urls.Add($"https://groceries.asda.com/search/{l2.Replace(" ", "%20")}/products?sort=price+asc");
}
string serialized = JsonSerializer.Serialize(urls, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText("urls.json", serialized);
string script = $"{serialized}.forEach(url => window.open(url));";
Process.Start(new ProcessStartInfo("clip.exe") { RedirectStandardInput = true }).StandardInput.Write(script);
Console.WriteLine("Go to asda.com and do Ctrl+Shift+J and paste the script.");
Console.WriteLine("Here are the quantities required for the ingredients:");
Console.WriteLine(orderableIngredients);

public static class ChatGPTExtra
{
    public static async Task<string> ExtractIngredients(string recipeText)
        => await RunGPT("You are ExtractGPT. Please answer with the extract requested and no other text.\n\nExtract the ingredients from the recipe provided below.", recipeText);

    public static async Task<string> ConvertIngredientsToAsdaOrder(string ingredientsText)
        => await RunGPT("You are AsdaGPT. Please convert the list of ingredients above into a list of items to search for at Asda. Asda is a grocery store. Certain items in recipes need to be converted as such. For example, \"chopped dates\" should just become \"dates\". \"Coriander & Mint\" should be separated into two separate items. Etc. Etc. Quantities should be include but in parantheses. For example, \"1 red onion\" + \"1 red onion\" should become \"red onion (2 onions)\". \"150g black beans\" should become \"black beans (150 g)\".", ingredientsText);

    public static async Task<string> RunGPT (string systemText, string userText)
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

public static class ConsoleExtra
{
    public static string ReadUntil(string endString = "END")
    {
        StringBuilder stringBuilder = new StringBuilder();
        while (true)
        {
            string line = Console.ReadLine()!;
            if (line == endString)
                break;
            stringBuilder.AppendLine(line);
        }
        return stringBuilder.ToString();
    }
}

//const string systemMessage = "You are AsdaGPT." +
//    " You are an AI that can order groceries." +
//    " Provide all grocery orders as a list starting with \"- \". Eg:" +
//    @"
//- bleach 300ml
//- detergent 500ml

//" +
//    "Once you provide that, a search will be performed and you will be asked to select the specific items for the user.";

////var messages = new List<ChatMessage>
////{
////    new ChatMessage(ChatMessageRole.System, systemMessage),
////    new ChatMessage(ChatMessageRole.User, "Suggest breakfast and lunch for the next 3 days. I am mostly vegan.")
////};
//var chat = api.Chat.CreateConversation(new ChatRequest
//{
//    Model = Model.ChatGPTTurbo,
//    //Messages = messages,
//    MaxTokens = 1000,
//    MultipleStopSequences = new string[] { "\n\n\n" }
//});
//chat.AppendSystemMessage(systemMessage);
//chat.AppendUserInput("Suggest breakfast and lunch for the next 3 days. I am mostly vegan. Make a list of meals and then order the groceries.");

//while (true)
//{
//    StringBuilder msg = new();
//    await foreach (string result in chat.StreamResponseEnumerableFromChatbotAsync())
//    {
//        msg.Append(result);
//        Console.Write(result);
//        //if (message == "You are AsdaGPT. You are an AI that can order groceries. Provide all grocery orders as a json array.")
//        //{
//        //    Console.WriteLine("You are AsdaGPT. You are an AI that can order groceries. Provide all grocery orders as a json array.");
//        //    break;
//        //}
//    }
//    string message = msg.ToString();
//    int index = 0;
//    const string separator = "\n- ";
//    while ((index = message.IndexOf(, index)) > -1)
//    {
//        index += separator.Length;
//        // Get contents of that line
//        int endIndex = message.IndexOf('\n', index);
//        string item = message[index..endIndex];
//        // Search the item

//        index = endIndex;
//    }
//    string userInput = Console.ReadLine();
//    if (string.IsNullOrWhiteSpace(userInput))
//        break;
//    chat.AppendUserInput(userInput);
//}

public static class Extensions
{
    public static decimal? GetKgs(this Product product) => product switch
    {
        { quantityType: "g" } => product.quantity / 1000,
        { quantityType: "kg" } => product.quantity,
        _ => null
    };
}