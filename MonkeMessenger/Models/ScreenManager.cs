using GorillaInfoWatch.Models;
using GorillaInfoWatch.Models.Attributes;
using GorillaInfoWatch.Models.Widgets;
using GorillaInfoWatch.Models.UserInput;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MonkeMessenger.Tools;
using UnityEngine;

namespace MonkeMessenger.Models;


[ShowOnHomeScreen]
internal class ScreenManager : InfoScreen
{
    public override string Title => "MonkeMessenger";
    public override string Description => "A messenger for Gorilla Tag";

    private EventHandler<UserInputArgs> _usernameHandler;
    private EventHandler<UserInputArgs> _passwordHandler;
    private EventHandler<UserInputArgs> _sendHandler;
    private EventHandler<UserInputArgs> _userSearchHandler;
    private EventHandler<UserInputArgs> _groupNameHandler;
    private EventHandler<UserInputArgs> _addMemberHandler;

    private List<ApiClient.ChatListItem> _cachedChats = new();
    private List<ApiClient.MessageItem> _cachedMessages = new();
    private List<ApiClient.UserItem> _searchedUsers = new();
    private string _currentScreenMode = "chats";
    private string _currentGroupChatId = null;
    private string _currentChatType = null;
    private string _currentChatName = null;
    private bool _showingAddMemberSearch = false;
    private bool _autoLoginAttempted = false;
    
    private int _logoutPressCount = 0;
    

    internal static ScreenManager Instance;

    public ScreenManager()
    {
        Instance = this;
        _usernameHandler += OnUsernameEntered;
        _passwordHandler += OnPasswordEntered;
        _sendHandler += OnSendEntered;
        _userSearchHandler += OnUserSearchEntered;
        _groupNameHandler += OnGroupNameEntered;
        _addMemberHandler += OnAddMemberEntered;
    }

    public override InfoContent GetContent()
    {
        LineBuilder lines = new();

        if (Plugin.IsOutdated)
        {
            lines.Add("MonkeMessenger is Outdated!");
            lines.Add("");
            lines.Add($"Current Version: {Constants.Version}", LineOptions.Wrapping);
            lines.Add($"Latest Version: {Plugin.LatestVersion ?? "Unknown"}", LineOptions.Wrapping);
            lines.Add("");
            lines.Add("Please update to continue using MonkeMessenger.", LineOptions.Wrapping);
            lines.Add("");
            lines.Add("Download latest version", new Widget_PushButton(() =>
            {
                Application.OpenURL("https://github.com/LapisGit/MonkeMessenger/releases/latest");
            }));
            return lines;
        }

        if (Plugin.IsBanned)
        {
            lines.Add("Your account has been banned.");
            lines.Add("");
            lines.Add("Reason:", LineOptions.Wrapping);
            lines.Add(Plugin.BanReason ?? "No reason provided", LineOptions.Wrapping);
            lines.Add("");
            lines.Add("This account cannot access MonkeMessenger anymore.");
            return lines;
        }

        bool requestRefresh = false;
        lock (Plugin.IncomingMessages)
        {
            while (Plugin.IncomingMessages.Count > 0)
            {
                var im = Plugin.IncomingMessages.Dequeue();
                if (IsMessageForCurrentChat(im))
                {
                    requestRefresh = true;
                    continue;
                }
                if (!_cachedMessages.Exists(m => m.id == im.id))
                {
                    _cachedMessages.Insert(0, im);
                }
            }
        }
        if (requestRefresh)
        {
            Plugin.RefreshCurrentChatRequested = true;
        }
        if (!Plugin.accountLoggedIn || string.IsNullOrEmpty(Plugin.AuthToken))
        {
            if (!_autoLoginAttempted && !Plugin.IsOutdated && !Plugin.IsBanned && !string.IsNullOrEmpty(Plugin.currentUsername) && !string.IsNullOrEmpty(Plugin.currentPassword))
            {
                _autoLoginAttempted = true;
                RunAsync(DoLogin());
            }

            lines.Add("Not logged in");
            lines.Add("Username", new Widget_PromptButton(Plugin.currentUsername ?? string.Empty, 64, UserInputBoard.Advanced, _usernameHandler)
            {
                Alignment = WidgetAlignment.Left
            });
            lines.Add("Password", new Widget_PromptButton(string.Empty, 64, UserInputBoard.Advanced, _passwordHandler)
            {
                Alignment = WidgetAlignment.Left
            });
            lines.Add("Login", new Widget_PushButton(() => { RunAsync(DoLogin()); }));
            lines.Add("Register", new Widget_PushButton(() => { RunAsync(DoRegister()); }));
            return lines;
        }

        lines.Add("Logged in as " + Plugin.currentUsername);
        
        if (!string.IsNullOrEmpty(Plugin.SelectedChatId))
        {
            if (_showingAddMemberSearch)
            {
                lines.Add("Done", new Widget_PushButton(() => {
                    _showingAddMemberSearch = false;
                    _searchedUsers.Clear();
                    LoadedScreen.SetContent();
                })
                {
                    Alignment = WidgetAlignment.Left
                });

                lines.Add("Search Users", new Widget_PromptButton(string.Empty, 64, UserInputBoard.Advanced, _addMemberHandler));

                if (_searchedUsers.Count > 0)
                {
                    foreach (var user in _searchedUsers)
                    {
                        var userId = user.id;
                        lines.Add(user.username, new Widget_PushButton(() => {
                            RunAsync(AddUserToGroup(userId));
                        }));
                    }
                }
            }
            else
            {
                lines.Add("Back to Chats", new Widget_PushButton(() =>
                {
                    Plugin.SelectedChatId = null;
                    _currentChatType = null;
                    _currentChatName = null;
                    Plugin.CurrentChatType = null;
                    Plugin.CurrentChatName = null;
                    _cachedMessages.Clear();
                    _currentScreenMode = "chats";
                    LoadedScreen.SetContent();
                })
                {
                    Alignment = WidgetAlignment.Left
                });
                if (_currentChatType == "group")
                {
                    lines.Add("Add Members", new Widget_PushButton(() => {
                        _showingAddMemberSearch = true;
                        _currentGroupChatId = Plugin.SelectedChatId;
                        _searchedUsers.Clear();
                        LoadedScreen.SetContent();
                    })
                    {
                        Alignment = WidgetAlignment.Left
                    });
                }

                if (_cachedMessages.Count == 0)
                {
                    RunAsync(RefreshMessages(lines, Plugin.SelectedChatId));
                }

                
                lines.Add("Refresh Messages", new Widget_PushButton(() => { 
                    RunAsync(RefreshMessages(lines, Plugin.SelectedChatId));
                })
                {
                    Alignment = WidgetAlignment.Left
                });

                lines.Add("Send Message", new Widget_PromptButton(string.Empty, 256, UserInputBoard.Advanced, _sendHandler)
                {
                    Alignment = WidgetAlignment.Left
                });

                
                if (_cachedMessages == null || _cachedMessages.Count == 0)
                {
                    lines.Add("No messages yet");
                }
                else
                {
                    foreach (var m in _cachedMessages)
                    {
                        var display = $"{m.from}: {m.content}";
                        lines.Add(display, LineOptions.Wrapping);
                    }
                }
            }
        }
        else if (_currentScreenMode == "search_users")
        {
            lines.Add("Back to Chats", new Widget_PushButton(() => { 
                _currentScreenMode = "chats";
                _searchedUsers.Clear();
                LoadedScreen.SetContent();
            })
            {
                Alignment = WidgetAlignment.Left
            });

            lines.Add("Search Users", new Widget_PromptButton(string.Empty, 64, UserInputBoard.Advanced, _userSearchHandler)
            {
                Alignment = WidgetAlignment.Left
            });

            if (_searchedUsers.Count == 0)
            {
                lines.Add("No users found - search above");
                lines.Add("Create Group Chat", new Widget_PushButton(() => {
                    _currentScreenMode = "create_group";
                    LoadedScreen.SetContent();
                }));
            }
            else
            {
                foreach (var user in _searchedUsers)
                {
                    var userId = user.id;
                    lines.Add(user.username, new Widget_PushButton(() => {
                        Plugin.SelectedChatId = userId;
                        _currentChatType = "direct";
                        _currentChatName = user.username;
                        Plugin.CurrentChatType = _currentChatType;
                        Plugin.CurrentChatName = _currentChatName;
                        _cachedMessages.Clear();
                        _currentScreenMode = "chats";
                        LoadedScreen.SetContent();
                    }));
                }
            }
        }
        else if (_currentScreenMode == "create_group")
        {
            lines.Add("Back to Search", new Widget_PushButton(() => { 
                _currentScreenMode = "search_users";
                LoadedScreen.SetContent();
            })
            {
                Alignment = WidgetAlignment.Left
            });

            lines.Add("Group Name", new Widget_PromptButton(string.Empty, 64, UserInputBoard.Advanced, _groupNameHandler)
            {
                Alignment = WidgetAlignment.Left
            });
            lines.Add("(You'll be added automatically)");
        }
        else
        {
            string logoutText = _logoutPressCount switch
            {
                0 => "Log Out",
                1 => "Are you sure?",
                _ => "ARE YOU 100% CONFIDENT YOU WANT TO LOG OUT?"
            };
            
            lines.Add(logoutText, new Widget_PushButton(() =>
            {
                _logoutPressCount++;
                LoadedScreen.SetContent(); 
                
                if (_logoutPressCount >= 3)
                {
                    Plugin.accountLoggedIn = false;
                    Plugin.AuthToken = null;
                    Plugin.SetAuthToken(null);
                    Plugin.SaveUsername(string.Empty);
                    Plugin.SavePassword(string.Empty);
                    _cachedChats.Clear();
                    _cachedMessages.Clear();
                    _searchedUsers.Clear();
                    _currentScreenMode = "chats";
                    Plugin.SelectedChatId = null;
                    _currentChatType = null;
                    _currentChatName = null;
                    Plugin.CurrentChatType = null;
                    Plugin.CurrentChatName = null;
                    _autoLoginAttempted = false;
                    _logoutPressCount = 0;
                    LoadedScreen.SetContent();   
                }
            })
            {
                Alignment = WidgetAlignment.Left
            });


            
            lines.Add("Add Chat", new Widget_PushButton(() => { 
                _currentScreenMode = "search_users";
                _searchedUsers.Clear();
                LoadedScreen.SetContent();
            })
            {
                Alignment = WidgetAlignment.Left
            });
            
            lines.Add("Refresh Chats", new Widget_PushButton(() => { 
                RunAsync(RefreshChats(lines));
            })
            {
                Alignment = WidgetAlignment.Left
            });
            
            if (_cachedChats == null || _cachedChats.Count == 0)
            {
                lines.Add("No chats loaded - tap Refresh or create a new chat.");
            }
            else
            {
                foreach (var c in _cachedChats)
                {
                    var id = c.id;
                    var chatType = c.type;
                    var chatName = c.name ?? "Unnamed Chat";
                    lines.Add(chatName, new Widget_PushButton(() => { 
                        Plugin.SelectedChatId = id;
                        _currentChatType = chatType;
                        _currentChatName = chatName;
                        Plugin.CurrentChatType = _currentChatType;
                        Plugin.CurrentChatName = _currentChatName;
                        _cachedMessages.Clear();
                        _currentScreenMode = "chats";
                        LoadedScreen.SetContent();
                    }));
                }
            }
        }

        return lines;
    }

    internal void RequestRefreshCurrentChat()
    {
        if (!string.IsNullOrEmpty(Plugin.SelectedChatId) && !_showingAddMemberSearch)
        {
            RunAsync(RefreshMessages(null, Plugin.SelectedChatId));
        }
    }
    

    private bool IsMessageForCurrentChat(ApiClient.MessageItem message)
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

    private void OnUsernameEntered(object sender, UserInputArgs args)
    {
        if (args?.Input == null) return;
        Plugin.SaveUsername(args.Input);
    }

    private void OnPasswordEntered(object sender, UserInputArgs args)
    {
        if (args?.Input == null) return;
        Plugin.SavePassword(args.Input);
    }

    private async void OnUserSearchEntered(object sender, UserInputArgs args)
    {
        if (args == null || string.IsNullOrEmpty(args.Input)) return;
        
        if (args.IsTyping)
        {
            return;
        }

        var (success, users, error) = await ApiClient.SearchUsers(Plugin.AuthToken, args.Input);
        if (success && users != null)
        {
            _searchedUsers = users;
            LoadedScreen.SetContent();
        }
    }

    private async void OnGroupNameEntered(object sender, UserInputArgs args)
    {
        if (args == null || string.IsNullOrEmpty(args.Input)) return;
        
        if (args.IsTyping)
        {
            return;
        }

        var (success, groupId, error) = await ApiClient.CreateGroup(Plugin.AuthToken, args.Input, new List<string>());
        if (success)
        {
            Plugin.SelectedChatId = groupId;
            _currentChatType = "group";
            _currentChatName = args.Input;
            Plugin.CurrentChatType = _currentChatType;
            Plugin.CurrentChatName = _currentChatName;
            _cachedMessages.Clear();
            _currentScreenMode = "chats";
            _searchedUsers.Clear();
            RunAsync(RefreshChats(null));
            LoadedScreen.SetContent();
        }
        else
        {
            Plugin.SendNewNotification(new Notification($"Failed to create group: {error}", 5, Sounds.activationMain));
        }
    }

    private async void OnAddMemberEntered(object sender, UserInputArgs args)
    {
        if (args == null || string.IsNullOrEmpty(args.Input)) return;
        
        if (args.IsTyping)
        {
            return;
        }

        var (success, users, error) = await ApiClient.SearchUsers(Plugin.AuthToken, args.Input);
        if (success && users != null)
        {
            _searchedUsers = users;
            LoadedScreen.SetContent();
        }
    }

    private async Task AddUserToGroup(string userId)
    {
        if (string.IsNullOrEmpty(_currentGroupChatId))
        {
            return;
        }

        var (success, error) = await ApiClient.AddMemberToGroup(Plugin.AuthToken, _currentGroupChatId, userId);
        
        if (success)
        {
            Plugin.SendNewNotification(new Notification("Member added successfully", 3, Sounds.activationMain));
            _searchedUsers.Clear();
            LoadedScreen.SetContent();
        }
        else
        {
            Plugin.SendNewNotification(new Notification($"Failed to add member: {error}", 5, Sounds.activationMain));
        }
    }

    private async Task DoLogin()
    {
        try
        {
            if (Plugin.IsOutdated)
            {
                Plugin.SendNewNotification(new Notification("MonkeMessenger is outdated!", 5, Sounds.error2));
                return;
            }
            
            if (string.IsNullOrEmpty(Plugin.currentUsername) || string.IsNullOrEmpty(Plugin.currentPassword))
            {
                return;
            }

            var (success, token, error) = await ApiClient.Login(Plugin.currentUsername, Plugin.currentPassword);
            
            if (success)
            {
                Plugin.AuthToken = token;
                Plugin.accountLoggedIn = true;

                Plugin.SetAuthToken(token);

                RunAsync(RefreshChats(null));
                
                LoadedScreen.SetContent();
            }
        }
        catch (Exception ex)
        {
            GorillaInfoWatch.Notifications.SendNotification(new Notification($"Login error: {ex.Message}", 5, Sounds.activationMain));
        }
    }

    private async Task DoRegister()
    {
        try
        {
            if (Plugin.IsOutdated)
            {
                Plugin.SendNewNotification(new Notification("Cannot register - MonkeMessenger is outdated!", 5, Sounds.activationMain));
                return;
            }
            
            if (string.IsNullOrEmpty(Plugin.currentUsername) || string.IsNullOrEmpty(Plugin.currentPassword))
            {
                return;
            }

            var (success, error) = await ApiClient.Register(Plugin.currentUsername, Plugin.currentPassword);
        }
        catch (Exception ex)
        {
            GorillaInfoWatch.Notifications.SendNotification(new Notification($"Registration error: {ex.Message}", 5, Sounds.activationMain));
        }
    }

    private async Task RefreshChats(LineBuilder lines)
    {
        var (success, chats, error) = await ApiClient.ListChats(Plugin.AuthToken);
        if (!success)
        {
            return;
        }

        _cachedChats = chats ?? new List<ApiClient.ChatListItem>();
        
        LoadedScreen.SetContent();
    }

    private async Task RefreshMessages(LineBuilder lines, string chatId)
    {
        var (success, messages, error) = await ApiClient.GetMessages(Plugin.AuthToken, chatId);
        if (!success)
        {
            return;
        }

        _cachedMessages = messages ?? new List<ApiClient.MessageItem>();
        
        LoadedScreen.SetContent();
    }

    private async void OnSendEntered(object sender, UserInputArgs args)
    {
        if (args == null || string.IsNullOrEmpty(args.Input)) return;
        
        if (args.IsTyping)
        {
            return;
        }
        
        if (string.IsNullOrEmpty(Plugin.SelectedChatId))
        {
            return;
        }

        var (success, message, error) = await ApiClient.SendMessage(Plugin.AuthToken, Plugin.SelectedChatId, args.Input);
        if (success)
        {
            await RefreshMessages(null, Plugin.SelectedChatId);
        }
    }

    private async void RunAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            GorillaInfoWatch.Notifications.SendNotification(new Notification($"Error: {ex.Message}", 5, Sounds.activationMain));
            LoadedScreen.SetContent();
        }
    }
}



