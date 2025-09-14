using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services;

public interface ICoolerControlApiController
{
    public Task<JArray> GetModes();
    public Task<bool> SetMode(string uid);
}

public class CoolerControlApiController : ICoolerControlApiController
{
    private readonly HttpClient _client;

    public CoolerControlApiController(string baseUrl = "http://127.0.01:11987/")
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl)
        };
    }
    
    public async Task<JArray> GetModes()
    {
        var json = await _client.GetStringAsync("modes");
        var jObj = JObject.Parse(json);
        return (JArray)jObj["modes"];
    }

    public async Task<bool> SetMode(string uid)
    {
        if (!await Login())
            return false;
            
        var response = await _client.PostAsync($"modes-active/{uid}", null);
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> Login()
    {
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes("CCAdmin:coolAdmin"));
        var req = new HttpRequestMessage(HttpMethod.Post, "login");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var response = await _client.SendAsync(req);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync();

        return false;
    }
}