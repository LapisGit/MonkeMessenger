using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using MonkeMessenger.Tools;

namespace MonkeMessenger.Models
{
    internal class ApiClient
    {
        private static string BaseUrl => Plugin.ServerUrl;

        public static async Task<(bool success, string error)> Register(string username, string password)
        {
            var request = new AuthRequest { username = username, password = password };
            var payload = JsonUtility.ToJson(request);
            var url = BaseUrl + "/register";
            
            using var www = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (www.result != UnityWebRequest.Result.Success)
            {
                var errorMsg = $"{www.error} (Code: {www.responseCode})";
                Logging.Error($"register failed: {errorMsg}");
                return (false, errorMsg);
            }

            try
            {
                var resp = JsonUtility.FromJson<RegisterResponse>(www.downloadHandler.text);
                if (resp.success)
                {
                    return (true, null);
                }
                return (false, "Registration failed");
            }
            catch (Exception e)
            {
                Logging.Error($"parse error when registering: {e.Message}");
                return (false, e.Message);
            }
        }

        public static async Task<(bool success, string token, string error)> Login(string username, string password)
        {
            var request = new AuthRequest { username = username, password = password };
            var payload = JsonUtility.ToJson(request);
            var url = BaseUrl + "/login";
            
            using var www = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (www.result != UnityWebRequest.Result.Success)
            {
                var errorMsg = $"{www.error} (Code: {www.responseCode})";
                Logging.Error($"login failed: {errorMsg}");
                
                if (!string.IsNullOrEmpty(www.downloadHandler.text))
                {
                    try
                    {
                        var jObj = JObject.Parse(www.downloadHandler.text);
                        var banned = jObj.Value<bool?>("banned");
                        if (banned == true)
                        {
                            var reason = jObj.Value<string>("reason") ?? "No reason provided";
                            Plugin.IsBanned = true;
                            Plugin.BanReason = reason;
                            Logging.Error($"account is banned: {reason}");
                            return (false, null, $"Account banned: {reason}");
                        }
                    }
                    catch (Exception)
                    {
                        Logging.Error("ban response parse error, but login failed: " + errorMsg);
                    }
                }
                
                return (false, null, errorMsg);
            }

            try
            {
                var resp = JsonUtility.FromJson<LoginResponse>(www.downloadHandler.text);
                return (true, resp.token, null);
            }
            catch (Exception e)
            {
                Logging.Error($"parse error when logging in: {e.Message}");
                return (false, null, e.Message);
            }
        }

        public static async Task<(bool success, List<ChatListItem> chats, string error)> ListChats(string token)
        {
            var url = BaseUrl + "/chats";
            
            using var www = UnityWebRequest.Get(url);
            www.SetRequestHeader("Authorization", "Bearer " + token);
            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            

            if (www.result != UnityWebRequest.Result.Success)
            {
                var errorMsg = $"{www.error} (Code: {www.responseCode})";
                Logging.Error($"listing chats failed: {errorMsg}");
                
                if (www.responseCode == 404)
                {
                    return (true, new List<ChatListItem>(), null);
                }
                
                return (false, null, errorMsg);
            }

            try
            {
                var wrapper = JsonUtility.FromJson<ChatListWrapper>(www.downloadHandler.text);
                
                if (wrapper == null)
                {
                    return (true, new List<ChatListItem>(), null);
                }
                
                if (wrapper.chats == null)
                {
                    try
                    {
                        var jObj = JObject.Parse(www.downloadHandler.text);
                        var chatsArray = jObj["chats"];
                        var chatsList = new List<ChatListItem>();
                        
                        if (chatsArray != null)
                        {
                            foreach (var chatToken in chatsArray)
                            {
                                var chat = new ChatListItem
                                {
                                    id = chatToken.Value<string>("id"),
                                    name = chatToken.Value<string>("name"),
                                    type = chatToken.Value<string>("type")
                                };
                                chatsList.Add(chat);
                            }
                        }
                        
                        return (true, chatsList, null);
                    }
                    catch (Exception ex2)
                    {
                        Logging.Error($"parse fail: {ex2.Message}");
                        return (true, new List<ChatListItem>(), null);
                    }
                }
                
                return (true, wrapper.chats, null);
            }
            catch (Exception e)
            {
                Logging.Error($"getting chat list fail: {e.Message}");
                return (false, null, e.Message);
            }
        }

        public static async Task<(bool success, List<MessageItem> messages, string error)> GetMessages(string token, string chatId)
        {
            var url = BaseUrl + $"/chats/{UnityWebRequest.EscapeURL(chatId)}/messages";
            
            using var www = UnityWebRequest.Get(url);
            www.SetRequestHeader("Authorization", "Bearer " + token);
            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (www.result != UnityWebRequest.Result.Success)
            {
                var errorMsg = $"{www.error} (Code: {www.responseCode})";
                Logging.Error($"getting msgs failed: {errorMsg}");
                
                if (www.responseCode == 404)
                {
                    return (true, new List<MessageItem>(), null);
                }
                
                return (false, null, errorMsg);
            }

            try
            {
                var wrapper = JsonUtility.FromJson<MessagesWrapper>(www.downloadHandler.text);
                
                if (wrapper == null)
                {
                    return (true, new List<MessageItem>(), null);
                }
                
                if (wrapper.messages == null)
                {
                    try
                    {
                        var jObj = JObject.Parse(www.downloadHandler.text);
                        var messagesArray = jObj["messages"];
                        var messagesList = new List<MessageItem>();
                        
                        if (messagesArray != null)
                        {
                            foreach (var msgToken in messagesArray)
                            {
                                var msg = new MessageItem
                                {
                                    id = msgToken.Value<string>("id"),
                                    from = msgToken.Value<string>("from"),
                                    to = msgToken.Value<string>("to"),
                                    content = msgToken.Value<string>("content"),
                                    timestamp = msgToken.Value<long>("timestamp")
                                };
                                messagesList.Add(msg);
                            }
                        }
                        
                        return (true, messagesList, null);
                    }
                    catch (Exception ex2)
                    {
                        Logging.Error($"parse fail: {ex2.Message}");
                        return (true, new List<MessageItem>(), null);
                    }
                }
                
                return (true, wrapper.messages, null);
            }
            catch (Exception e)
            {
                Logging.Error($"could not get msgs: {e.Message}");
                return (false, null, e.Message);
            }
        }

        public static async Task<(bool success, MessageItem message, string error)> SendMessage(string token, string to, string content)
        {
            var request = new SendMessageRequest { to = to, content = content };
            var payload = JsonUtility.ToJson(request);
            var url = BaseUrl + "/messages";
            using var www = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + token);

            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (www.result != UnityWebRequest.Result.Success)
            {
                return (false, null, www.error);
            }

            try
            {
                var wrapper = JsonUtility.FromJson<MessageWrapper>(www.downloadHandler.text);
                return (true, wrapper.message, null);
            }
            catch (Exception e)
            {
                return (false, null, e.Message);
            }
        }

        public static async Task<(bool success, List<UserItem> users, string error)> SearchUsers(string token, string query)
        {
            var url = BaseUrl + $"/users/search?q={UnityWebRequest.EscapeURL(query)}";
            using var www = UnityWebRequest.Get(url);
            www.SetRequestHeader("Authorization", "Bearer " + token);
            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (www.result != UnityWebRequest.Result.Success)
            {
                var errorMsg = $"{www.error} (Code: {www.responseCode})";
                Logging.Error($"searching for a user failed: {errorMsg}");
                return (false, null, errorMsg);
            }

            try
            {
                var jObj = JObject.Parse(www.downloadHandler.text);
                var usersArray = jObj["users"];
                var usersList = new List<UserItem>();
                
                if (usersArray != null)
                {
                    foreach (var userToken in usersArray)
                    {
                        var user = new UserItem
                        {
                            id = userToken.Value<string>("id"),
                            username = userToken.Value<string>("username")
                        };
                        usersList.Add(user);
                    }
                }
                
                return (true, usersList, null);
            }
            catch (Exception e)
            {
                Logging.Error($"searching for a user failed: {e.Message}");
                return (false, null, e.Message);
            }
        }

        public static async Task<(bool success, string groupId, string error)> CreateGroup(string token, string groupName, List<string> memberIds)
        {
            var request = new CreateGroupRequest { name = groupName, members = memberIds };
            var payload = JsonUtility.ToJson(request);
            var url = BaseUrl + "/groups";
            using var www = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + token);

            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (www.result != UnityWebRequest.Result.Success)
            {
                var errorMsg = $"{www.error} (Code: {www.responseCode})";
                Logging.Error($"creating a group failed: {errorMsg}");
                return (false, null, errorMsg);
            }

            try
            {
                var jObj = JObject.Parse(www.downloadHandler.text);
                var groupToken = jObj["group"];
                if (groupToken != null)
                {
                    var groupId = groupToken.Value<string>("id");
                    return (true, groupId, null);
                }
                return (false, null, "no group id in response");
            }
            catch (Exception e)
            {
                Logging.Error($"creategroup fail: {e.Message}");
                return (false, null, e.Message);
            }
        }

        public static async Task<(bool success, string error)> AddMemberToGroup(string token, string groupId, string userId)
        {
            var request = new AddMemberRequest { userId = userId };
            var payload = JsonUtility.ToJson(request);
            var url = BaseUrl + $"/groups/{UnityWebRequest.EscapeURL(groupId)}/members";
            using var www = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + token);

            var op = www.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (www.result != UnityWebRequest.Result.Success)
            {
                var errorMsg = $"{www.error} (Code: {www.responseCode})";
                Logging.Error($"adding person to group failed: {errorMsg}");
                return (false, errorMsg);
            }

            return (true, null);
        }

        [Serializable]
        private class AuthRequest 
        { 
            public string username; 
            public string password; 
        }

        [Serializable]
        private class SendMessageRequest 
        { 
            public string to; 
            public string content; 
        }

        [Serializable]
        private class CreateGroupRequest
        {
            public string name;
            public List<string> members;
        }

        [Serializable]
        private class AddMemberRequest
        {
            public string userId;
        }

        [Serializable]
        private class LoginResponse { public string token; }

        [Serializable]
        private class RegisterResponse { public bool success; }

        [Serializable]
        public class ChatListItem 
        { 
            public string id; 
            public string name; 
            public string type; 
        }

        [Serializable]
        public class UserItem
        {
            public string id;
            public string username;
        }

        [Serializable]
        private class ChatListWrapper { public List<ChatListItem> chats; }

        [Serializable]
        public class MessageItem { public string id; public string from; public string to; public string content; public long timestamp; }
        [Serializable]
        private class MessagesWrapper { public List<MessageItem> messages; }

        [Serializable]
        private class MessageWrapper { public MessageItem message; }
    }
}
