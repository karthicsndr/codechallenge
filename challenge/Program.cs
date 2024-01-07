using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace challenge
{
    class Program
    {
        #region Variables
        private static readonly HttpClient apiClient = GetClient();
        #endregion

        #region Main Program
        static async Task Main(string[] args)
        {
            try
            {
                const string tokenUrl = "/api/token";

                // This will get token information
                var token = await GetResult<Token>(tokenUrl).ConfigureAwait(false);

                string categoryUrl = $"/api/categories/{token.token}";
                string subscribersUrl = $"/api/subscribers/{token.token}";

                // This will get all magazine categories
                var resAllCategories = await GetResult<ApiResponse<string>>(categoryUrl).ConfigureAwait(false);
                var allCategories = resAllCategories.data.ToHashSet();

                // This will get all subscribers
                var resAllSubscribers = await GetResult<ApiResponse<Subscriber>>(subscribersUrl).ConfigureAwait(false);
                var allSubscribers = resAllSubscribers.data;

                // This will get all magazine details belongs to each category
                var resMagazines = await Task.WhenAll(allCategories.Select(e =>
                    GetResult<ApiResponse<Magazine>>($"/api/magazines/{token.token}/{e}")
                )).ConfigureAwait(false);

                var allMagazines = CovertToSingleRes(resMagazines);

                // This will get subscribers details subscribed to at least one magazine in each category.
                var result = GetSubscribersSubscribedToAllCategories(allSubscribers, allMagazines, allCategories);
                var answer = new Answer { subscribers = result };             

                // This will post the answer
                var postUrl = $"/api/answer/{token.token}";
                var postResult = await PostData(postUrl, answer).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(postResult));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
        #endregion

        #region Methods
        static List<Magazine> CovertToSingleRes(IEnumerable<ApiResponse<Magazine>> res)
        {
            return res.SelectMany(response => response.data).ToList();
        }

        static async Task<Response> PostData(string url, Answer data)
        {
            var response = await apiClient.PostAsJsonAsync(url, data).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var res = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<Response>(res);
        }

        static HttpClient GetClient()
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri("http://magazinestore.azurewebsites.net"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            return client;
        }

        static List<Guid> GetSubscribersSubscribedToAllCategories(List<Subscriber> subscribers, List<Magazine> magazines, HashSet<string> categories)
        {
            var magazinesByCategory = magazines.GroupBy(m => m.category)
                .ToDictionary(group => group.Key, group => new HashSet<int>(group.Select(m => m.id)));

            var result = new ConcurrentBag<Guid>();

            Parallel.ForEach(subscribers, subscriber =>
            {
                if (magazinesByCategory.All(categoryMagazines =>
                    categoryMagazines.Value.Overlaps(subscriber.magazineIds)))
                {
                    result.Add(subscriber.id);
                }
            });

            return result.ToList();
        }

        static async Task<T> GetResult<T>(string url) where T : Token
        {
            var response = await apiClient.GetAsync(url).ConfigureAwait(false);
            var res = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var resToken = JsonSerializer.Deserialize<T>(res);
            if (resToken is { success: false })
            {
                throw new Exception($"{resToken.message}");
            }
            return resToken;
        }
        #endregion

        #region Class Properties

        private class Token
        {
            public bool success { get; set; }
            public string token { get; set; }
            public string message { get; set; }
        }
        private class ApiResponse<T> : Token
        {
            public List<T> data { get; set; }
        }
        private class Subscriber
        {
            public Guid id { get; set; }
            public string firstName { get; set; }
            public string lastName { get; set; }
            public IList<int> magazineIds { get; set; }
        }
        private class Magazine
        {
            public int id { get; set; }
            public string name { get; set; }
            public string category { get; set; }
        }
        private class Answer
        {
            public List<Guid> subscribers { get; set; }
        }
        public class AnswerResponse
        {
            public string totalTime { get; set; }
            public bool answerCorrect { get; set; }
            public List<string>? shouldBe { get; set; }
        }
        class Response : Token
        {
            public AnswerResponse data { get; set; }
        }

        #endregion
    }
}
