using System;
using BepInEx;
using BepInEx.Logging;
using MonkeMessenger.Tools;
using System.Collections.Generic;
using GorillaInfoWatch.Models;
using MonkeMessenger.Models;
using GorillaInfoWatch.Models.Attributes;
using BepInEx.Configuration;

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

        WebSocketClientInstance = new WebSocketClient();
        WebSocketClientInstance.Start();
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
}
