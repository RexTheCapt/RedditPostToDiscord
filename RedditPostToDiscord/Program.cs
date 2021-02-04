using Discord.WebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RedditPostToDiscord
{
    class Program
    {
        private static DiscordSocketClient _client = null;
        private static JObject _settings;
        private DateTime _lastRedditGetTime = DateTime.MinValue;

        public static void Main() => new Program().MainAsync().GetAwaiter().GetResult();

        private async Task MainAsync()
        {
            if (_client == null)
                _client = new DiscordSocketClient();
            else
                throw new Exception("client is already assigned");

            _settings = LoadSettings();

            _client.Log += DiscordClient_log;

            string token = _settings.Value<string>("token");

            await _client.LoginAsync(Discord.TokenType.Bot, token);
            await _client.StartAsync();

            var postChannel = _client.GetChannel(_settings.Value<ulong>("post channel"));
            var generalChannel = _client.GetChannel(_settings.Value<ulong>("general id"));

            await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"Next general message: {_settings.Value<DateTime>("general time")}"));

            while (true)
            {
                Thread.Sleep((postChannel == null || generalChannel == null ? 1000 : 100));

                #region Get channels
                if (postChannel == null)
                {
                    postChannel = _client.GetChannel(_settings.Value<ulong>("post channel"));

                    if (postChannel == null)
                        continue;
                }

                if (generalChannel == null && _settings.Value<ulong>("general id") != 0)
                {
                    generalChannel = _client.GetChannel(_settings.Value<ulong>("general id"));

                    if (generalChannel == null)
                        continue;
                }
                #endregion

                #region Post to set channel ever x time
                if (_lastRedditGetTime.AddSeconds(_settings.Value<int>("post delay")) < DateTime.Now)
                {
                    var posts = GetPosts();

                    var msgChnl = postChannel as ISocketMessageChannel;
                    DateTime lastMessage = DateTime.MinValue;

                    bool posted = false;

                    int index = 0;

                    foreach (var p in posts)
                    {
                        var data = p.Value<JObject>("data");
                        string url = data.Value<string>("url");

                        if (SentBefore(url))
                        {
                            index++;
                            continue;
                        }

                        await msgChnl.SendMessageAsync(url);
                        await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"Posted [{index}] {url} to set channel"));
                        
                        AddSentUrl(url);
                        lastMessage = DateTime.Now;

                        posted = true;
                        break;
                    }

                    if (!posted)
                        await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Warning, "Main", "No more images to post"));

                    _lastRedditGetTime = DateTime.Now;

                    await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"Next post: {_lastRedditGetTime.AddSeconds(_settings.Value<int>("post delay"))}"));
                }
                #endregion

                #region Post to general once a day
                if (_settings.Value<DateTime>("general time") < DateTime.Now && generalChannel != null && _settings.Value<ulong>("general id") != 0)
                {
                    Random rdm = new Random();
                    var posts = GetPosts();
                    var select = posts[rdm.Next(0, posts.Count)].Value<JObject>("data");

                    var msgChnl = generalChannel as ISocketMessageChannel;

                    string url = select.Value<string>("url");

                    await msgChnl.SendMessageAsync(url);
                    await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"Posted {url} to general"));

                    int hours = rdm.Next(0, 24);
                    int minutes = rdm.Next(0, 60);
                    int seconds = rdm.Next(0, 60);

                    _settings.Property("general time").Value = DateTime.Now.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
                    await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"Next general message time: {_settings.Value<DateTime>("general time")}"));
                }
                #endregion

                // TODO:
                // Connect to an random voice channel with people
                // Play an random sound from an folder
                // Disconnect

                bool shutdown = false;
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q)
                        shutdown = true;
                }

                if (shutdown)
                {
                    await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", "Logging out"));
                    await _client.LogoutAsync();
                    _client.Dispose();

                    await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", "Saving settings..."));
                    using (StreamWriter sw = new StreamWriter("settings.json"))
                        sw.WriteLine(JsonConvert.SerializeObject(_settings));
                    break;
                }
            }

            await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", "Shutdown complete"));
            Console.ReadLine();
        }

        private void AddSentUrl(string url)
        {
            JProperty j1 = new JProperty("url", url);
            JProperty j2 = new JProperty("timestamp", DateTime.Now);

            JObject jo = new JObject();
            jo.Add(j1);
            jo.Add(j2);

            if (!_settings.ContainsKey("sent urls"))
                _settings.Add("sent urls", new JArray());

            _settings.Value<JArray>("sent urls").Add(jo);
        }

        private bool SentBefore(string url)
        {
            JArray ja = _settings.Value<JArray>("sent urls");

            if (ja == null)
                return false;

            foreach (JToken jt in ja.Children())
            {
                string urlString = jt.Value<string>("url");
                DateTime timestamp = jt.Value<DateTime>("timestamp");

                if (urlString == url)
                    return true;
            }

            return false;
        }

        private JObject LoadSettings()
        {
            string raw = "";
            JObject jObject = null;

            #region Create default settings
            if (!File.Exists("settings.json"))
                using (StreamWriter sw = new StreamWriter("settings.json"))
                {
                    JObject j = new JObject();

                    #region Bot token // token
                    Console.WriteLine("Please input bot token:");
                    Console.Write("String > ");

                    j.Add("token", Console.ReadLine());
                    #endregion

                    #region Target channel ID // post channel
                    RetryChannelId:
                    Console.WriteLine("Please input target channel ID:");
                    Console.Write("Ulong > ");

                    if (ulong.TryParse(Console.ReadLine(), out ulong targetChannelId))
                        j.Add("post channel", targetChannelId);
                    else
                        goto RetryChannelId;
                    #endregion

                    #region Add delay in seconds // post delay
                    RetrySeconds:
                    Console.WriteLine("Please input delay in seconds to send image:");
                    Console.Write("Int > ");

                    if (int.TryParse(Console.ReadLine(), out int seconds))
                        j.Add("post delay", seconds);
                    else
                        goto RetrySeconds;
                    #endregion

                    #region Add next general message time // general time
                    j.Add("general time", DateTime.Now);
                    #endregion

                    #region Add general channel ID // general id
                    RetryGeneralChannelId:
                    Console.WriteLine("Input general channel ID:");
                    Console.Write("Uint > ");

                    if (ulong.TryParse(Console.ReadLine(), out ulong channelId))
                        j.Add("general id", channelId);
                    else
                        goto RetryGeneralChannelId;
                    #endregion

                    raw = JsonConvert.SerializeObject(j);

                    sw.WriteLine(raw);
                    raw = "";
                    jObject = j;
                }
            #endregion

            if (jObject != null)
                return jObject;

            using (StreamReader sr = new StreamReader("settings.json"))
                while (!sr.EndOfStream)
                    raw += sr.ReadLine();

            jObject = JsonConvert.DeserializeObject<JObject>(raw);

            #region retro load settings
            #region Bot token // token
            if (!jObject.ContainsKey("token"))
            {
                Console.WriteLine("Please input bot token:");
                Console.Write("String > ");
                jObject.Add("token", Console.ReadLine());
            }
            #endregion

            #region Post channel id // post channel
            if (!jObject.ContainsKey("post channel"))
            {
                RetryChannelId:
                Console.WriteLine("Please input post channel ID");
                Console.Write("Ulong > ");

                if (ulong.TryParse(Console.ReadLine(), out ulong channelId))
                    jObject.Add("post channel", channelId);
                else
                    goto RetryChannelId;
            }
            #endregion

            #region Add delay // post delay
            if (!jObject.ContainsKey("post delay"))
            {
                Retry:
                Console.WriteLine("Please input post delay:");
                Console.Write("Int > ");

                if (int.TryParse(Console.ReadLine(), out int res))
                    jObject.Add("post delay", res);
                else
                    goto Retry;
            }
            #endregion

            #region Next time to post in general // general time
            if (!jObject.ContainsKey("general time"))
            {
                jObject.Add("general time", DateTime.MinValue);
            }
            #endregion

            #region General channel ID // general id
            if (!jObject.ContainsKey("general id"))
            {
                Retry:
                Console.WriteLine("Please insert general channel ID, leave empty to disable:");
                Console.Write("Ulong > ");

                string input = Console.ReadLine();

                if (ulong.TryParse(input, out ulong res))
                    jObject.Add("general id", res);
                else if (input == "")
                    jObject.Add("general id", 0);
                else
                    goto Retry;
            }
            #endregion
            #endregion

            return jObject;
        }

        private Task DiscordClient_log(Discord.LogMessage arg)
        {
            Console.WriteLine($"[{DateTime.Now}] [{arg.Severity}] {arg.Source}: {arg.Message}");
            return Task.CompletedTask;
        }

        static JArray GetPosts()
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "RedditPostToDiscord/rexthecapt AT gmail.com");
            client.BaseAddress = new Uri("https://www.reddit.com/r/TIHI/top/");

            JObject redditPosts;

            #region Get posts
            using (HttpResponseMessage response = client.GetAsync(".json").Result)
            {
                if (response.IsSuccessStatusCode)
                {
                    string responseString = response.Content.ReadAsStringAsync().Result;
                    redditPosts = JsonConvert.DeserializeObject<JObject>(responseString);
                }
                else
                    throw new NotImplementedException();
            }
            #endregion

            return redditPosts.Value<JObject>("data").Value<JArray>("children");
        }
    }
}
