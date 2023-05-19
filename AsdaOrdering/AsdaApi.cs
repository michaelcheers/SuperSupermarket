using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Linq;

namespace AsdaOrdering
{
    internal class AsdaApi
    {
        public static List<OrderProduct> GetLastOrderProducts(string cookieFilePath)
        {
            // Read ASDA cookie from file
            string cookie = File.ReadAllText(cookieFilePath);

            // Get last order id
            string ordersUrl = "https://groceries.asda.com/api/order/view?showmultisave=true&showrefund=true&pagenum=1&pagesize=25&requestorigin=gi";
            string orderJson = HttpGet(ordersUrl, cookie);
            using JsonDocument doc = JsonDocument.Parse(orderJson);
            string orderId = doc.RootElement.GetProperty("orders")[0].GetProperty("orderId").GetString()
                ?? throw new Exception("No orders found");

            // Get url for last order
            string orderUrl = $"https://groceries.asda.com/api/order/view?showmultisave=true&showrefund=true&orderid={orderId}&responsegroup=extended&pagesize=nolimit&pagenum=1&requestorigin=gi&_={DateTimeOffset.Now.ToUnixTimeSeconds()}";

            // Get products from last order
            string productsJson = HttpGet(orderUrl, cookie);
            using JsonDocument productsDoc = JsonDocument.Parse(productsJson);
            string itemsJson = productsDoc.RootElement.GetProperty("orders")[0].GetProperty("item").GetRawText();

            // Create JsonSerializerOptions to specify the property name handling
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Deserialize the JSON into a list of OrderProduct objects
            List<OrderProduct> orderProducts = JsonSerializer.Deserialize<List<OrderProduct>>(itemsJson, options)
                ?? throw new Exception("No products found");
            return orderProducts;
        }

        private static string HttpGet(string url, string cookie)
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            using HttpResponseMessage response = client.GetAsync(url).Result;
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().Result;
        }

        public class OrderProduct
        {
            public string Desc { get; set; }
            public int Qty { get; set; }
            public decimal Cost { get; set; }
            public decimal Price { get; set; }
            public string PromoDetail { get; set; }
        }
    }
}