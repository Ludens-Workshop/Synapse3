﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
using EmbedIO;
using JetBrains.Annotations;
using MEC;
using Microsoft.Extensions.Caching.Memory;
using Swan;
using Swan.Formatters;
using Synapse.Api;
using Synapse.Network.Models;

namespace Synapse.Network
{
    public class SynapseNetworkClient
    {
        private MemoryCache _requestMemCache;
        public string CipherKey;
        private HttpClient Client;
        public string ClientIdentifier;
        public string ClientName = "SynapseServerClient";

        public bool IsStarted;
        public string LastSyncEntriesHash = "__";
        public int MigrationPriority;
        public RSA PrivateKey;
        public string PublicKey;
        public string Secret;
        public string ServerCipherKey;
        public RSA ServerPublicKey;
        public string SessionToken;
        public List<string> SyncedClientList = new();

        public HashSet<KeyValueObjectWrapper>
            SyncEntries = new(); //Own ClientSync (Only client has write permissions)

        public ClientSyncEntryThread SyncVarThread;

        public string Url;

        public SynapseNetworkClient()
        {
            GetClient = this;
        }

        public static SynapseNetworkClient GetClient { get; private set; }

        public void Init()
        {
            Client = new HttpClient
            {
                BaseAddress = new Uri(Url)
            };
            PrivateKey = RSA.Create();
            CipherKey = TokenFactory.Instance.GenerateShortToken();
            PublicKey = PrivateKey.ToXmlString(false);
            _requestMemCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 64
            });
        }

        /// <summary>
        ///     Gets the InstanceDetails of one specific other Instance including its ClientVars
        /// </summary>
        /// <param name="uid">The id of the other Distance</param>
        public async Task<InstanceDetails> Details(string uid)
        {
            return (await Get<InstanceDetailsTransmission>($"/synapse/client/{uid}/details")).Details;
        }

        /// <summary>
        ///     Gets the InstanceDetails of all server instances including their ClientVars
        /// </summary>
        public async Task<List<InstanceDetails>> Details()
        {
            return (await Get<InstanceDetailsListTransmission>("/synapse/client/all/details")).Details.ToList();
        }

        [ItemCanBeNull]
        public async Task<NetworkPlayer> GetPlayer(string uid)
        {
            var message = await SendMessageAndAwaitResponse(InstanceMessage.CreateBroadcast("GetPlayer", uid));
            return message?.Value<NetworkPlayer>();
        }


        public async Task<List<NetworkPlayer>> GetAllPlayers()
        {
            var players = Server.Get.Players.Select(NetworkPlayer.FromLocalPlayer).ToList();
            var tasks = new List<Task<InstanceMessage>>();
            foreach (var client in SyncedClientList)
            {
                if (client == ClientIdentifier) continue;
                tasks.Add(SendMessageAndAwaitResponse(InstanceMessage.CreateSend("GetPlayers", "", client)));
            }

            if (!tasks.IsEmpty())
            {
                await Task.WhenAll(tasks);
                Logger.Get.Warn("All tasks completed");
                players.AddRange(tasks.Select(x => x.Result).Select(x =>
                {
                    if (x == null) return new List<NetworkPlayer>();
                    return x.Value<List<NetworkPlayer>>();
                }).Aggregate((x, y) =>
                {
                    var list = x.ToList();
                    list.AddRange(y);
                    return list;
                }));
            }

            return players;
        }

        internal async Task<PingResponse> PollServer()
        {
            try
            {
                var result = await Get<PingResponse>("/synapse/ping", exceptionHandler: x => { });
                if (result != null)
                {
                    LastSyncEntriesHash = result.LatestVarHash;
                    try
                    {
                        foreach (var message in result.Messages)
                        {
                            foreach (var node in Server.Get.NetworkManager.NetworkNodes)
                                node.ReceiveInstanceMessage(message);
                            var key = $"{message.ReferenceId}{message.Subject}";
                            var existsReq = _requestMemCache.TryGetValue(key, out var obj);
                            if (existsReq) ((Action<InstanceMessage>) obj).Invoke(message);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Get.Error(e);
                    }

                    SyncedClientList = result.ConnectedClients;

                    return result;
                }

                return (PingResponse) await Task.FromResult<object>(null);
            }
            catch
            {
                return (PingResponse) await Task.FromResult<object>(null);
            }
        }

        private async Task OnConnected()
        {
            Server.Get.Logger.Info("Continuing OnConnected");
            var networkNodes = Server.Get.NetworkManager.NetworkNodes;
            networkNodes.ForEach(x => x.StartClient(this));
            var authority = SynapseNetworkServer.GetServer.Status == WebServerState.Stopped
                ? InstanceAuthority.Client
                : InstanceAuthority.Master;
            networkNodes.ForEach(x => x.Reconfigure(authority));
            var configuration = Server.Get.Configs.synapseConfiguration;
            SyncVarThread = new ClientSyncEntryThread(configuration);
            SyncVarThread.Start();

            //prepare one-shot sync entries
            SyncEntries.Set("plugins",
                SynapseController.PluginLoader.Plugins.Select(SerializablePlugin.FromAttribute).ToList());
            SyncEntries.Set("startupTimestamp", new NetHealthData
            {
                ClientId = ClientIdentifier,
                ClientName = ClientName,
                StartupTimestamp = Server.Get.NetworkManager.Startup.ToUnixEpochDate()
            });
            
            Timing.RunCoroutine(UpdateNetworkVars());

            Server.Get.Logger.Info("Finished OnConnected");
        }

        public IEnumerator<float> UpdateNetworkVars()
        {
            while (true)
            {
                SyncEntries.Set("round", NetRound.Get());
                yield return Timing.WaitForSeconds(5);
            }
        }

        public async Task Connect()
        {
            IsStarted = true;
            try
            {
                Server.Get.Logger.Info("Connecting to Synapse-Network...");
                await SyncMaster();
                await KeyExchange();
                await AuthMaster();
                Server.Get.Logger.Info(
                    $"Connected to Master-Server with MigrationPriority {MigrationPriority} and ClientUID{ClientIdentifier}");
                Server.Get.Logger.Send($"Synapse-Network Session-Token is {SessionToken}", ConsoleColor.Magenta);
                OnConnected();
            }
            catch (Exception e)
            {
                IsStarted = false;
                Server.Get.Logger.Error(e);
            }
        }

        public void Disconnect()
        {
            IsStarted = false;
            SyncVarThread.Stop();
            Client.Dispose();
            _requestMemCache.Dispose();
        }

        #region InstanceMessaging

        public void AddRequestToCache(string reference, string subject, Action<InstanceMessage> action)
        {
            var key = $"{reference}{subject}";
            var options = new MemoryCacheEntryOptions
            {
                Size = 1,
                Priority = CacheItemPriority.Normal,
                SlidingExpiration = TimeSpan.FromMinutes(1)
            };
            options.RegisterPostEvictionCallback(EvictMessageRequest);
            _requestMemCache.Set(key, new Action<InstanceMessage>(x =>
            {
                if (x != null) _requestMemCache.Remove(key); //Assume it's expired
                action(x);
            }), options);
        }

        private static void EvictMessageRequest(object key, object value, EvictionReason reason, object state)
        {
            ((Action<InstanceMessage>) value).Invoke(null);
        }


        public async Task<InstanceMessageTransmission> SendMessage(InstanceMessage message)
        {
            return await Post<InstanceMessageTransmission, InstanceMessage>("/synapse/post", message);
        }

        [ItemCanBeNull]
        public async Task<InstanceMessage> SendMessageAndAwaitResponse(InstanceMessage message, string responseSubject)
        {
            InstanceMessage result = null;
            var completer = new TaskCompletionSource<InstanceMessage>();
            AddRequestToCache(message.ReferenceId, responseSubject, x =>
            {
                if (x == null && result != null) return;
                result = x;
                completer.TrySetResult(result);
            });
            await Post<InstanceMessageTransmission, InstanceMessage>("/synapse/post", message);
            return await completer.Task;
        }

        [ItemCanBeNull]
        public async Task<InstanceMessage> SendMessageAndAwaitResponse(InstanceMessage message)
        {
            return await SendMessageAndAwaitResponse(message, message.Subject + "Res");
        }

        #endregion

        #region NetworkVars

        [ItemCanBeNull]
        public async Task<TR> RequestNetworkVar<TR>(string key)
        {
            var hasException = false;
            var entry = await Server.Get.NetworkManager.Client.Get<NetworkSyncEntry>($"/networksync?key={key}",
                exceptionHandler: x =>
                {
                    hasException = true;
#if DEBUG
                    Server.Get.Logger.Info(x);
#endif
                });
            if (entry != null && !hasException) return entry.Value<TR>();
            return (TR) await Task.FromResult<object>(null);
        }

        public async Task<bool> SetNetworkVar<TR>(string key, TR value)
        {
            var result =
                await Server.Get.NetworkManager.Client.Post<StatusedResponse, NetworkSyncEntry>(
                    $"/networksync?key={key}", SerializableObjectWrapper.NetFromPair(key, value));
            return result?.Successful ?? false;
        }

        public async Task<List<NetworkSyncEntry>> RequestAllNetworkVars()
        {
            var entry = await Server.Get.NetworkManager.Client.Get<List<NetworkSyncEntry>>("/networksync");
            return entry ?? new List<NetworkSyncEntry>();
        }

        #endregion

        #region HttpMethods

        [ItemCanBeNull]
        public async Task<TR> Get<TR>(string path, bool authenticated = true, Action<Exception> exceptionHandler = null,
            Dictionary<string, string> headers = null)
        {
            var exc = exceptionHandler ?? (x => Server.Get.Logger.Error(x));
            try
            {
                var content = await GetString(path, authenticated, exc, headers);
                try
                {
                    var status = Json.Deserialize<StatusedResponse>(content);
                    if (!status.Successful)
                    {
                        exc(new Exception($"Request failed: {status.Message}"));
                        return (TR) await Task.FromResult<object>(null);
                    }
                }
                catch (Exception e)
                {
                    Logger.Get.Error("Json parse error?");
                    exc(e);
                }

                return Json.Deserialize<TR>(content);
            }
            catch (Exception e)
            {
                exc(e);
                return (TR) await Task.FromResult<object>(null);
            }
        }

        [ItemCanBeNull]
        public async Task<string> GetString(string path, bool authenticated = true,
            Action<Exception> exceptionHandler = null, Dictionary<string, string> headers = null)
        {
            var exc = exceptionHandler ?? (x => Server.Get.Logger.Error(x));
            try
            {
                var message = new HttpRequestMessage
                {
                    RequestUri = new Uri(Url + path),
                    Method = HttpMethod.Get
                };
                if (authenticated)
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionToken);
                if (headers != null)
                    foreach (var pair in headers)
                        message.Headers.Add(pair.Key, pair.Value);
                var result = await Client.SendAsync(message);
                var content = await result.Content.ReadAsStringAsync();
#if DEBUG
                Server.Get.Logger.Info($">>> {message.RequestUri}\n{content}");
#endif
                return content;
            }
            catch (Exception e)
            {
                exc(e);
                return (string) await Task.FromResult<object>(null);
            }
        }

        [ItemCanBeNull]
        public async Task<string> PostString<TB>(string path, TB body, bool authenticated = true,
            bool encodeBody = true, Action<Exception> exceptionHandler = null,
            Dictionary<string, string> headers = null)
        {
            // ReSharper disable once HeapView.PossibleBoxingAllocation
            var exc = exceptionHandler ?? (x => Server.Get.Logger.Error(x));
            try
            {
                var data = encodeBody ? Json.Serialize(body) : body.ToString();
                var message = new HttpRequestMessage
                {
                    RequestUri = new Uri(Url + path),
                    Method = HttpMethod.Post,
                    Content = new StringContent(data)
                };

#if DEBUG
                Server.Get.Logger.Info($"<<< {data}");
#endif

                if (authenticated)
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionToken);
                if (headers != null)
                    foreach (var pair in headers)
                        message.Headers.Add(pair.Key, pair.Value);
                var result = await Client.SendAsync(message);
                var content = await result.Content.ReadAsStringAsync();
#if DEBUG
                Server.Get.Logger.Info($">>> {content}");
#endif
                return content;
            }
            catch (Exception e)
            {
                exc(e);
                return (string) await Task.FromResult<object>(null);
            }
        }

        [ItemCanBeNull]
        public async Task<TR> Post<TR, TB>(string path, TB body, bool authenticated = true, bool encodeBody = true,
            Action<Exception> exceptionHandler = null, Dictionary<string, string> headers = null)
        {
            // ReSharper disable once HeapView.PossibleBoxingAllocation
            var exc = exceptionHandler ?? (x => Server.Get.Logger.Error(x));
            try
            {
                var content = await PostString(path, body, authenticated, encodeBody, exc, headers);
                try
                {
                    var status = Json.Deserialize<StatusedResponse>(content);
                    if (!status.Successful)
                    {
                        exc(new Exception($"Request failed: {status.Message}"));
                        return (TR) await Task.FromResult<object>(null);
                    }
                }
                catch
                {
                }

                return Json.Deserialize<TR>(content);
            }
            catch (Exception e)
            {
                exc(e);
                return (TR) await Task.FromResult<object>(null);
            }
        }

        #endregion

        #region Authentication

        private async Task KeyExchange()
        {
            var obj = new NetworkAuthKeyExchange
            {
                Key = CipherKey
            };
            obj.EncodeWithPublic(ServerPublicKey);
            var reqContent = Json.Serialize(obj);
            var req = await Client.PostAsync($"/synapse/client/{ClientIdentifier}/key", new StringContent(reqContent));
            var content = await req.Content.ReadAsStringAsync();
            var serverKey = Json.Deserialize<NetworkAuthKeyExchange>(content);
            serverKey.DecodeWithPrivate(PrivateKey);
            ServerCipherKey = serverKey.Key;
        }

        private async Task SyncMaster()
        {
            var obj = new NetworkAuthSyn
            {
                ClientName = ClientName,
                PublicKey = PublicKey,
                Port = Server.Get.Port
            };
            var reqContent = Json.Serialize(obj);
            var req = await Client.PostAsync("/synapse/handshake", new StringContent(reqContent));
            var content = await req.Content.ReadAsStringAsync();
#if DEBUG
            Server.Get.Logger.Info($">>> {content}");
#endif
            var ack = Json.Deserialize<NetworkAuthAck>(content);
            ServerPublicKey = RSA.Create();
            ServerPublicKey.FromXmlString(ack.PublicKey);
            MigrationPriority = ack.MigrationPriority;
            ClientIdentifier = ack.ClientIdentifier;
        }

        private async Task AuthMaster()
        {
            var payload = Json.Serialize(new NetworkAuthReqAuth
            {
                Secret = Secret,
                ClientIdentifier = ClientIdentifier
            });
            payload = AESUtils.Encrypt(payload, ServerCipherKey);
            var req = await Client.PostAsync($"/synapse/client/{ClientIdentifier}/auth", new StringContent(payload));
            var content = await req.Content.ReadAsStringAsync();
            content = content.Replace("\"", ""); //For any reason, this payload always is in " ".
            content = AESUtils.Decrypt(content, CipherKey);
            var res = Json.Deserialize<NetworkAuthResAuth>(content);
            SessionToken = res.SessionToken;
        }

        #endregion
    }
}