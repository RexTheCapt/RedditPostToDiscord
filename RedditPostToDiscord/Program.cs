using Discord;
using Discord.Net;
using Discord.Rest;
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
        private bool _overrideSend = false;
        private List<IMessage> _deletionQueue = new List<IMessage>();
        private RestUserMessage _purgeMessage = null;

        public static void Main() => new Program().MainAsync().GetAwaiter().GetResult();

        private async Task MainAsync()
        {
            if (_client == null)
                _client = new DiscordSocketClient();
            else
                throw new Exception("client is already assigned");

            _settings = LoadSettings();

            _client.Log += DiscordClient_log;
            _client.MessageReceived += DiscordClient_MessageReceived;

            string token = _settings.Value<string>("token");

            await _client.LoginAsync(Discord.TokenType.Bot, token);
            await _client.StartAsync();

            var postChannel = _client.GetChannel(_settings.Value<ulong>("post channel"));
            var generalChannel = _client.GetChannel(_settings.Value<ulong>("general id"));

            await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"Next general message: {_settings.Value<DateTime>("general time")}"));

            DateTime lastMessageDeletionDatetime = DateTime.MinValue;
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
                if (_lastRedditGetTime.AddSeconds(_settings.Value<int>("post delay")) < DateTime.Now || _overrideSend)
                {
                    _overrideSend = false;
                    var posts = GetPosts();

                    var msgChnl = postChannel as ISocketMessageChannel;
                    DateTime lastMessage = DateTime.MinValue;

                    bool posted = false;

                    int index = 0;

                    foreach (var p in posts)
                    {
                        var data = p.Value<JObject>("data");
                        string url = data.Value<string>("url");
                        string thumbnail = data.Value<string>("thumbnail");

                        bool nsfw = thumbnail.Equals("nsfw", StringComparison.OrdinalIgnoreCase);

                        if (SentBefore(url))
                        {
                            index++;
                            continue;
                        }

                        await msgChnl.SendMessageAsync($"{(nsfw ? "||" : "")}{url}{(nsfw ? "||" : "")}");
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
                /*
                if (_settings.Value<DateTime>("general time") < DateTime.Now && generalChannel != null && _settings.Value<ulong>("general id") != 0)
                {
                    Random rdm = new Random();
                    var posts = GetPosts();
                    var select = posts[rdm.Next(0, posts.Count)].Value<JObject>("data");
                    string thumbnail = select.Value<string>("thumbnail");

                    bool nsfw = thumbnail.Equals("nsfw", StringComparison.OrdinalIgnoreCase);

                    var msgChnl = generalChannel as ISocketMessageChannel;

                    string url = select.Value<string>("url");

                    await msgChnl.SendMessageAsync($"{(nsfw ? "||" : "")}{url}{(nsfw ? "||" : "")}");
                    await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"Posted {url} to general"));

                    int hours = rdm.Next(0, 24);
                    int minutes = rdm.Next(0, 60);
                    int seconds = rdm.Next(0, 60);

                    _settings.Property("general time").Value = DateTime.Now.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
                    await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"Next general message time: {_settings.Value<DateTime>("general time")}"));
                }
                */
                #endregion

                #region Delete messages in deletion queue
                /*
                if (lastMessageDeletionDatetime.AddSeconds(2) < DateTime.Now && _deletionQueue.Count > 0)
                {
                    if (_purgeMessage == null)
                    {
                        _purgeMessage = await (postChannel as ISocketMessageChannel).SendMessageAsync($"Deleting {_deletionQueue.Count} messages...");
                        var posts = GetPosts("eyebleach");
                        Random rdm = new Random();
                        await (postChannel as ISocketMessageChannel).SendMessageAsync(posts[rdm.Next(0, posts.Count)].Value<JObject>("data").Value<string>("url"));
                    }

                    try
                    {
                        await _deletionQueue[0].DeleteAsync();
                    }
                    catch (Exception e)
                    {
                        Type t = e.GetType();

                        if (t == typeof(HttpException))
                        {
                            HttpException he = (HttpException)e;

                            if (he.HttpCode == System.Net.HttpStatusCode.NotFound)
                            {
                                // Do nothing
                            }
                            else
                                await DiscordClient_log(new LogMessage(LogSeverity.Critical, "main", "Not implemented", he));
                        }
                        else
                            await DiscordClient_log(new LogMessage(LogSeverity.Critical, "main", "Not implemented", e));
                    }
                    _deletionQueue.RemoveAt(0);

                    if (_deletionQueue.Count == 0)
                    {
                        await _purgeMessage.DeleteAsync();
                        _purgeMessage = null;
                    }
                    else
                        await _purgeMessage.ModifyAsync(x =>
                    {
                        x.Content = $"Deleting {_deletionQueue.Count}";
                    });

                    lastMessageDeletionDatetime = DateTime.Now;
                }
                */
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
                    await (postChannel as ISocketMessageChannel).SendMessageAsync("Shutting down");
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

        private async Task DiscordClient_MessageReceived(SocketMessage arg)
        {
            if (arg.Channel.Id == _settings.Value<ulong>("post channel"))
            {
                if (arg.Content.StartsWith("r/", StringComparison.OrdinalIgnoreCase))
                {
                    await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"User {arg.Author.Username}#{arg.Author.DiscriminatorValue} [{arg.Author.Id}] changed the subreddit to {arg.Content}"));
                    string subreddit = arg.Content.Split('/')[1];

                    if (GetPosts(subreddit).Count > 0)
                    {
                        await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Info, "main", $"success in setting subreddit to {subreddit}"));
                        _settings.Property("subreddit").Value = subreddit;
                        await arg.Channel.SendMessageAsync($"<@{arg.Author.Id}> subreddit \"{subreddit}\" is now selected");
                    }
                    else
                    {
                        await DiscordClient_log(new Discord.LogMessage(Discord.LogSeverity.Error, "main", $"failed setting subreddit to {subreddit}"));
                        await arg.Channel.SendMessageAsync($"<@{arg.Author.Id}> could not get posts from \"{subreddit}\", settings is not updated.");
                    }
                }
                else if (arg.Content.StartsWith("more", StringComparison.OrdinalIgnoreCase))
                {
                    _overrideSend = true;
                }
                else if (arg.Content.StartsWith("purge") && arg.Channel.Id == _settings.Value<ulong>("post channel"))
                {
                    if (!arg.Content.Contains(' '))
                    {
                        await arg.Channel.SendMessageAsync($"{arg.Author.Mention} Usage: purge n");
                    }
                    else if (int.TryParse(arg.Content.Split(' ')[1], out int limit))
                    {
                        IEnumerable<IMessage> messages = await arg.Channel.GetMessagesAsync(limit + 1).FlattenAsync();

                        try
                        {
                            await ((ITextChannel)arg.Channel).DeleteMessagesAsync(messages);
                            #region Post eye bleach
                            var posts = GetPosts("eyebleach");
                            Random rdm = new Random();
                            await arg.Channel.SendMessageAsync(posts[rdm.Next(0, posts.Count)].Value<JObject>("data").Value<string>("url"));
                            #endregion
                            const int delay = 3000;
                            IUserMessage m = await arg.Channel.SendMessageAsync($"I have deleted {limit} messages for ya. :)");
                            await Task.Delay(delay);
                            await m.DeleteAsync();
                        }
                        catch (Exception e)
                        {
                            Type t = e.GetType();

                            if (t == typeof(ArgumentOutOfRangeException))
                            {
                                var m = await arg.Channel.SendMessageAsync($"Messages to be deleted has to be younger than 2 weeks old.");
                                await Task.Delay(3000);
                                await m.DeleteAsync();
                            }
                            else
                                await DiscordClient_log(new LogMessage(LogSeverity.Critical, "main", "Unhandled exception", e));
                        }

                        /*
                        _deletionQueue.Clear();
                        _purgeMessage = null;

                        await DiscordClient_log(new LogMessage(LogSeverity.Info, "main", $"Purging last {limit} messages."));
                        var messages = await arg.Channel.GetMessagesAsync(limit: limit).FlattenAsync();

                        _deletionQueue.AddRange(messages);
                        */
                    }
                }
                else if (arg.Content.Contains("wtf"))
                    await arg.Channel.SendMessageAsync($"<@{arg.Author.Id}> Yes!");
            }
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
            //if (!File.Exists("settings.json"))
            #region is this code really needed?
            /*using (StreamWriter sw = new StreamWriter("settings.json"))
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
            }*/
            #endregion
            #endregion

            //if (jObject != null)
            //    return jObject;

            if (File.Exists("settings.json"))
            {
                using (StreamReader sr = new StreamReader("settings.json"))
                    while (!sr.EndOfStream)
                        raw += sr.ReadLine();
                jObject = JsonConvert.DeserializeObject<JObject>(raw);
            }
            else
                jObject = new JObject();

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

            #region Subreddit // subreddit
            if (!jObject.ContainsKey("subreddit"))
            {
                Console.WriteLine("Please input subreddit:");
                Console.Write("String > ");
                jObject.Add("subreddit", Console.ReadLine());
            }
            #endregion
            #endregion

            return jObject;
        }

        private Task DiscordClient_log(Discord.LogMessage arg)
        {
            Console.WriteLine($"[{DateTime.Now}] [{arg.Severity}] {arg.Source}: {arg.Message}");

            if (arg.Exception != null)
                Console.WriteLine($"Exception: {arg.Exception.Message}\nStacktrace:\n{arg.Exception.StackTrace}");

            return Task.CompletedTask;
        }

        static JArray GetPosts(string subreddit = null)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "RedditPostToDiscord/rexthecapt AT gmail.com");
            client.BaseAddress = new Uri("https://www.reddit.com/r/");

            JObject redditPosts;

            string sub;
            if (subreddit != null)
                sub = subreddit;
            else
                sub = _settings.Value<string>("subreddit");

            #region Get posts
            using (HttpResponseMessage response = client.GetAsync($"{sub}/new/.json?limit=100").Result)
            {
                if (response.IsSuccessStatusCode)
                {
                    string responseString = response.Content.ReadAsStringAsync().Result;
                    redditPosts = JsonConvert.DeserializeObject<JObject>(responseString);
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new JArray();
                }
                else
                    throw new NotImplementedException();
            }
            #endregion

            return redditPosts.Value<JObject>("data").Value<JArray>("children");
        }
    }
}
