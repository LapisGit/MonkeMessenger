using System;
using BepInEx;
using BepInEx.Logging;
using MonkeMessenger.Tools;
using System.Collections.Generic;
using System.Threading.Tasks;
using GorillaInfoWatch.Models;
using MonkeMessenger.Models;
using GorillaInfoWatch.Models.Attributes;
using BepInEx.Configuration;
using UnityEngine.Networking;

[assembly: InfoWatchCompatible]

namespace MonkeMessenger;

[BepInDependency("dev.gorillainfowatch")]
[BepInPlugin(Constants.GUID, Constants.Name, Constants.Version)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static bool accountLoggedIn = false;
    public static string currentUsername = "";
    public static string currentPassword = "";
    public static string ServerUrl = "https://monkemessenger.lapis.codes";
    public static string AuthToken;
    public static string SelectedChatId = null;
    public static string CurrentChatType;
    public static string CurrentChatName;
    public static bool RefreshCurrentChatRequested;
    public static bool IsBanned = false;
    public static string BanReason = null;
    public static bool IsOutdated = false;
    public static string LatestVersion = null;

    private static ConfigEntry<string> SavedUsername;
    private static ConfigEntry<string> SavedPassword;

    internal static WebSocketClient WebSocketClientInstance;
    internal static Queue<ApiClient.MessageItem> IncomingMessages = new();
    internal static bool HasNewMessages = false;
    internal static Queue<Notification> PendingNotifications = new();

    private void Awake()
    {
        Logger = base.Logger;

        SavedUsername = Config.Bind("Login", "Username", string.Empty, "Saved username for auto login");
        SavedPassword = Config.Bind("Login", "Password", string.Empty, "Saved password for auto login");
        currentUsername = SavedUsername.Value ?? string.Empty;
        currentPassword = SavedPassword.Value ?? string.Empty;

        GorillaTagger.OnPlayerSpawned(async () =>
        {
            await CheckVersionAsync();
            
            if (IsOutdated)
            {
                Logger.LogWarning($"MonkeMessenger is outdated");
                return;
            }
            
            WebSocketClientInstance = new WebSocketClient();
            WebSocketClientInstance.Start();
            
            if (!IsBanned && !accountLoggedIn && !string.IsNullOrEmpty(currentUsername) && !string.IsNullOrEmpty(currentPassword))
            {
                await AutoLoginAsync();
            }
        });
    }

    private void Update()
    {
        lock (PendingNotifications)
        {
            while (PendingNotifications.Count > 0)
            {
                var notification = PendingNotifications.Dequeue();
                try
                {
                    GorillaInfoWatch.Notifications.SendNotification(notification);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"failed to send notification: {ex}");
                }
            }
        }

        if (RefreshCurrentChatRequested)
        {
            RefreshCurrentChatRequested = false;
            ScreenManager.Instance?.RequestRefreshCurrentChat();
        }
    }

    public static void SendNewNotification(Notification notification)
    {
        lock (PendingNotifications)
        {
            PendingNotifications.Enqueue(notification);
        }
    }

    private void OnDestroy()
    {
        WebSocketClientInstance?.Stop();
    }

    public static void SetAuthToken(string token)
    {
        if (IsOutdated)
        {
            Logger.LogWarning("Cannot authenticate - MonkeMessenger is outdated");
            return;
        }
        
        AuthToken = token;
        if (!string.IsNullOrEmpty(token))
        {
            WebSocketClientInstance?.Authenticate(token);
        }
    }

    public static void SaveUsername(string username)
    {
        currentUsername = username ?? string.Empty;
        if (SavedUsername != null)
        {
            SavedUsername.Value = currentUsername;
        }
    }

    public static void SavePassword(string password)
    {
        currentPassword = password ?? string.Empty;
        if (SavedPassword != null)
        {
            SavedPassword.Value = currentPassword;
        }
    }

    private static async Task AutoLoginAsync()
    {
        try
        {
            if (IsOutdated || IsBanned || accountLoggedIn || string.IsNullOrEmpty(currentUsername) || string.IsNullOrEmpty(currentPassword))
            {
                return;
            }

            var (success, token, error) = await ApiClient.Login(currentUsername, currentPassword);
            
            if (success)
            {
                AuthToken = token;
                accountLoggedIn = true;
                SetAuthToken(token);
                
                if (ScreenManager.Instance != null)
                {
                    await ScreenManager.Instance.RefreshChats(null);
                }
            }
            else
            {
                Logger.LogWarning($"Auto-login failed: {error}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Auto-login error: {ex.Message}");
        }
    }

    private static async Task CheckVersionAsync()
    {
        try
        {
            const string versionUrl = "https://raw.githubusercontent.com/LapisGit/MonkeMessenger/refs/heads/main/version.txt";
            
            using var www = UnityWebRequest.Get(versionUrl);
            var op = www.SendWebRequest();
            
            while (!op.isDone)
            {
                await Task.Yield();
            }

            if (www.result != UnityWebRequest.Result.Success)
            {
                Logger.LogError($"failed to check version: {www.error}");
                return;
            }

            LatestVersion = www.downloadHandler.text.Trim();
            string currentVersion = Constants.Version;
            
            IsOutdated = currentVersion != LatestVersion;
        }
        catch (Exception ex)
        {
            Logger.LogError($"version check failed: {ex.Message}");
        }
    }
}
