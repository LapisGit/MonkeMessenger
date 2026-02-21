using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GorillaInfoWatch.Models;
using Newtonsoft.Json.Linq;
using MonkeMessenger.Tools;

namespace MonkeMessenger.Models
{
    internal class WebSocketClient
    {
        private ClientWebSocket _ws;
        private Uri _uri;
        private CancellationTokenSource _cts;
        private Task _receiveTask;

        private int _reconnectAttempt = 0;
        private string _pendingAuthToken;
        private bool _banNotificationSent = false;

        public WebSocketClient() { }

        public void Start()
        {
            try
            {
                _uri = new Uri("ws://monkemessenger.lapis.codes");

                _cts = new CancellationTokenSource();
                _ws = new ClientWebSocket();

                _receiveTask = Task.Run(() => ConnectLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                Logging.Error("ws start failed: " + ex);
            }
        }

        public async Task Stop()
        {
            try
            {
                _cts?.Cancel();
                if (_ws != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting))
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                }
            }
            catch { }
        }

        public async void Authenticate(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            _pendingAuthToken = token;

            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open)
                {
                    await SendAsync(JObject.FromObject(new { token }).ToString());   
                }
            }
            catch (Exception ex)
            {
                Logging.Warning("auth ws failed: " + ex.Message);
            }
        }

        private async Task ConnectLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_ws == null || _ws.State != WebSocketState.Open)
                    {
                        _ws?.Dispose();
                        _ws = new ClientWebSocket();
                        await _ws.ConnectAsync(_uri, token);
                        _reconnectAttempt = 0;

                        if (!string.IsNullOrEmpty(_pendingAuthToken))
                        {
                            try
                            {
                                await SendAsync(JObject.FromObject(new { token = _pendingAuthToken }).ToString());
                            }
                            catch (Exception ex)
                            {
                                Logging.Warning("failed to send auth token ws: " + ex.Message);
                            }
                        }
                    }

                    await ReceiveLoop(_ws, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Logging.Warning("ws error: " + ex.Message);
                    _reconnectAttempt++;
                    var wait = Math.Min(30, 1 << Math.Min(6, _reconnectAttempt));
                    await Task.Delay(TimeSpan.FromSeconds(wait), token).ContinueWith(_ => { });
                }
            }
        }

        private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[4096];
            var seg = new ArraySegment<byte>(buffer);
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                try
                {
                    var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(seg, token);
                        if (result.Count > 0) ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage && !token.IsCancellationRequested);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "ok", CancellationToken.None);
                        break;
                    }

                    ms.Seek(0, System.IO.SeekOrigin.Begin);
                    var message = Encoding.UTF8.GetString(ms.ToArray());
                    HandleMessage(message);
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException wsex)
                {
                    Logging.Warning("ws receive error: " + wsex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    Logging.Error("ws received an unexpected error: " + ex);
                    break;
                }
            }
        }

        private void HandleMessage(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                var type = obj.Value<string>("type");
                
                if (type == "banned")
                {
                    var reason = obj.Value<string>("reason") ?? "No reason provided";
                    
                    if (!_banNotificationSent)
                    {
                        Plugin.IsBanned = true;
                        Plugin.BanReason = reason;
                        Logging.Error($"Account has been banned: {reason}");
                        
                        Plugin.accountLoggedIn = false;
                        Plugin.AuthToken = null;
                        
                        Plugin.SendNewNotification(new Notification($"You have been banned: {reason}", 10, Sounds.error3));
                        
                        _banNotificationSent = true;
                    }
                    
                    return;
                }
                
                if (type == "new_message")
                {
                    var msgToken = obj["message"];
                    if (msgToken != null)
                    {
                        try
                        {
                            var message = msgToken.ToObject<ApiClient.MessageItem>();
                            
                            lock (Plugin.IncomingMessages)
                            {
                                Plugin.IncomingMessages.Enqueue(message);

                                if (IsMessageForCurrentChat(message))
                                {
                                    Plugin.RefreshCurrentChatRequested = true;
                                }
                                
                                var senderName = message.from ?? "Someone";
                                var notificationText = $"New message from {senderName}";
                                
                                Plugin.SendNewNotification(new Notification(notificationText, 5, Sounds.notificationPositive));
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Warning("failed to parse msg: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Warning("ws handle msg failed: " + ex.Message);
            }
        }

        private static bool IsMessageForCurrentChat(ApiClient.MessageItem message)
        {
            if (string.IsNullOrEmpty(Plugin.SelectedChatId) || string.IsNullOrEmpty(Plugin.CurrentChatName))
            {
                return false;
            }

            if (string.Equals(Plugin.CurrentChatType, "group", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(message.to, Plugin.CurrentChatName, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(message.from, Plugin.CurrentChatName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.to, Plugin.CurrentChatName, StringComparison.OrdinalIgnoreCase);
        }

        public async Task SendAsync(string text)
        {
            try
            {
                if (_ws == null || _ws.State != WebSocketState.Open) return;
                var bytes = Encoding.UTF8.GetBytes(text);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logging.Warning("ws send failed: " + ex.Message);
            }
        }
    }
}
