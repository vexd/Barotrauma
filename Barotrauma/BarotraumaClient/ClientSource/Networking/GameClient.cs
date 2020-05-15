﻿using Barotrauma.Items.Components;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class GameClient : NetworkMember
    {
        public override bool IsClient
        {
            get { return true; }
        }

        private string name;

        private UInt16 nameId = 0;

        public string Name
        {
            get { return name; }
        }

        public string PendingName = string.Empty;

        public void SetName(string value)
        {
            value = value.Replace(":", "").Replace(";", "");
            if (string.IsNullOrEmpty(value)) { return; }
            name = value;
            nameId++;
        }

        public void ForceNameAndJobUpdate()
        {
            nameId++;
        }

        private ClientPeer clientPeer;
        public ClientPeer ClientPeer { get { return clientPeer; } }

        private GUIMessageBox reconnectBox, waitInServerQueueBox;

        //TODO: move these to NetLobbyScreen
        public GUITickBox EndVoteTickBox;
        private GUIComponent buttonContainer;

        private NetStats netStats;

        protected GUITickBox cameraFollowsSub;

        public RoundEndCinematic EndCinematic;

        private ClientPermissions permissions = ClientPermissions.None;
        private List<string> permittedConsoleCommands = new List<string>();

        private bool connected;

        private enum RoundInitStatus
        {
            NotStarted,
            Starting,
            WaitingForStartGameFinalize,
            Started,
            TimedOut,
            Error,
            Interrupted
        }

        private RoundInitStatus roundInitStatus = RoundInitStatus.NotStarted;

        private byte myID;

        private List<Client> otherClients;

        private readonly List<SubmarineInfo> serverSubmarines = new List<SubmarineInfo>();

        private string serverIP, serverName;

        private bool allowReconnect;
        private bool requiresPw;
        private int pwRetries;
        private bool canStart;

        private UInt16 lastSentChatMsgID = 0; //last message this client has successfully sent
        private UInt16 lastQueueChatMsgID = 0; //last message added to the queue
        private List<ChatMessage> chatMsgQueue = new List<ChatMessage>();

        public UInt16 LastSentEntityEventID;

        private ClientEntityEventManager entityEventManager;

        private FileReceiver fileReceiver;

#if DEBUG
        public void PrintReceiverTransters()
        {
            foreach (var transfer in fileReceiver.ActiveTransfers)
            {
                DebugConsole.NewMessage(transfer.FileName + " " + transfer.Progress.ToString());
            }
        }
#endif

        //has the client been given a character to control this round
        public bool HasSpawned;

        public bool SpawnAsTraitor;
        public string TraitorFirstObjective;
        public TraitorMissionPrefab TraitorMission = null;

        public byte ID
        {
            get { return myID; }
        }

        public VoipClient VoipClient
        {
            get;
            private set;
        }

        public override List<Client> ConnectedClients
        {
            get
            {
                return otherClients;
            }
        }

        public FileReceiver FileReceiver
        {
            get { return fileReceiver; }
        }

        public bool MidRoundSyncing
        {
            get { return entityEventManager.MidRoundSyncing; }
        }

        public ClientEntityEventManager EntityEventManager
        {
            get { return entityEventManager; }
        }

        private object serverEndpoint;
        private int ownerKey;
        private bool steamP2POwner;

        public bool IsServerOwner
        {
            get { return ownerKey > 0 || steamP2POwner; }
        }
        
        public GameClient(string newName, string ip, UInt64 steamId, string serverName = null, int ownerKey = 0, bool steamP2POwner = false)
        {
            //TODO: gui stuff should probably not be here?
            this.ownerKey = ownerKey;
            this.steamP2POwner = steamP2POwner;

            roundInitStatus = RoundInitStatus.NotStarted;

            allowReconnect = true;

            netStats = new NetStats();

            inGameHUD = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null)
            {
                CanBeFocused = false
            };

            cameraFollowsSub = new GUITickBox(new RectTransform(new Vector2(0.05f, 0.05f), inGameHUD.RectTransform, anchor: Anchor.TopCenter)
            {
                AbsoluteOffset = new Point(0, 5),
                MaxSize = new Point(25, 25)
            }, TextManager.Get("CamFollowSubmarine"))
            {
                Selected = Camera.FollowSub,
                OnSelected = (tbox) =>
                {
                    Camera.FollowSub = tbox.Selected;
                    return true;
                }
            };

            chatBox = new ChatBox(inGameHUD, isSinglePlayer: false);
            chatBox.OnEnterMessage += EnterChatMessage;
            chatBox.InputBox.OnTextChanged += TypingChatMessage;

            buttonContainer = new GUILayoutGroup(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ButtonAreaTop, inGameHUD.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                AbsoluteSpacing = 5,
                CanBeFocused = false
            };

            EndVoteTickBox = new GUITickBox(new RectTransform(new Vector2(0.1f, 0.4f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("EndRound"))
            {
                UserData = TextManager.Get("EndRound"),
                OnSelected = ToggleEndRoundVote,
                Visible = false
            };

            ShowLogButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.6f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("ServerLog"))
            {
                OnClicked = (GUIButton button, object userData) =>
                {
                    if (serverSettings.ServerLog.LogFrame == null)
                    {
                        serverSettings.ServerLog.CreateLogFrame();
                    }
                    else
                    {
                        serverSettings.ServerLog.LogFrame = null;
                        GUI.KeyboardDispatcher.Subscriber = null;
                    }
                    return true;
                }
            };
            ShowLogButton.TextBlock.AutoScaleHorizontal = true;

            GameMain.DebugDraw = false;
            Hull.EditFire = false;
            Hull.EditWater = false;

            SetName(newName);

            entityEventManager = new ClientEntityEventManager(this);

            fileReceiver = new FileReceiver();
            fileReceiver.OnFinished += OnFileReceived;
            fileReceiver.OnTransferFailed += OnTransferFailed;

            characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, name, null)
            {
                Job = null
            };

            otherClients = new List<Client>();

            serverSettings = new ServerSettings(this, "Server", 0, 0, 0, false, false);

            if (steamId == 0)
            {
                serverEndpoint = ip;
            }
            else
            {
                serverEndpoint = steamId;
            }
            ConnectToServer(serverEndpoint, serverName);

            //ServerLog = new ServerLog("");

            ChatMessage.LastID = 0;
            GameMain.NetLobbyScreen = new NetLobbyScreen();
        }

        private void ConnectToServer(object endpoint, string hostName)
        {
            LastClientListUpdateID = 0;
            foreach (var c in ConnectedClients)
            {
                GameMain.NetLobbyScreen.RemovePlayer(c);
                c.Dispose();
            }
            ConnectedClients.Clear();

            chatBox.InputBox.Enabled = false;
            if (GameMain.NetLobbyScreen?.ChatInput != null)
            {
                GameMain.NetLobbyScreen.ChatInput.Enabled = false;
            }

            serverName = hostName;
            
            myCharacter = Character.Controlled;
            ChatMessage.LastID = 0;

            clientPeer?.Close();
            clientPeer = null;
            object translatedEndpoint = null;
            if (endpoint is string hostIP)
            {
                int port;
                string[] address = hostIP.Split(':');
                if (address.Length == 1)
                {
                    serverIP = hostIP;
                    port = NetConfig.DefaultPort;
                }
                else
                {
                    serverIP = string.Join(":", address.Take(address.Length - 1));
                    if (!int.TryParse(address[address.Length - 1], out port))
                    {
                        DebugConsole.ThrowError("Invalid port: " + address[address.Length - 1] + "!");
                        port = NetConfig.DefaultPort;
                    }
                }

                clientPeer = new LidgrenClientPeer(Name);

                System.Net.IPEndPoint IPEndPoint = null;
                try
                {
                    IPEndPoint = new System.Net.IPEndPoint(Lidgren.Network.NetUtility.Resolve(serverIP), port);
                }
                catch
                {
                    new GUIMessageBox(TextManager.Get("CouldNotConnectToServer"),
                        TextManager.GetWithVariables("InvalidIPAddress", new string[2] { "[serverip]", "[port]" }, new string[2] { serverIP, port.ToString() }));
                    return;
                }

                translatedEndpoint = IPEndPoint;
            }
#if USE_STEAM
            else if (endpoint is UInt64)
            {
                if (steamP2POwner)
                {
                    clientPeer = new SteamP2POwnerPeer(Name);
                }
                else
                {
                    clientPeer = new SteamP2PClientPeer(Name);
                }

                translatedEndpoint = endpoint;
            }
#endif
            clientPeer.OnDisconnect = OnDisconnect;
            clientPeer.OnDisconnectMessageReceived = HandleDisconnectMessage;
            clientPeer.OnInitializationComplete = () =>
            {
                if (SteamManager.IsInitialized)
                {
                    Steamworks.SteamFriends.ClearRichPresence();
                    Steamworks.SteamFriends.SetRichPresence("status", "Playing on " + serverName);
                    Steamworks.SteamFriends.SetRichPresence("connect", "-connect \"" + serverName.Replace("\"", "\\\"") + "\" " + serverEndpoint);
                }

                canStart = true;
                connected = true;

                VoipClient = new VoipClient(this, clientPeer);

                if (Screen.Selected != GameMain.GameScreen)
                {
                    GameMain.NetLobbyScreen.Select();
                }

                chatBox.InputBox.Enabled = true;
                if (GameMain.NetLobbyScreen?.ChatInput != null)
                {
                    GameMain.NetLobbyScreen.ChatInput.Enabled = true;
                }
            };
            clientPeer.OnRequestPassword = (int salt, int retries) =>
            {
                if (pwRetries != retries) { requiresPw = true; }
                pwRetries = retries;
            };
            clientPeer.OnMessageReceived = ReadDataMessage;
            
            // Connect client, to endpoint previously requested from user
            try
            {
                clientPeer.Start(translatedEndpoint, ownerKey);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't connect to " + endpoint.ToString() + ". Error message: " + e.Message);
                Disconnect();
                chatBox.InputBox.Enabled = true;
                if (GameMain.NetLobbyScreen?.ChatInput != null)
                {
                    GameMain.NetLobbyScreen.ChatInput.Enabled = true;
                }
                GameMain.ServerListScreen.Select();
                return;
            }

            updateInterval = new TimeSpan(0, 0, 0, 0, 150);

            CoroutineManager.StartCoroutine(WaitForStartingInfo(), "WaitForStartingInfo");
        }        

        private bool ReturnToPreviousMenu(GUIButton button, object obj)
        {
            Disconnect();

            Submarine.Unload();
            GameMain.Client = null;
            GameMain.GameSession = null;
            if (IsServerOwner)
            {
                GameMain.MainMenuScreen.Select();
            }
            else
            {
                GameMain.ServerListScreen.Select();
            }

            GUIMessageBox.MessageBoxes.RemoveAll(m => true);

            return true;
        }
        
        private bool connectCancelled;
        private void CancelConnect()
        {
            ChildServerRelay.ShutDown();
            connectCancelled = true;
            Disconnect();
        }

        // Before main looping starts, we loop here and wait for approval message
        private IEnumerable<object> WaitForStartingInfo()
        {
            GUI.SetCursorWaiting();
            requiresPw = false;
            pwRetries = -1;

            connectCancelled = false;
            // When this is set to true, we are approved and ready to go
            canStart = false;

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 20);
            DateTime reqAuthTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, 200);

            // Loop until we are approved
            string connectingText = TextManager.Get("Connecting");
            while (!canStart && !connectCancelled)
            {
                if (reconnectBox == null && waitInServerQueueBox == null)
                {
                    string serverDisplayName = serverName;
                    if (string.IsNullOrEmpty(serverDisplayName)) { serverDisplayName = serverIP; }
                    if (string.IsNullOrEmpty(serverDisplayName) && clientPeer?.ServerConnection is SteamP2PConnection steamConnection)
                    {
                        serverDisplayName = steamConnection.SteamID.ToString();
                        if (SteamManager.IsInitialized)
                        {
                            string steamUserName = Steamworks.SteamFriends.GetFriendPersonaName(steamConnection.SteamID);
                            if (!string.IsNullOrEmpty(steamUserName) && steamUserName != "[unknown]")
                            {
                                serverDisplayName = steamUserName;
                            }
                        }
                    }
                    if (string.IsNullOrEmpty(serverDisplayName)) { serverDisplayName = TextManager.Get("Unknown"); }

                    reconnectBox = new GUIMessageBox(
                        connectingText,
                        TextManager.GetWithVariable("ConnectingTo", "[serverip]", serverDisplayName),
                        new string[] { TextManager.Get("Cancel") });
                    reconnectBox.Buttons[0].OnClicked += (btn, userdata) => { CancelConnect(); return true; };
                    reconnectBox.Buttons[0].OnClicked += reconnectBox.Close;
                }

                if (reconnectBox != null)
                {
                    reconnectBox.Header.Text = connectingText + new string('.', ((int)Timing.TotalTime % 3 + 1));
                }

                yield return CoroutineStatus.Running;

                if (DateTime.Now > timeOut)
                {
                    clientPeer?.Close(Lidgren.Network.NetConnection.NoResponseMessage);
                    var msgBox = new GUIMessageBox(TextManager.Get("ConnectionFailed"), TextManager.Get("CouldNotConnectToServer"));
                    msgBox.Buttons[0].OnClicked += ReturnToPreviousMenu;
                    reconnectBox?.Close(); reconnectBox = null;
                    break;
                }
                
                if (requiresPw && !canStart && !connectCancelled)
                {
                    GUI.ClearCursorWait();
                    reconnectBox?.Close(); reconnectBox = null;

                    string pwMsg = TextManager.Get("PasswordRequired");

                    var msgBox = new GUIMessageBox(pwMsg, "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") },
                        relativeSize: new Vector2(0.25f, 0.1f), minSize: new Point(400, (int)(170 * Math.Max(1.0f, GUI.Scale))));
                    var passwordHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), msgBox.Content.RectTransform), childAnchor: Anchor.TopCenter);
                    var passwordBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1f), passwordHolder.RectTransform) { MinSize = new Point(0, 20) })
                    {
                        UserData = "password",
                        Censor = true
                    };

                    msgBox.Content.Recalculate();
                    msgBox.Content.RectTransform.MinSize = new Point(0, msgBox.Content.RectTransform.Children.Sum(c => c.Rect.Height));
                    msgBox.Content.Parent.RectTransform.MinSize = new Point(0, (int)(msgBox.Content.RectTransform.MinSize.Y / msgBox.Content.RectTransform.RelativeSize.Y));

                    var okButton = msgBox.Buttons[0];
                    var cancelButton = msgBox.Buttons[1];

                    okButton.OnClicked += (GUIButton button, object obj) =>
                    {
                        clientPeer.SendPassword(passwordBox.Text);
                        requiresPw = false;
                        return true;
                    };

                    cancelButton.OnClicked += (GUIButton button, object obj) =>
                    {
                        requiresPw = false;
                        connectCancelled = true;
                        GameMain.ServerListScreen.Select();
                        return true;
                    };

                    while (GUIMessageBox.MessageBoxes.Contains(msgBox))
                    {
                        if (!requiresPw)
                        {
                            msgBox.Close();
                            break;
                        }
                        yield return CoroutineStatus.Running;
                    }
                }
            }

            reconnectBox?.Close(); reconnectBox = null;

            GUI.ClearCursorWait();
            if (connectCancelled) { yield return CoroutineStatus.Success; }
            
            yield return CoroutineStatus.Success;
        }

        public override void Update(float deltaTime)
        {
#if DEBUG
            if (PlayerInput.GetKeyboardState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.P)) return;
#endif

            foreach (Client c in ConnectedClients)
            {
                if (c.Character != null && c.Character.Removed) { c.Character = null; }
                c.UpdateSoundPosition();
            }

            if (VoipCapture.Instance != null)
            {
                if (VoipCapture.Instance.LastEnqueueAudio > DateTime.Now - new TimeSpan(0, 0, 0, 0, milliseconds: 100))
                {
                    var myClient = ConnectedClients.Find(c => c.ID == ID);
                    if (Screen.Selected == GameMain.NetLobbyScreen)
                    {
                        GameMain.NetLobbyScreen.SetPlayerSpeaking(myClient);
                    }
                    else
                    {
                        GameMain.GameSession?.CrewManager?.SetClientSpeaking(myClient);
                    }
                }
            }

            /*TODO: reimplement
            if (ShowNetStats && client?.ServerConnection != null)
            {
                netStats.AddValue(NetStats.NetStatType.ReceivedBytes, client.ServerConnection.Statistics.ReceivedBytes);
                netStats.AddValue(NetStats.NetStatType.SentBytes, client.ServerConnection.Statistics.SentBytes);
                netStats.AddValue(NetStats.NetStatType.ResentMessages, client.ServerConnection.Statistics.ResentMessages);
                netStats.Update(deltaTime);
            }*/

            UpdateHUD(deltaTime);

            base.Update(deltaTime);

            try
            {
                incomingMessagesToProcess.Clear();
                incomingMessagesToProcess.AddRange(pendingIncomingMessages);
                foreach (var inc in incomingMessagesToProcess)
                {
                    ReadDataMessage(inc);
                }
                pendingIncomingMessages.Clear();
                clientPeer?.Update(deltaTime);
            }
            catch (Exception e)
            {
                string errorMsg = "Error while reading a message from server. {" + e + "}. ";
                if (GameMain.Client == null) { errorMsg += "Client disposed."; }
                errorMsg += "\n" + e.StackTrace;
                if (e.InnerException != null)
                {
                    errorMsg += "\nInner exception: " + e.InnerException.Message + "\n" + e.InnerException.StackTrace;
                }
                GameAnalyticsManager.AddErrorEventOnce("GameClient.Update:CheckServerMessagesException" + e.TargetSite.ToString(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                DebugConsole.ThrowError("Error while reading a message from server.", e);
                new GUIMessageBox(TextManager.Get("Error"), TextManager.GetWithVariables("MessageReadError", new string[2] { "[message]", "[targetsite]" }, new string[2] { e.Message, e.TargetSite.ToString() }));
                Disconnect();
                GameMain.MainMenuScreen.Select();
                return;
            }

            if (!connected) return;

            if (reconnectBox != null)
            {
                reconnectBox.Close();
                reconnectBox = null;
            }

            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                EndVoteTickBox.Visible = serverSettings.Voting.AllowEndVoting && HasSpawned;

                if (respawnManager != null)
                {
                    respawnManager.Update(deltaTime);
                }

                if (updateTimer <= DateTime.Now)
                {
                    SendIngameUpdate();
                }
            }
            else
            {
                if (updateTimer <= DateTime.Now)
                {
                    SendLobbyUpdate();
                }
            }

            if (serverSettings.VoiceChatEnabled)
            {
                VoipClient?.SendToServer();
            }

            if (IsServerOwner && connected && !connectCancelled)
            {
                if (GameMain.WindowActive)
                {
                    if (ChildServerRelay.Process?.HasExited ?? true)
                    {
                        Disconnect();
                        var msgBox = new GUIMessageBox(TextManager.Get("ConnectionLost"), TextManager.Get("ServerProcessClosed"));
                        msgBox.Buttons[0].OnClicked += ReturnToPreviousMenu;
                    }
                }
            }

            if (updateTimer <= DateTime.Now)
            {
                // Update current time
                updateTimer = DateTime.Now + updateInterval;
            }
        }

        private readonly List<IReadMessage> pendingIncomingMessages = new List<IReadMessage>();
        private readonly List<IReadMessage> incomingMessagesToProcess = new List<IReadMessage>();

        private void ReadDataMessage(IReadMessage inc)
        {
            ServerPacketHeader header = (ServerPacketHeader)inc.ReadByte();

            if (header != ServerPacketHeader.STARTGAMEFINALIZE &&
                header != ServerPacketHeader.ENDGAME &&
                header != ServerPacketHeader.PING_REQUEST &&
                roundInitStatus == RoundInitStatus.WaitingForStartGameFinalize)
            {
                //rewind the header byte we just read
                inc.BitPosition -= 8;
                pendingIncomingMessages.Add(inc);
                return;
            }

            switch (header)
            {
                case ServerPacketHeader.PING_REQUEST:
                    IWriteMessage response = new WriteOnlyMessage();
                    response.Write((byte)ClientPacketHeader.PING_RESPONSE);
                    byte requestLen = inc.ReadByte();
                    response.Write(requestLen);
                    for (int i=0;i<requestLen;i++)
                    {
                        byte b = inc.ReadByte();
                        response.Write(b);
                    }
                    clientPeer.Send(response, DeliveryMethod.Unreliable);
                    break;
                case ServerPacketHeader.CLIENT_PINGS:
                    byte clientCount = inc.ReadByte();
                    for (int i=0;i<clientCount;i++)
                    {
                        byte clientId = inc.ReadByte();
                        UInt16 clientPing = inc.ReadUInt16();
                        Client client = ConnectedClients.Find(c => c.ID == clientId);
                        if (client != null)
                        {
                            client.Ping = clientPing;
                        }
                    }
                    break;
                case ServerPacketHeader.UPDATE_LOBBY:
                    ReadLobbyUpdate(inc);
                    break;
                case ServerPacketHeader.UPDATE_INGAME:
                    try
                    {
                        ReadIngameUpdate(inc);
                    }
                    catch (Exception e)
                    {
                        string errorMsg = "Error while reading an ingame update message from server. {" + e + "}\n" + e.StackTrace;
                        if (e.InnerException != null)
                        {
                            errorMsg += "\nInner exception: " + e.InnerException.Message + "\n" + e.InnerException.StackTrace;
                        }
#if DEBUG
                        DebugConsole.ThrowError("Error while reading an ingame update message from server.", e);
#endif
                        GameAnalyticsManager.AddErrorEventOnce("GameClient.ReadDataMessage:ReadIngameUpdate", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                        throw;
                    }
                    break;
                case ServerPacketHeader.VOICE:
                    if (VoipClient == null)
                    {
                        string errorMsg = "Failed to read a voice packet from the server (VoipClient == null). ";
                        if (GameMain.Client == null) { errorMsg += "Client disposed. "; }
                        errorMsg += "\n" + Environment.StackTrace;
                        GameAnalyticsManager.AddErrorEventOnce(
                            "GameClient.ReadDataMessage:VoipClientNull", 
                            GameMain.Client == null ? GameAnalyticsSDK.Net.EGAErrorSeverity.Error : GameAnalyticsSDK.Net.EGAErrorSeverity.Warning, 
                            errorMsg);
                        return;
                    }

                    VoipClient.Read(inc);
                    break;
                case ServerPacketHeader.QUERY_STARTGAME:
                    string subName = inc.ReadString();
                    string subHash = inc.ReadString();

                    bool usingShuttle = inc.ReadBoolean();
                    string shuttleName = inc.ReadString();
                    string shuttleHash = inc.ReadString();

                    IWriteMessage readyToStartMsg = new WriteOnlyMessage();
                    readyToStartMsg.Write((byte)ClientPacketHeader.RESPONSE_STARTGAME);

                    MultiPlayerCampaign campaign = GameMain.NetLobbyScreen.SelectedMode == GameMain.GameSession?.GameMode.Preset ?
                                                        GameMain.GameSession?.GameMode as MultiPlayerCampaign : null;

                    GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;
                    bool readyToStart;
                    if (campaign == null)
                    {
                        readyToStart = GameMain.NetLobbyScreen.TrySelectSub(subName, subHash, GameMain.NetLobbyScreen.SubList) &&
                                       GameMain.NetLobbyScreen.TrySelectSub(shuttleName, shuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox);
                    }
                    else
                    {
                        readyToStart = !fileReceiver.ActiveTransfers.Any(c => c.FileType == FileTransferType.CampaignSave) &&
                                            (campaign.LastSaveID == campaign.PendingSaveID);
                    }
                    readyToStartMsg.Write(readyToStart);

                    WriteCharacterInfo(readyToStartMsg);
                    
                    clientPeer.Send(readyToStartMsg, DeliveryMethod.Reliable);

                    if (readyToStart && !CoroutineManager.IsCoroutineRunning("WaitForStartRound"))
                    {
                        CoroutineManager.StartCoroutine(GameMain.NetLobbyScreen.WaitForStartRound(startButton: null, allowCancel: false), "WaitForStartRound");
                    }
                    break;
                case ServerPacketHeader.STARTGAME:
                    GameMain.Instance.ShowLoading(StartGame(inc), false);
                    break;
                case ServerPacketHeader.STARTGAMEFINALIZE:
                    if (roundInitStatus == RoundInitStatus.WaitingForStartGameFinalize)
                    {
                        ReadStartGameFinalize(inc);
                    }
                    break;
                case ServerPacketHeader.ENDGAME:
                    string endMessage = inc.ReadString();
                    bool missionSuccessful = inc.ReadBoolean();
                    Character.TeamType winningTeam = (Character.TeamType)inc.ReadByte();
                    if (missionSuccessful && GameMain.GameSession?.Mission != null)
                    {
                        GameMain.GameSession.WinningTeam = winningTeam;
                        GameMain.GameSession.Mission.Completed = true;
                    }

                    roundInitStatus = RoundInitStatus.Interrupted;
                    CoroutineManager.StartCoroutine(EndGame(endMessage), "EndGame");
                    break;
                case ServerPacketHeader.CAMPAIGN_SETUP_INFO:
                    UInt16 saveCount = inc.ReadUInt16();
                    List<string> saveFiles = new List<string>();
                    for (int i = 0; i < saveCount; i++)
                    {
                        saveFiles.Add(inc.ReadString());
                    }
                    MultiPlayerCampaign.StartCampaignSetup(saveFiles);
                    break;
                case ServerPacketHeader.PERMISSIONS:
                    ReadPermissions(inc);
                    break;
                case ServerPacketHeader.ACHIEVEMENT:
                    ReadAchievement(inc);
                    break;
                case ServerPacketHeader.CHEATS_ENABLED:
                    bool cheatsEnabled = inc.ReadBoolean();
                    inc.ReadPadBits();
                    if (cheatsEnabled == DebugConsole.CheatsEnabled)
                    {
                        return;
                    }
                    else
                    {
                        DebugConsole.CheatsEnabled = cheatsEnabled;
                        SteamAchievementManager.CheatsEnabled = cheatsEnabled;
                        if (cheatsEnabled)
                        {
                            var cheatMessageBox = new GUIMessageBox(TextManager.Get("CheatsEnabledTitle"), TextManager.Get("CheatsEnabledDescription"));
                            cheatMessageBox.Buttons[0].OnClicked += (btn, userdata) =>
                            {
                                DebugConsole.TextBox.Select();
                                return true;
                            };
                        }
                    }
                    break;
                case ServerPacketHeader.FILE_TRANSFER:
                    fileReceiver.ReadMessage(inc);
                    break;
                case ServerPacketHeader.TRAITOR_MESSAGE:
                    ReadTraitorMessage(inc);
                    break;
                case ServerPacketHeader.MISSION:
                    GameMain.GameSession.Mission?.ClientRead(inc);
                    break;
            }
        }

        private void ReadStartGameFinalize(IReadMessage inc)
        {
            ushort contentToPreloadCount = inc.ReadUInt16();
            List<ContentFile> contentToPreload = new List<ContentFile>();
            for (int i = 0; i < contentToPreloadCount; i++)
            {
                ContentType contentType = (ContentType)inc.ReadByte();
                string filePath = inc.ReadString();
                contentToPreload.Add(new ContentFile(filePath, contentType));
            }

            GameMain.GameSession.EventManager.PreloadContent(contentToPreload);

            int levelEqualityCheckVal = inc.ReadInt32();

            if (Level.Loaded.EqualityCheckVal != levelEqualityCheckVal)
            {
                string errorMsg = "Level equality check failed. The level generated at your end doesn't match the level generated by the server (seed: " + Level.Loaded.Seed +
                    ", sub: " + Submarine.MainSub.Info.Name + " (" + Submarine.MainSub.Info.MD5Hash.ShortHash + ")" +
                    ", mirrored: " + Level.Loaded.Mirrored + ").";
                GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:LevelsDontMatch" + Level.Loaded.Seed, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                throw new Exception(errorMsg);
            }

            GameMain.GameSession.Mission?.ClientReadInitial(inc);

            roundInitStatus = RoundInitStatus.Started;
        }


        private void OnDisconnect()
        {
            if (SteamManager.IsInitialized)
            {
                Steamworks.SteamFriends.ClearRichPresence();
            }
        }

        private void HandleDisconnectMessage(string disconnectMsg)
        {
            disconnectMsg = disconnectMsg ?? "";

            string[] splitMsg = disconnectMsg.Split('/');
            DisconnectReason disconnectReason = DisconnectReason.Unknown;
            bool disconnectReasonIncluded = false;
            if (splitMsg.Length > 0) 
            {
                if (Enum.TryParse(splitMsg[0], out disconnectReason)) { disconnectReasonIncluded = true; }
            }

            if (disconnectMsg == Lidgren.Network.NetConnection.NoResponseMessage)
            {
                allowReconnect = false;
            }

            DebugConsole.NewMessage("Received a disconnect message (" + disconnectMsg + ")");

            if (disconnectReason != DisconnectReason.Banned &&
                disconnectReason != DisconnectReason.ServerShutdown &&
                disconnectReason != DisconnectReason.TooManyFailedLogins &&
                disconnectReason != DisconnectReason.NotOnWhitelist &&
                disconnectReason != DisconnectReason.MissingContentPackage &&
                disconnectReason != DisconnectReason.InvalidVersion)
            {
                GameAnalyticsManager.AddErrorEventOnce(
                "GameClient.HandleDisconnectMessage", 
                GameAnalyticsSDK.Net.EGAErrorSeverity.Debug, 
                "Client received a disconnect message. Reason: " + disconnectReason.ToString() + ", message: " + disconnectMsg);
            }

            if (disconnectReason == DisconnectReason.ServerFull)
            {
                CoroutineManager.StopCoroutines("WaitForStartingInfo");
                //already waiting for a slot to free up, stop waiting for starting info and 
                //let WaitInServerQueue reattempt connecting later
                if (CoroutineManager.IsCoroutineRunning("WaitInServerQueue"))
                {
                    return;
                }

                reconnectBox?.Close(); reconnectBox = null;

                var queueBox = new GUIMessageBox(
                    TextManager.Get("DisconnectReason.ServerFull"),
                    TextManager.Get("ServerFullQuestionPrompt"), new string[] { TextManager.Get("Cancel"), TextManager.Get("ServerQueue") });

                queueBox.Buttons[0].OnClicked += queueBox.Close;
                queueBox.Buttons[1].OnClicked += queueBox.Close;
                queueBox.Buttons[1].OnClicked += (btn, userdata) =>
                {
                    reconnectBox?.Close(); reconnectBox = null;
                    CoroutineManager.StartCoroutine(WaitInServerQueue(), "WaitInServerQueue");
                    return true;
                };
                return;
            }
            else
            {
                //disconnected/denied for some other reason than the server being full
                // -> stop queuing and show a message box
                waitInServerQueueBox?.Close();
                waitInServerQueueBox = null;
                CoroutineManager.StopCoroutines("WaitInServerQueue");
            }

            bool eventSyncError = 
                disconnectReason == DisconnectReason.ExcessiveDesyncOldEvent ||
                disconnectReason == DisconnectReason.ExcessiveDesyncRemovedEvent ||
                disconnectReason == DisconnectReason.SyncTimeout;

            if (allowReconnect && 
                (disconnectReason == DisconnectReason.Unknown || eventSyncError))
            {
                if (eventSyncError)
                {
                    GameMain.NetLobbyScreen.Select();
                    GameMain.GameSession?.EndRound("");
                    gameStarted = false;
                    myCharacter = null;
                }

                DebugConsole.NewMessage("Attempting to reconnect...");

                //if the first part of the message is the disconnect reason Enum, don't include it in the popup message
                string msg = TextManager.GetServerMessage(disconnectReasonIncluded ? string.Join('/', splitMsg.Skip(1)) :  disconnectMsg);
                msg = string.IsNullOrWhiteSpace(msg) ?
                    TextManager.Get("ConnectionLostReconnecting") :
                    msg + '\n' + TextManager.Get("ConnectionLostReconnecting");

                reconnectBox?.Close();
                reconnectBox = new GUIMessageBox(
                    TextManager.Get("ConnectionLost"),
                    msg, new string[0]);
                connected = false;
                ConnectToServer(serverEndpoint, serverName);
            }
            else
            {
                connected = false;
                connectCancelled = true;

                string msg = "";
                if (disconnectReason == DisconnectReason.Unknown)
                {
                    DebugConsole.NewMessage("Do not attempt reconnect (not allowed).");
                    msg = disconnectMsg;
                }
                else
                {
                    DebugConsole.NewMessage("Do not attempt to reconnect (DisconnectReason doesn't allow reconnection).");
                    msg = TextManager.Get("DisconnectReason." + disconnectReason.ToString());
                    
                    for (int i = 1; i < splitMsg.Length; i++)
                    {
                        msg += TextManager.GetServerMessage(splitMsg[i]);
                    }

                    if (disconnectReason == DisconnectReason.ServerCrashed && IsServerOwner)
                    {
                        msg = TextManager.Get("ServerProcessCrashed");
                    }
                }

                reconnectBox?.Close();

                if (msg == Lidgren.Network.NetConnection.NoResponseMessage)
                {
                    //display a generic "could not connect" popup if the message is Lidgren's "failed to establish connection"
                    var msgBox = new GUIMessageBox(TextManager.Get("ConnectionFailed"), TextManager.Get(allowReconnect ? "ConnectionLost" : "CouldNotConnectToServer"));
                    msgBox.Buttons[0].OnClicked += ReturnToPreviousMenu;
                }
                else
                {
                    var msgBox = new GUIMessageBox(TextManager.Get(allowReconnect ? "ConnectionLost" : "CouldNotConnectToServer"), msg);
                    msgBox.Buttons[0].OnClicked += ReturnToPreviousMenu;
                }

                if (disconnectReason == DisconnectReason.InvalidName)
                {
                    GameMain.ServerListScreen.ClientNameBox.Text = "";
                    GameMain.ServerListScreen.ClientNameBox.Flash(flashDuration: 5.0f);
                    GameMain.ServerListScreen.ClientNameBox.Select();
                }
            }
        }

        private IEnumerable<object> WaitInServerQueue()
        {
            waitInServerQueueBox = new GUIMessageBox(
                    TextManager.Get("ServerQueuePleaseWait"),
                    TextManager.Get("WaitingInServerQueue"), new string[] { TextManager.Get("Cancel") });
            waitInServerQueueBox.Buttons[0].OnClicked += (btn, userdata) =>
            {
                CoroutineManager.StopCoroutines("WaitInServerQueue");
                waitInServerQueueBox?.Close();
                waitInServerQueueBox = null;
                return true;
            };

            while (!connected)
            {
                if (!CoroutineManager.IsCoroutineRunning("WaitForStartingInfo"))
                {
                    ConnectToServer(serverEndpoint, serverName);
                    yield return new WaitForSeconds(5.0f);
                }
                yield return new WaitForSeconds(0.5f);
            }

            waitInServerQueueBox?.Close();
            waitInServerQueueBox = null;

            yield return CoroutineStatus.Success;
        }


        private void ReadAchievement(IReadMessage inc)
        {
            string achievementIdentifier = inc.ReadString();
            SteamAchievementManager.UnlockAchievement(achievementIdentifier);
        }

        private void ReadTraitorMessage(IReadMessage inc)
        {
            TraitorMessageType messageType = (TraitorMessageType)inc.ReadByte();
            string missionIdentifier = inc.ReadString();
            string message = inc.ReadString();
            message = TextManager.GetServerMessage(message);

            var missionPrefab = TraitorMissionPrefab.List.Find(t => t.Identifier == missionIdentifier);
            Sprite icon = missionPrefab?.Icon;

            switch(messageType) 
            {
                case TraitorMessageType.Objective:
                    var isTraitor = !string.IsNullOrEmpty(message); 
                    SpawnAsTraitor = isTraitor;
                    TraitorFirstObjective = message;
                    TraitorMission = missionPrefab;
                    if (Character != null)
                    {
                        Character.IsTraitor = isTraitor;
                        Character.TraitorCurrentObjective = message;
                    }
                    break;
                case TraitorMessageType.Console:
                    GameMain.Client.AddChatMessage(ChatMessage.Create("", message, ChatMessageType.Console, null));
                    DebugConsole.NewMessage(message);
                    break;
                case TraitorMessageType.ServerMessageBox:
                    var msgBox = new GUIMessageBox("", message, new string[0], type: GUIMessageBox.Type.InGame, icon: icon);
                    if (msgBox.Icon != null)
                    {
                        msgBox.IconColor = missionPrefab.IconColor;
                    }
                    break;
                case TraitorMessageType.Server:
                default:
                    GameMain.Client.AddChatMessage(message, ChatMessageType.Server);
                    break;
            }
        }

        private void ReadPermissions(IReadMessage inc)
        {
            List<string> permittedConsoleCommands = new List<string>();
            byte clientID = inc.ReadByte();

            ClientPermissions permissions = ClientPermissions.None;
            List<DebugConsole.Command> permittedCommands = new List<DebugConsole.Command>();
            Client.ReadPermissions(inc, out permissions, out permittedCommands);

            Client targetClient = ConnectedClients.Find(c => c.ID == clientID);
            if (targetClient != null)
            {
                targetClient.SetPermissions(permissions, permittedCommands);
            }
            if (clientID == myID)
            {
                SetMyPermissions(permissions, permittedCommands.Select(command => command.names[0]));
            }
        }

        private void SetMyPermissions(ClientPermissions newPermissions, IEnumerable<string> permittedConsoleCommands)
        {
            if (!(this.permittedConsoleCommands.Any(c => !permittedConsoleCommands.Contains(c)) ||
                permittedConsoleCommands.Any(c => !this.permittedConsoleCommands.Contains(c))))
            {
                if (newPermissions == permissions) return;
            }

            permissions = newPermissions;
            this.permittedConsoleCommands = new List<string>(permittedConsoleCommands);
            //don't show the "permissions changed" popup if the client owns the server
            if (!IsServerOwner)
            {
                GUIMessageBox.MessageBoxes.RemoveAll(mb => mb.UserData as string == "permissions");
                GUIMessageBox msgBox = new GUIMessageBox("", "") { UserData = "permissions" };
                msgBox.Content.ClearChildren();
                msgBox.Content.RectTransform.RelativeSize = new Vector2(0.95f, 0.9f);

                var header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgBox.Content.RectTransform), TextManager.Get("PermissionsChanged"), textAlignment: Alignment.Center, font: GUI.LargeFont);
                header.RectTransform.IsFixedSize = true;

                var permissionArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), msgBox.Content.RectTransform), isHorizontal: true) { Stretch = true, RelativeSpacing = 0.05f };
                var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), permissionArea.RectTransform)) { Stretch = true, RelativeSpacing = 0.05f };
                var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), permissionArea.RectTransform)) { Stretch = true, RelativeSpacing = 0.05f };

                var permissionsLabel = new GUITextBlock(new RectTransform(new Vector2(newPermissions == ClientPermissions.None ? 2.0f : 1.0f, 0.0f), leftColumn.RectTransform),
                    TextManager.Get(newPermissions == ClientPermissions.None ? "PermissionsRemoved" : "CurrentPermissions"),
                    wrap: true, font: (newPermissions == ClientPermissions.None ? GUI.Font : GUI.SubHeadingFont));
                permissionsLabel.RectTransform.NonScaledSize = new Point(permissionsLabel.Rect.Width, permissionsLabel.Rect.Height);
                permissionsLabel.RectTransform.IsFixedSize = true;
                if (newPermissions != ClientPermissions.None)
                {
                    string permissionList = "";
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        if (!newPermissions.HasFlag(permission) || permission == ClientPermissions.None) { continue; }
                        permissionList += "   - " + TextManager.Get("ClientPermission." + permission) + "\n";
                    }
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), leftColumn.RectTransform),
                        permissionList);
                }

                if (newPermissions.HasFlag(ClientPermissions.ConsoleCommands))
                {
                    var commandsLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), rightColumn.RectTransform),
                         TextManager.Get("PermittedConsoleCommands"), wrap: true, font: GUI.SubHeadingFont);
                    var commandList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), rightColumn.RectTransform));
                    foreach (string permittedCommand in permittedConsoleCommands)
                    {
                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), commandList.Content.RectTransform, minSize: new Point(0, 15)),
                            permittedCommand, font: GUI.SmallFont)
                        {
                            CanBeFocused = false
                        };
                    }
                    permissionsLabel.RectTransform.NonScaledSize = commandsLabel.RectTransform.NonScaledSize = 
                        new Point(permissionsLabel.Rect.Width, Math.Max(permissionsLabel.Rect.Height, commandsLabel.Rect.Height));
                    commandsLabel.RectTransform.IsFixedSize = true;
                }

                new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), msgBox.Content.RectTransform), TextManager.Get("ok"))
                {
                    OnClicked = msgBox.Close
                };

                permissionArea.RectTransform.MinSize = new Point(0, Math.Max( leftColumn.RectTransform.Children.Sum(c => c.Rect.Height), rightColumn.RectTransform.Children.Sum(c => c.Rect.Height)));
                permissionArea.RectTransform.IsFixedSize = true;
                int contentHeight = (int)(msgBox.Content.RectTransform.Children.Sum(c => c.Rect.Height + msgBox.Content.AbsoluteSpacing) * 1.05f);
                msgBox.Content.ChildAnchor = Anchor.TopCenter;
                msgBox.Content.Stretch = true;
                msgBox.Content.RectTransform.MinSize = new Point(0, contentHeight);
                msgBox.InnerFrame.RectTransform.MinSize = new Point(0, (int)(contentHeight / permissionArea.RectTransform.RelativeSize.Y / msgBox.Content.RectTransform.RelativeSize.Y));
            }

            GameMain.NetLobbyScreen.UpdatePermissions();
        }

        private IEnumerable<object> StartGame(IReadMessage inc)
        {
            if (Character != null) Character.Remove();
            HasSpawned = false;
            eventErrorWritten = false;
            GameMain.NetLobbyScreen.StopWaitingForStartRound();

            while (CoroutineManager.IsCoroutineRunning("EndGame"))
            {
                if (EndCinematic != null) { EndCinematic.Stop(); }
                yield return CoroutineStatus.Running;
            }

            GameMain.LightManager.LightingEnabled = true;

            //enable spectate button in case we fail to start the round now
            //(for example, due to a missing sub file or an error)
            GameMain.NetLobbyScreen.ShowSpectateButton();

            entityEventManager.Clear();
            LastSentEntityEventID = 0;

            EndVoteTickBox.Selected = false;

            roundInitStatus = RoundInitStatus.Starting;

            int seed                    = inc.ReadInt32();
            string levelSeed            = inc.ReadString();
            //int levelEqualityCheckVal   = inc.ReadInt32();
            float levelDifficulty       = inc.ReadSingle();

            byte losMode            = inc.ReadByte();

            int missionTypeIndex    = inc.ReadByte();

            string subName          = inc.ReadString();
            string subHash          = inc.ReadString();

            bool usingShuttle       = inc.ReadBoolean();
            string shuttleName      = inc.ReadString();
            string shuttleHash      = inc.ReadString();

            string modeIdentifier   = inc.ReadString();
            int missionIndex        = inc.ReadInt16();

            bool respawnAllowed     = inc.ReadBoolean();

            bool disguisesAllowed   = inc.ReadBoolean();
            bool rewiringAllowed    = inc.ReadBoolean();

            bool allowRagdollButton = inc.ReadBoolean();

            serverSettings.ReadMonsterEnabled(inc);

            bool includesFinalize = inc.ReadBoolean(); inc.ReadPadBits();

            GameModePreset gameMode = GameModePreset.List.Find(gm => gm.Identifier == modeIdentifier);
            MultiPlayerCampaign campaign = 
                GameMain.NetLobbyScreen.SelectedMode == GameMain.GameSession?.GameMode.Preset && gameMode == GameMain.NetLobbyScreen.SelectedMode ?
                GameMain.GameSession?.GameMode as MultiPlayerCampaign : 
                null;

            if (gameMode == null)
            {
                DebugConsole.ThrowError("Game mode \"" + modeIdentifier + "\" not found!");
                yield return CoroutineStatus.Success;
            }

            GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;
            GameMain.LightManager.LosMode = (LosMode)losMode;

            serverSettings.AllowDisguises = disguisesAllowed;
            serverSettings.AllowRewiring = rewiringAllowed;
            serverSettings.AllowRagdollButton = allowRagdollButton;

            if (campaign == null)
            {
                if (!GameMain.NetLobbyScreen.TrySelectSub(subName, subHash, GameMain.NetLobbyScreen.SubList))
                {
                    yield return CoroutineStatus.Success;
                }

                if (!GameMain.NetLobbyScreen.TrySelectSub(shuttleName, shuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox))
                {
                    yield return CoroutineStatus.Success;
                }
            }

            Rand.SetSyncedSeed(seed);

            if (campaign == null)
            {
                //this shouldn't happen, TrySelectSub should stop the coroutine if the correct sub/shuttle cannot be found
                if (GameMain.NetLobbyScreen.SelectedSub == null ||
                    GameMain.NetLobbyScreen.SelectedSub.Name != subName ||
                    GameMain.NetLobbyScreen.SelectedSub.MD5Hash?.Hash != subHash)
                {
                    string errorMsg = "Failed to select submarine \"" + subName + "\" (hash: " + subHash + ").";
                    if (GameMain.NetLobbyScreen.SelectedSub == null)
                    {
                        errorMsg += "\n" + "SelectedSub is null";
                    }
                    else
                    {
                        if (GameMain.NetLobbyScreen.SelectedSub.Name != subName)
                        {
                            errorMsg += "\n" + "Name mismatch: " + GameMain.NetLobbyScreen.SelectedSub.Name + " != " + subName;
                        }
                        if (GameMain.NetLobbyScreen.SelectedSub.MD5Hash?.Hash != subHash)
                        {
                            errorMsg += "\n" + "Hash mismatch: " + GameMain.NetLobbyScreen.SelectedSub.MD5Hash?.Hash + " != " + subHash;
                        }
                    }
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:FailedToSelectSub" + subName, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    CoroutineManager.StartCoroutine(EndGame(""));
                    yield return CoroutineStatus.Failure;
                }
                if (GameMain.NetLobbyScreen.SelectedShuttle == null ||
                    GameMain.NetLobbyScreen.SelectedShuttle.Name != shuttleName ||
                    GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash?.Hash != shuttleHash)
                {
                    string errorMsg = "Failed to select shuttle \"" + shuttleName + "\" (hash: " + shuttleHash + ").";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:FailedToSelectShuttle" + shuttleName, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    CoroutineManager.StartCoroutine(EndGame(""));
                    yield return CoroutineStatus.Failure;
                }

                MissionPrefab missionPrefab = missionIndex < 0 ? null : MissionPrefab.List[missionIndex];

                GameMain.GameSession = missionIndex < 0 ?
                    new GameSession(GameMain.NetLobbyScreen.SelectedSub, "", gameMode, MissionType.None) :
                    new GameSession(GameMain.NetLobbyScreen.SelectedSub, "", gameMode, missionPrefab);

                //startRoundTask = Task.Run(async () => { await Task.Yield(); GameMain.GameSession.StartRound(levelSeed, levelDifficulty); });
                GameMain.GameSession.StartRound(levelSeed, levelDifficulty);
            }
            else
            {
                if (GameMain.GameSession?.CrewManager != null) GameMain.GameSession.CrewManager.Reset();
                /*startRoundTask = Task.Run(async () =>
                {
                    await Task.Yield();
                    GameMain.GameSession.StartRound(campaign.Map.SelectedConnection.Level,
                        reloadSub: true,
                        mirrorLevel: campaign.Map.CurrentLocation != campaign.Map.SelectedConnection.Locations[0]);
                });*/
                GameMain.GameSession.StartRound(campaign.Map.SelectedConnection.Level,
                        mirrorLevel: campaign.Map.CurrentLocation != campaign.Map.SelectedConnection.Locations[0]);
            }

            roundInitStatus = RoundInitStatus.WaitingForStartGameFinalize;

            DateTime? timeOut = null;
            DateTime requestFinalizeTime = DateTime.Now;
            TimeSpan requestFinalizeInterval = new TimeSpan(0, 0, 2);

            while (true)
            {
                try
                {
                    if (timeOut.HasValue)
                    {
                        if (DateTime.Now > requestFinalizeTime)
                        {
                            IWriteMessage msg = new WriteOnlyMessage();
                            msg.Write((byte)ClientPacketHeader.REQUEST_STARTGAMEFINALIZE);
                            clientPeer.Send(msg, DeliveryMethod.Unreliable);
                            requestFinalizeTime = DateTime.Now + requestFinalizeInterval;
                        }
                        if (DateTime.Now > timeOut)
                        {
                            DebugConsole.ThrowError("Error while starting the round (did not receive STARTGAMEFINALIZE message from the server). Stopping the round...");
                            roundInitStatus = RoundInitStatus.TimedOut;
                            break;
                        }
                    }
                    else
                    {
                        if (includesFinalize)
                        {
                            ReadStartGameFinalize(inc);
                            break;
                        }

                        //wait for up to 30 seconds for the server to send the STARTGAMEFINALIZE message
                        timeOut = DateTime.Now + new TimeSpan(0, 0, seconds: 30);
                    }

                    if (!connected)
                    {
                        roundInitStatus = RoundInitStatus.Interrupted;
                        break;
                    }

                    if (roundInitStatus != RoundInitStatus.WaitingForStartGameFinalize)
                    {
                        break;
                    }

                    clientPeer.Update((float)Timing.Step);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("There was an error initializing the round.", e, true);
                    roundInitStatus = RoundInitStatus.Error;
                    break;
                }

                //waiting for a STARTGAMEFINALIZE message
                yield return CoroutineStatus.Running;
            }

            if (roundInitStatus != RoundInitStatus.Started)
            {
                if (roundInitStatus != RoundInitStatus.Interrupted)
                {
                    DebugConsole.ThrowError(roundInitStatus.ToString());
                    CoroutineManager.StartCoroutine(EndGame(""));
                    yield return CoroutineStatus.Failure;
                }
                else
                {
                    yield return CoroutineStatus.Success;
                }
            }

            if (GameMain.GameSession.Submarine.Info.IsFileCorrupted)
            {
                DebugConsole.ThrowError($"Failed to start a round. Could not load the submarine \"{GameMain.GameSession.Submarine.Info.Name}\".");
                yield return CoroutineStatus.Failure;
            }

            for (int i = 0; i < Submarine.MainSubs.Length; i++)
            {
                if (Submarine.MainSubs[i] == null) { break; }

                var teamID = i == 0 ? Character.TeamType.Team1 : Character.TeamType.Team2;
                Submarine.MainSubs[i].TeamID = teamID;
                foreach (Submarine sub in Submarine.MainSubs[i].DockedTo)
                {
                    sub.TeamID = teamID;
                }
            }

            if (respawnAllowed) { respawnManager = new RespawnManager(this, GameMain.NetLobbyScreen.UsingShuttle ? GameMain.NetLobbyScreen.SelectedShuttle : null); }

            gameStarted = true;
            ServerSettings.ServerDetailsChanged = true;

            GameMain.GameScreen.Select();

            AddChatMessage($"ServerMessage.HowToCommunicate~[chatbutton]={GameMain.Config.KeyBindText(InputType.Chat)}~[radiobutton]={GameMain.Config.KeyBindText(InputType.RadioChat)}", ChatMessageType.Server);

            yield return CoroutineStatus.Success;
        }

        public IEnumerable<object> EndGame(string endMessage)
        {
            if (!gameStarted)
            {
                GameMain.NetLobbyScreen.Select();
                yield return CoroutineStatus.Success;
            }

            if (GameMain.GameSession != null) { GameMain.GameSession.GameMode.End(endMessage); }

            // Enable characters near the main sub for the endCinematic
            foreach (Character c in Character.CharacterList)
            {
                if (Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, c.WorldPosition) < NetConfig.EnableCharacterDistSqr)
                {
                    c.Enabled = true;
                }
            }

            ServerSettings.ServerDetailsChanged = true;

            gameStarted = false;
            Character.Controlled = null;
            SpawnAsTraitor = false;
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;
            respawnManager = null;
            
            if (Screen.Selected == GameMain.GameScreen)
            {
                EndCinematic = new RoundEndCinematic(Submarine.MainSub, GameMain.GameScreen.Cam);
                while (EndCinematic.Running && Screen.Selected == GameMain.GameScreen)
                {
                    yield return CoroutineStatus.Running;
                }
                EndCinematic = null;
            }

            Submarine.Unload();
            GameMain.NetLobbyScreen.Select();
            myCharacter = null;
            foreach (Client c in otherClients)
            {
                c.InGame = false;
                c.Character = null;
            }
            yield return CoroutineStatus.Success;
        }

        private void ReadInitialUpdate(IReadMessage inc)
        {
            myID = inc.ReadByte();

            UInt16 subListCount = inc.ReadUInt16();
            serverSubmarines.Clear();
            for (int i = 0; i < subListCount; i++)
            {
                string subName = inc.ReadString();
                string subHash = inc.ReadString();
                bool requiredContentPackagesInstalled = inc.ReadBoolean();

                var matchingSub =
                    SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName && s.MD5Hash.Hash == subHash) ??
                    new SubmarineInfo(Path.Combine(SubmarineInfo.SavePath, subName) + ".sub", subHash, tryLoad: false);

                matchingSub.RequiredContentPackagesInstalled = requiredContentPackagesInstalled;
                serverSubmarines.Add(matchingSub);
            }

            GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.SubList, serverSubmarines);
            GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.ShuttleList.ListBox, serverSubmarines);

            gameStarted = inc.ReadBoolean();
            bool allowSpectating = inc.ReadBoolean();

            ReadPermissions(inc);

            if (gameStarted && Screen.Selected != GameMain.GameScreen)
            {
                new GUIMessageBox(TextManager.Get("PleaseWait"), TextManager.Get(allowSpectating ? "RoundRunningSpectateEnabled" : "RoundRunningSpectateDisabled"));
                GameMain.NetLobbyScreen.Select();
            }
        }

        private void ReadClientList(IReadMessage inc)
        {
            UInt16 listId = inc.ReadUInt16();
            List<TempClient> tempClients = new List<TempClient>();
            int clientCount = inc.ReadByte();
            for (int i = 0; i < clientCount; i++)
            {
                byte id             = inc.ReadByte();
                UInt64 steamId      = inc.ReadUInt64();
                UInt16 nameId       = inc.ReadUInt16();
                string name         = inc.ReadString();
                string preferredJob = inc.ReadString();
                UInt16 characterID  = inc.ReadUInt16();
                float karma         = inc.ReadSingle();
                bool muted          = inc.ReadBoolean();
                bool inGame         = inc.ReadBoolean();
                bool hasPermissions = inc.ReadBoolean();
                bool allowKicking   = inc.ReadBoolean() || IsServerOwner;
                inc.ReadPadBits();

                tempClients.Add(new TempClient
                {
                    ID = id,
                    NameID = nameId,
                    SteamID = steamId,
                    Name = name,
                    PreferredJob = preferredJob,
                    CharacterID = characterID,
                    Karma = karma,
                    Muted = muted,
                    InGame = inGame,
                    HasPermissions = hasPermissions,
                    AllowKicking = allowKicking
                });
            }

            if (NetIdUtils.IdMoreRecent(listId, LastClientListUpdateID))
            {
                bool updateClientListId = true;
                List<Client> currentClients = new List<Client>();
                foreach (TempClient tc in tempClients)
                {
                    //see if the client already exists
                    var existingClient = ConnectedClients.Find(c => c.ID == tc.ID && c.Name == tc.Name);
                    if (existingClient == null) //if not, create it
                    {
                        existingClient = new Client(tc.Name, tc.ID)
                        {
                            SteamID = tc.SteamID,
                            Muted = tc.Muted,
                            InGame = tc.InGame,
                            AllowKicking = tc.AllowKicking
                        };
                        ConnectedClients.Add(existingClient);
                        GameMain.NetLobbyScreen.AddPlayer(existingClient);
                    }
                    existingClient.NameID = tc.NameID;
                    existingClient.PreferredJob = tc.PreferredJob;
                    existingClient.Character = null;
                    existingClient.Karma = tc.Karma;
                    existingClient.Muted = tc.Muted;
                    existingClient.HasPermissions = tc.HasPermissions;
                    existingClient.InGame = tc.InGame;
                    existingClient.AllowKicking = tc.AllowKicking;
                    GameMain.NetLobbyScreen.SetPlayerNameAndJobPreference(existingClient);
                    if (Screen.Selected != GameMain.NetLobbyScreen && tc.CharacterID > 0)
                    {
                        existingClient.CharacterID = tc.CharacterID;
                    }
                    if (existingClient.ID == myID)
                    {
                        existingClient.SetPermissions(permissions, permittedConsoleCommands);
                        if (!NetIdUtils.IdMoreRecent(nameId, tc.NameID))
                        {
                            name = tc.Name;
                            nameId = tc.NameID;
                        }
                        if (GameMain.NetLobbyScreen.CharacterNameBox != null &&
                            !GameMain.NetLobbyScreen.CharacterNameBox.Selected)
                        {
                            GameMain.NetLobbyScreen.CharacterNameBox.Text = name;
                        }
                    }
                    currentClients.Add(existingClient);
                }
                //remove clients that aren't present anymore
                for (int i = ConnectedClients.Count - 1; i >= 0; i--)
                {
                    if (!currentClients.Contains(ConnectedClients[i]))
                    {
                        GameMain.NetLobbyScreen.RemovePlayer(ConnectedClients[i]);
                        ConnectedClients[i].Dispose();
                        ConnectedClients.RemoveAt(i);
                    }
                }
                if (updateClientListId) { LastClientListUpdateID = listId; }

#if USE_STEAM
                if (clientPeer is SteamP2POwnerPeer)
                {
                    TaskPool.Add(Steamworks.SteamNetworkingUtils.WaitForPingDataAsync(), (task) =>
                    {
                        Steam.SteamManager.UpdateLobby(serverSettings);
                    });

                    Steam.SteamManager.UpdateLobby(serverSettings);
                }
#endif
            }
        }

        private bool initialUpdateReceived;

        private void ReadLobbyUpdate(IReadMessage inc)
        {
            ServerNetObject objHeader;
            while ((objHeader = (ServerNetObject)inc.ReadByte()) != ServerNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ServerNetObject.SYNC_IDS:
                        bool lobbyUpdated = inc.ReadBoolean();
                        inc.ReadPadBits();

                        if (lobbyUpdated)
                        {
                            var prevDispatcher = GUI.KeyboardDispatcher.Subscriber;

                            UInt16 updateID     = inc.ReadUInt16();

                            UInt16 settingsLen = inc.ReadUInt16();
                            byte[] settingsData = inc.ReadBytes(settingsLen);

                            bool isInitialUpdate = inc.ReadBoolean();
                            if (isInitialUpdate)
                            {
                                if (GameSettings.VerboseLogging)
                                {
                                    DebugConsole.NewMessage("Received initial lobby update, ID: " + updateID + ", last ID: " + GameMain.NetLobbyScreen.LastUpdateID, Color.Gray);
                                }
                                ReadInitialUpdate(inc);
                                initialUpdateReceived = true;
                            }

                            string selectSubName        = inc.ReadString();
                            string selectSubHash        = inc.ReadString();

                            bool usingShuttle           = inc.ReadBoolean();
                            string selectShuttleName    = inc.ReadString();
                            string selectShuttleHash    = inc.ReadString();

                            bool allowSubVoting         = inc.ReadBoolean();
                            bool allowModeVoting        = inc.ReadBoolean();

                            bool voiceChatEnabled       = inc.ReadBoolean();

                            bool allowSpectating        = inc.ReadBoolean();

                            YesNoMaybe traitorsEnabled  = (YesNoMaybe)inc.ReadRangedInteger(0, 2);
                            MissionType missionType     = (MissionType)inc.ReadRangedInteger(0, (int)MissionType.All);
                            int modeIndex               = inc.ReadByte();

                            string levelSeed            = inc.ReadString();
                            float levelDifficulty       = inc.ReadSingle();

                            byte botCount               = inc.ReadByte();
                            BotSpawnMode botSpawnMode   = inc.ReadBoolean() ? BotSpawnMode.Fill : BotSpawnMode.Normal;

                            bool autoRestartEnabled     = inc.ReadBoolean();
                            float autoRestartTimer      = autoRestartEnabled ? inc.ReadSingle() : 0.0f;

                            //ignore the message if we already a more up-to-date one
                            //or if we're still waiting for the initial update
                            if (NetIdUtils.IdMoreRecent(updateID, GameMain.NetLobbyScreen.LastUpdateID) &&
                                (isInitialUpdate || initialUpdateReceived))
                            {
                                ReadWriteMessage settingsBuf = new ReadWriteMessage();
                                settingsBuf.Write(settingsData, 0, settingsLen); settingsBuf.BitPosition = 0;
                                serverSettings.ClientRead(settingsBuf);
                                if (!IsServerOwner)
                                {
                                    ServerInfo info = GameMain.ServerListScreen.UpdateServerInfoWithServerSettings(serverEndpoint, serverSettings);
                                    GameMain.ServerListScreen.AddToRecentServers(info);
                                }

                                GameMain.NetLobbyScreen.LastUpdateID = updateID;

                                serverSettings.ServerLog.ServerName = serverSettings.ServerName;

                                if (!GameMain.NetLobbyScreen.ServerName.Selected) GameMain.NetLobbyScreen.ServerName.Text = serverSettings.ServerName;
                                if (!GameMain.NetLobbyScreen.ServerMessage.Selected) GameMain.NetLobbyScreen.ServerMessage.Text = serverSettings.ServerMessageText;
                                GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;

                                if (!allowSubVoting) GameMain.NetLobbyScreen.TrySelectSub(selectSubName, selectSubHash, GameMain.NetLobbyScreen.SubList);
                                GameMain.NetLobbyScreen.TrySelectSub(selectShuttleName, selectShuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox);

                                GameMain.NetLobbyScreen.SetTraitorsEnabled(traitorsEnabled);
                                GameMain.NetLobbyScreen.SetMissionType(missionType);

                                if (!allowModeVoting) GameMain.NetLobbyScreen.SelectMode(modeIndex);

                                GameMain.NetLobbyScreen.SetAllowSpectating(allowSpectating);
                                GameMain.NetLobbyScreen.LevelSeed = levelSeed;
                                GameMain.NetLobbyScreen.SetLevelDifficulty(levelDifficulty);
                                GameMain.NetLobbyScreen.SetBotCount(botCount);
                                GameMain.NetLobbyScreen.SetBotSpawnMode(botSpawnMode);
                                GameMain.NetLobbyScreen.SetAutoRestart(autoRestartEnabled, autoRestartTimer);

                                serverSettings.VoiceChatEnabled = voiceChatEnabled;
                                serverSettings.Voting.AllowSubVoting = allowSubVoting;
                                serverSettings.Voting.AllowModeVoting = allowModeVoting;

#if USE_STEAM
                                if (clientPeer is SteamP2POwnerPeer)
                                {
                                    Steam.SteamManager.UpdateLobby(serverSettings);
                                }
#endif

                                GUI.KeyboardDispatcher.Subscriber = prevDispatcher;
                            }
                        }

                        bool campaignUpdated = inc.ReadBoolean();
                        inc.ReadPadBits();
                        if (campaignUpdated)
                        {
                            MultiPlayerCampaign.ClientRead(inc);
                        }
                        else if (GameMain.NetLobbyScreen.SelectedMode?.Identifier != "multiplayercampaign")
                        {
                            GameMain.NetLobbyScreen.SetCampaignCharacterInfo(null);
                        }

                        lastSentChatMsgID = inc.ReadUInt16();
                        break;
                    case ServerNetObject.CLIENT_LIST:
                        ReadClientList(inc);
                        break;
                    case ServerNetObject.CHAT_MESSAGE:
                        ChatMessage.ClientRead(inc);
                        break;
                    case ServerNetObject.VOTE:
                        serverSettings.Voting.ClientRead(inc);
                        break;
                }
            }
        }

        private void ReadIngameUpdate(IReadMessage inc)
        {
            List<IServerSerializable> entities = new List<IServerSerializable>();

            float sendingTime = inc.ReadSingle() - 0.0f;//TODO: reimplement inc.SenderConnection.RemoteTimeOffset;

            ServerNetObject? prevObjHeader = null;
            long prevBitPos = 0;
            long prevBytePos = 0;

            long prevBitLength = 0;
            long prevByteLength = 0;

            ServerNetObject objHeader;
            while ((objHeader = (ServerNetObject)inc.ReadByte()) != ServerNetObject.END_OF_MESSAGE)
            {
                bool eventReadFailed = false;
                switch (objHeader)
                {
                    case ServerNetObject.SYNC_IDS:
                        lastSentChatMsgID = inc.ReadUInt16();
                        LastSentEntityEventID = inc.ReadUInt16();
                        break;
                    case ServerNetObject.ENTITY_POSITION:
                        UInt16 id = inc.ReadUInt16();
                        uint msgLength = inc.ReadVariableUInt32();

                        int msgEndPos = (int)(inc.BitPosition + msgLength * 8);

                        var entity = Entity.FindEntityByID(id) as IServerSerializable;
                        if (entity != null)
                        {
                            entity.ClientRead(objHeader, inc, sendingTime);
                        }

                        //force to the correct position in case the entity doesn't exist
                        //or the message wasn't read correctly for whatever reason
                        inc.BitPosition = msgEndPos;
                        inc.ReadPadBits();
                        break;
                    case ServerNetObject.CLIENT_LIST:
                        ReadClientList(inc);
                        break;
                    case ServerNetObject.ENTITY_EVENT:
                    case ServerNetObject.ENTITY_EVENT_INITIAL:
                        if (!entityEventManager.Read(objHeader, inc, sendingTime, entities))
                        {
                            eventReadFailed = true;
                            break;
                        }
                        break;
                    case ServerNetObject.CHAT_MESSAGE:
                        ChatMessage.ClientRead(inc);
                        break;
                    default:
                        List<string> errorLines = new List<string>
                        {
                            "Error while reading update from server (unknown object header \"" + objHeader + "\"!)",
                            "Message length: " + inc.LengthBits + " (" + inc.LengthBytes + " bytes)",
                            prevObjHeader != null ? "Previous object type: " + prevObjHeader.ToString() : "Error occurred on the very first object!",
                            "Previous object was " + (prevBitLength) + " bits long (" + (prevByteLength) + " bytes)"
                        };
                        if (prevObjHeader == ServerNetObject.ENTITY_EVENT || prevObjHeader == ServerNetObject.ENTITY_EVENT_INITIAL)
                        {
                            foreach (IServerSerializable ent in entities)
                            {
                                if (ent == null)
                                {
                                    errorLines.Add(" - NULL");
                                    continue;
                                }
                                Entity e = ent as Entity;
                                errorLines.Add(" - " + e.ToString());
                            }
                        }

                        foreach (string line in errorLines)
                        {
                            DebugConsole.ThrowError(line);
                        }
                        errorLines.Add("Last console messages:");
                        for (int i = DebugConsole.Messages.Count - 1; i > Math.Max(0, DebugConsole.Messages.Count - 20); i--)
                        {
                            errorLines.Add("[" + DebugConsole.Messages[i].Time + "] " + DebugConsole.Messages[i].Text);
                        }
                        GameAnalyticsManager.AddErrorEventOnce("GameClient.ReadInGameUpdate", GameAnalyticsSDK.Net.EGAErrorSeverity.Critical, string.Join("\n", errorLines));

                        DebugConsole.ThrowError("Writing object data to \"crashreport_object.bin\", please send this file to us at http://github.com/Regalis11/Barotrauma/issues");

                        using (FileStream fl = File.Open("crashreport_object.bin", FileMode.Create))
                        using (BinaryWriter sw = new BinaryWriter(fl))
                        {
                            sw.Write(inc.Buffer, (int)(prevBytePos - prevByteLength), (int)(prevByteLength));
                        }

                        throw new Exception("Error while reading update from server: please send us \"crashreport_object.bin\"!");
                }
                prevBitLength = inc.BitPosition - prevBitPos;
                prevByteLength = inc.BytePosition - prevByteLength;

                prevObjHeader = objHeader;
                prevBitPos = inc.BitPosition;
                prevBytePos = inc.BytePosition;

                if (eventReadFailed)
                {
                    break;
                }
            }
        }

        private void SendLobbyUpdate()
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ClientPacketHeader.UPDATE_LOBBY);

            outmsg.Write((byte)ClientNetObject.SYNC_IDS);
            outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
            outmsg.Write(ChatMessage.LastID);
            outmsg.Write(LastClientListUpdateID);
            outmsg.Write(nameId);
            outmsg.Write(name);
            var jobPreferences = GameMain.NetLobbyScreen.JobPreferences;
            if (jobPreferences.Count > 0)
            {
                outmsg.Write(jobPreferences[0].First.Identifier);
            }
            else
            {
                outmsg.Write("");
            }

            var campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
            if (campaign == null || campaign.LastSaveID == 0)
            {
                outmsg.Write((UInt16)0);
            }
            else
            {
                outmsg.Write(campaign.LastSaveID);
                outmsg.Write(campaign.CampaignID);
                outmsg.Write(campaign.LastUpdateID);
                outmsg.Write(GameMain.NetLobbyScreen.CampaignCharacterDiscarded);
            }

            chatMsgQueue.RemoveAll(cMsg => !NetIdUtils.IdMoreRecent(cMsg.NetStateID, lastSentChatMsgID));
            for (int i = 0; i < chatMsgQueue.Count && i < ChatMessage.MaxMessagesPerPacket; i++)
            {
                if (outmsg.LengthBytes + chatMsgQueue[i].EstimateLengthBytesClient() > MsgConstants.MTU - 5)
                {
                    //no more room in this packet
                    break;
                }
                chatMsgQueue[i].ClientWrite(outmsg);
            }
            outmsg.Write((byte)ClientNetObject.END_OF_MESSAGE);

            if (outmsg.LengthBytes > MsgConstants.MTU)
            {
                DebugConsole.ThrowError("Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + MsgConstants.MTU);
            }

            clientPeer.Send(outmsg, DeliveryMethod.Unreliable);
        }

        private void SendIngameUpdate()
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ClientPacketHeader.UPDATE_INGAME);
            outmsg.Write(entityEventManager.MidRoundSyncingDone);
            outmsg.WritePadBits();

            outmsg.Write((byte)ClientNetObject.SYNC_IDS);
            //outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
            outmsg.Write(ChatMessage.LastID);
            outmsg.Write(entityEventManager.LastReceivedID);
            outmsg.Write(LastClientListUpdateID);

            Character.Controlled?.ClientWrite(outmsg);
            GameMain.GameScreen.Cam?.ClientWrite(outmsg);

            entityEventManager.Write(outmsg, clientPeer?.ServerConnection);

            chatMsgQueue.RemoveAll(cMsg => !NetIdUtils.IdMoreRecent(cMsg.NetStateID, lastSentChatMsgID));
            for (int i = 0; i < chatMsgQueue.Count && i < ChatMessage.MaxMessagesPerPacket; i++)
            {
                if (outmsg.LengthBytes + chatMsgQueue[i].EstimateLengthBytesClient() > MsgConstants.MTU - 5)
                {
                    //not enough room in this packet
                    break;
                }
                chatMsgQueue[i].ClientWrite(outmsg);
            }

            outmsg.Write((byte)ClientNetObject.END_OF_MESSAGE);

            if (outmsg.LengthBytes > MsgConstants.MTU)
            {
                DebugConsole.ThrowError("Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + MsgConstants.MTU);
            }

            clientPeer.Send(outmsg, DeliveryMethod.Unreliable);
        }

        public void SendChatMessage(ChatMessage msg)
        {
            if (clientPeer?.ServerConnection == null) { return; }
            lastQueueChatMsgID++;
            msg.NetStateID = lastQueueChatMsgID;
            chatMsgQueue.Add(msg);
        }

        public void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Default)
        {
            if (clientPeer?.ServerConnection == null) { return; }

            ChatMessage chatMessage = ChatMessage.Create(
                gameStarted && myCharacter != null ? myCharacter.Name : name,
                message,
                type,
                gameStarted && myCharacter != null ? myCharacter : null);

            lastQueueChatMsgID++;
            chatMessage.NetStateID = lastQueueChatMsgID;

            chatMsgQueue.Add(chatMessage);
        }

        public void RequestFile(FileTransferType fileType, string file, string fileHash)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.FILE_REQUEST);
            msg.Write((byte)FileTransferMessageType.Initiate);
            msg.Write((byte)fileType);
            if (file != null) msg.Write(file);
            if (fileHash != null) msg.Write(fileHash);
            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void CancelFileTransfer(FileReceiver.FileTransferIn transfer)
        {
            CancelFileTransfer(transfer.ID);
        }

        public void UpdateFileTransfer(int id, int offset, bool reliable=false)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.FILE_REQUEST);
            msg.Write((byte)FileTransferMessageType.Data);
            msg.Write((byte)id);
            msg.Write(offset);
            clientPeer.Send(msg, reliable ? DeliveryMethod.Reliable : DeliveryMethod.Unreliable);
        }

        public void CancelFileTransfer(int id)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.FILE_REQUEST);
            msg.Write((byte)FileTransferMessageType.Cancel);
            msg.Write((byte)id);
            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        private void OnFileReceived(FileReceiver.FileTransferIn transfer)
        {
            switch (transfer.FileType)
            {
                case FileTransferType.Submarine:
                    new GUIMessageBox(TextManager.Get("ServerDownloadFinished"), TextManager.GetWithVariable("FileDownloadedNotification", "[filename]", transfer.FileName));
                    var newSub = new SubmarineInfo(transfer.FilePath);
                    if (newSub.IsFileCorrupted) { return; }

                    var existingSubs = SubmarineInfo.SavedSubmarines.Where(s => s.Name == newSub.Name && s.MD5Hash.Hash == newSub.MD5Hash.Hash).ToList();
                    foreach (SubmarineInfo existingSub in existingSubs)
                    {
                        existingSub.Dispose();
                    }
                    SubmarineInfo.AddToSavedSubs(newSub);

                    for (int i = 0; i < 2; i++)
                    {
                        IEnumerable<GUIComponent> subListChildren = (i == 0) ?
                            GameMain.NetLobbyScreen.ShuttleList.ListBox.Content.Children :
                            GameMain.NetLobbyScreen.SubList.Content.Children;

                        var subElement = subListChildren.FirstOrDefault(c =>
                            ((SubmarineInfo)c.UserData).Name == newSub.Name &&
                            ((SubmarineInfo)c.UserData).MD5Hash.Hash == newSub.MD5Hash.Hash);
                        if (subElement == null) continue;

                        subElement.GetChild<GUITextBlock>().TextColor = new Color(subElement.GetChild<GUITextBlock>().TextColor, 1.0f);
                        subElement.UserData = newSub;
                        subElement.ToolTip = newSub.Description;
                    }

                    if (GameMain.NetLobbyScreen.FailedSelectedSub != null &&
                        GameMain.NetLobbyScreen.FailedSelectedSub.First == newSub.Name &&
                        GameMain.NetLobbyScreen.FailedSelectedSub.Second == newSub.MD5Hash.Hash)
                    {
                        GameMain.NetLobbyScreen.TrySelectSub(newSub.Name, newSub.MD5Hash.Hash, GameMain.NetLobbyScreen.SubList);
                    }

                    if (GameMain.NetLobbyScreen.FailedSelectedShuttle != null &&
                        GameMain.NetLobbyScreen.FailedSelectedShuttle.First == newSub.Name &&
                        GameMain.NetLobbyScreen.FailedSelectedShuttle.Second == newSub.MD5Hash.Hash)
                    {
                        GameMain.NetLobbyScreen.TrySelectSub(newSub.Name, newSub.MD5Hash.Hash, GameMain.NetLobbyScreen.ShuttleList.ListBox);
                    }

                    break;
                case FileTransferType.CampaignSave:
                    var campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
                    if (campaign == null) { return; }

                    GameMain.GameSession.SavePath = transfer.FilePath;
                    if (GameMain.GameSession.SubmarineInfo == null)
                    {
                        var gameSessionDoc = SaveUtil.LoadGameSessionDoc(GameMain.GameSession.SavePath);
                        string subPath = Path.Combine(SaveUtil.TempPath, gameSessionDoc.Root.GetAttributeString("submarine", "")) + ".sub";
                        GameMain.GameSession.SubmarineInfo = new SubmarineInfo(subPath, "");
                    }
                    SaveUtil.LoadGame(GameMain.GameSession.SavePath, GameMain.GameSession);
                    GameMain.GameSession?.SubmarineInfo?.Reload();
                    GameMain.GameSession?.SubmarineInfo?.CheckSubsLeftBehind();
                    if (GameMain.GameSession?.SubmarineInfo?.Name != null)
                    {
                        GameMain.NetLobbyScreen.TryDisplayCampaignSubmarine(GameMain.GameSession.SubmarineInfo);
                    }
                    campaign.LastSaveID = campaign.PendingSaveID;

                    DebugConsole.Log("Campaign save received, save ID " + campaign.LastSaveID);
                    //decrement campaign update ID so the server will send us the latest data
                    //(as there may have been campaign updates after the save file was created)
                    campaign.LastUpdateID--;
                    break;
            }
        }

        private void OnTransferFailed(FileReceiver.FileTransferIn transfer)
        {
            if (transfer.FileType == FileTransferType.CampaignSave)
            {
                GameMain.Client.RequestFile(FileTransferType.CampaignSave, null, null);
            }
        }

        public override void CreateEntityEvent(INetSerializable entity, object[] extraData)
        {
            if (!(entity is IClientSerializable)) throw new InvalidCastException("Entity is not IClientSerializable");
            entityEventManager.CreateEvent(entity as IClientSerializable, extraData);
        }

        public bool HasPermission(ClientPermissions permission)
        {
            return permissions.HasFlag(permission);
        }

        public bool HasConsoleCommandPermission(string commandName)
        {
            if (!permissions.HasFlag(ClientPermissions.ConsoleCommands)) { return false; }

            if (permittedConsoleCommands.Any(c => c.Equals(commandName, StringComparison.OrdinalIgnoreCase))) { return true; }

            //check aliases
            foreach (DebugConsole.Command command in DebugConsole.Commands)
            {
                if (command.names.Contains(commandName))
                {
                    if (command.names.Intersect(permittedConsoleCommands).Any()) { return true; }
                    break;
                }
            }

            return false;
        }

        public override void Disconnect()
        {
            allowReconnect = false;

#if USE_STEAM
            if (clientPeer is SteamP2PClientPeer || clientPeer is SteamP2POwnerPeer)
            {
                SteamManager.LeaveLobby();
            }
#endif

            clientPeer?.Close();
            clientPeer = null;

            List<FileReceiver.FileTransferIn> activeTransfers = new List<FileReceiver.FileTransferIn>(FileReceiver.ActiveTransfers);
            foreach (var fileTransfer in activeTransfers)
            {
                FileReceiver.StopTransfer(fileTransfer, deleteFile: true);
            }

            if (HasPermission(ClientPermissions.ServerLog))
            {
                serverSettings.ServerLog?.Save();
            }

            if (ChildServerRelay.Process != null)
            {
                int checks = 0;
                while (ChildServerRelay.Process != null && !ChildServerRelay.Process.HasExited)
                {
                    if (checks > 10)
                    {
                        ChildServerRelay.ShutDown();
                    }
                    Thread.Sleep(100);
                    checks++;
                }
            }
            ChildServerRelay.ShutDown();

            characterInfo?.Remove();

            VoipClient?.Dispose();
            VoipClient = null;
            GameMain.Client = null;
        }

        public void WriteCharacterInfo(IWriteMessage msg)
        {
            msg.Write(characterInfo == null);
            if (characterInfo == null) return;

            msg.Write((byte)characterInfo.Gender);
            msg.Write((byte)characterInfo.Race);
            msg.Write((byte)characterInfo.HeadSpriteId);
            msg.Write((byte)characterInfo.HairIndex);
            msg.Write((byte)characterInfo.BeardIndex);
            msg.Write((byte)characterInfo.MoustacheIndex);
            msg.Write((byte)characterInfo.FaceAttachmentIndex);

            var jobPreferences = GameMain.NetLobbyScreen.JobPreferences;
            int count = Math.Min(jobPreferences.Count, 3);
            msg.Write((byte)count);
            for (int i = 0; i < count; i++)
            {
                msg.Write(jobPreferences[i].First.Identifier);
                msg.Write((byte)jobPreferences[i].Second);
            }
        }

        public void Vote(VoteType voteType, object data)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.UPDATE_LOBBY);
            msg.Write((byte)ClientNetObject.VOTE);
            serverSettings.Voting.ClientWrite(msg, voteType, data);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void VoteForKick(Client votedClient)
        {
            if (votedClient == null) { return; }
            votedClient.AddKickVote(ConnectedClients.First(c => c.ID == ID));
            Vote(VoteType.Kick, votedClient);
        }

        public override void AddChatMessage(ChatMessage message)
        {
            base.AddChatMessage(message);

            if (string.IsNullOrEmpty(message.Text)) { return; }
            GameMain.NetLobbyScreen.NewChatMessage(message);
            chatBox.AddMessage(message);
        }

        public override void KickPlayer(string kickedName, string reason)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.Kick);
            msg.Write(kickedName);
            msg.Write(reason);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public override void BanPlayer(string kickedName, string reason, bool range = false, TimeSpan? duration = null)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.Ban);
            msg.Write(kickedName);
            msg.Write(reason);
            msg.Write(range);
            msg.Write(duration.HasValue ? duration.Value.TotalSeconds : 0.0); //0 = permaban

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public override void UnbanPlayer(string playerName, string playerIP)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.Unban);
            msg.Write(string.IsNullOrEmpty(playerName) ? "" : playerName);
            msg.Write(string.IsNullOrEmpty(playerIP) ? "" : playerIP);
            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void UpdateClientPermissions(Client targetClient)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ManagePermissions);
            targetClient.WritePermissions(msg);
            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SendCampaignState()
        {
            MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;
            if (campaign == null)
            {
                DebugConsole.ThrowError("Failed send campaign state to the server (no campaign active).\n" + Environment.StackTrace);
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ManageCampaign);
            campaign.ClientWrite(msg);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SendConsoleCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                DebugConsole.ThrowError("Cannot send an empty console command to the server!\n" + Environment.StackTrace);
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ConsoleCommands);
            msg.Write(command);
            Vector2 cursorWorldPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
            msg.Write(cursorWorldPos.X);
            msg.Write(cursorWorldPos.Y);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to start the round (permission required)
        /// </summary>
        public void RequestStartRound()
        {
            if (!HasPermission(ClientPermissions.ManageRound)) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ManageRound);
            msg.Write(false); //indicates round start

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to select a submarine (permission required)
        /// </summary>
        public void RequestSelectSub(int subIndex, bool isShuttle)
        {
            if (!HasPermission(ClientPermissions.SelectSub)) return;

            var subList = isShuttle ? GameMain.NetLobbyScreen.ShuttleList.ListBox : GameMain.NetLobbyScreen.SubList;

            if (subIndex < 0 || subIndex >= subList.Content.CountChildren)
            {
                DebugConsole.ThrowError("Submarine index out of bounds (" + subIndex + ")\n" + Environment.StackTrace);
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.SelectSub);
            msg.Write(isShuttle); msg.WritePadBits();
            msg.Write((UInt16)subIndex);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to select a submarine (permission required)
        /// </summary>
        public void RequestSelectMode(int modeIndex)
        {
            if (!HasPermission(ClientPermissions.SelectMode)) return;
            if (modeIndex < 0 || modeIndex >= GameMain.NetLobbyScreen.ModeList.Content.CountChildren)
            {
                DebugConsole.ThrowError("Gamemode index out of bounds (" + modeIndex + ")\n" + Environment.StackTrace);
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.SelectMode);
            msg.Write((UInt16)modeIndex);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SetupNewCampaign(SubmarineInfo sub, string saveName, string mapSeed)
        {
            GameMain.NetLobbyScreen.CampaignSetupFrame.Visible = false;

            saveName = Path.GetFileNameWithoutExtension(saveName);

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.CAMPAIGN_SETUP_INFO);

            msg.Write(true); msg.WritePadBits();
            msg.Write(saveName);
            msg.Write(mapSeed);
            msg.Write(sub.Name);
            msg.Write(sub.MD5Hash.Hash);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SetupLoadCampaign(string saveName)
        {
            GameMain.NetLobbyScreen.CampaignSetupFrame.Visible = false;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.CAMPAIGN_SETUP_INFO);

            msg.Write(false); msg.WritePadBits();
            msg.Write(saveName);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to end the round (permission required)
        /// </summary>
        public void RequestRoundEnd()
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ManageRound);
            msg.Write(true); //indicates round end

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public bool SpectateClicked(GUIButton button, object userData)
        {
            MultiPlayerCampaign campaign = 
                GameMain.NetLobbyScreen.SelectedMode == GameMain.GameSession?.GameMode.Preset ?
                GameMain.GameSession?.GameMode as MultiPlayerCampaign : null;
            if (campaign != null && campaign.LastSaveID < campaign.PendingSaveID)
            {
                new GUIMessageBox("", TextManager.Get("campaignfiletransferinprogress"));
                return false;
            }
            if (button != null) { button.Enabled = false; }

            IWriteMessage readyToStartMsg = new WriteOnlyMessage();
            readyToStartMsg.Write((byte)ClientPacketHeader.RESPONSE_STARTGAME);

            //assume we have the required sub files to start the round
            //(if not, we'll find out when the server sends the STARTGAME message and can initiate a file transfer)
            readyToStartMsg.Write(true);

            WriteCharacterInfo(readyToStartMsg);

            clientPeer.Send(readyToStartMsg, DeliveryMethod.Reliable);

            return false;
        }

        public bool SetReadyToStart(GUITickBox tickBox)
        {
            if (gameStarted)
            {
                tickBox.Parent.Visible = false;
                return false;
            }
            Vote(VoteType.StartRound, tickBox.Selected);
            return true;
        }

        public bool ToggleEndRoundVote(GUITickBox tickBox)
        {
            if (!gameStarted) return false;

            if (!serverSettings.Voting.AllowEndVoting || !HasSpawned)
            {
                tickBox.Visible = false;
                return false;
            }

            Vote(VoteType.EndRound, tickBox.Selected);
            return false;
        }

        protected CharacterInfo characterInfo;
        protected Character myCharacter;

        public CharacterInfo CharacterInfo
        {
            get { return characterInfo; }
            set { characterInfo = value; }
        }

        public Character Character
        {
            get { return myCharacter; }
            set { myCharacter = value; }
        }

        protected GUIFrame inGameHUD;
        protected ChatBox chatBox;
        public GUIButton ShowLogButton; //TODO: move to NetLobbyScreen
        
        public GUIFrame InGameHUD
        {
            get { return inGameHUD; }
        }

        public ChatBox ChatBox
        {
            get { return chatBox; }
        }
        
        public bool TypingChatMessage(GUITextBox textBox, string text)
        {
            return chatBox.TypingChatMessage(textBox, text);
        }

        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];

            if (string.IsNullOrWhiteSpace(message))
            {
                if (textBox == chatBox.InputBox) textBox.Deselect();
                return false;
            }
            chatBox.ChatManager.Store(message);
            SendChatMessage(message);

            if (textBox.DeselectAfterMessage)
            {
                textBox.Deselect();
            }
            textBox.Text = "";

            if (ChatBox.CloseAfterMessageSent)
            {
                ChatBox.ToggleOpen = false;
                ChatBox.CloseAfterMessageSent = false;
            }

            return true;
        }

        public virtual void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD || GUI.DisableUpperHUD) return;

            if (gameStarted &&
                Screen.Selected == GameMain.GameScreen)
            {
                inGameHUD.AddToGUIUpdateList();
                GameMain.NetLobbyScreen.FileTransferFrame?.AddToGUIUpdateList();
            }

            serverSettings.AddToGUIUpdateList();
            if (serverSettings.ServerLog.LogFrame != null) serverSettings.ServerLog.LogFrame.AddToGUIUpdateList();

            GameMain.NetLobbyScreen?.PlayerFrame?.AddToGUIUpdateList();
        }

        public void UpdateHUD(float deltaTime)
        {
            GUITextBox msgBox = null;

            if (Screen.Selected == GameMain.GameScreen)
            {
                msgBox = chatBox.InputBox;
            }
            else if (Screen.Selected == GameMain.NetLobbyScreen)
            {
                msgBox = GameMain.NetLobbyScreen.ChatInput;
            }

            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                bool disableButtons =
                    Character.Controlled != null &&
                    Character.Controlled.SelectedConstruction?.GetComponent<Controller>() != null;
                buttonContainer.Visible = !disableButtons;
                
                if (!GUI.DisableHUD && !GUI.DisableUpperHUD)
                {
                    inGameHUD.UpdateManually(deltaTime);
                    chatBox.Update(deltaTime);

                    cameraFollowsSub.Visible = Character.Controlled == null;
                }
                if (Character.Controlled == null || Character.Controlled.IsDead)
                {
                    GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                    GameMain.LightManager.LosEnabled = false;
                }
            }

            //tab doesn't autoselect the chatbox when debug console is open,
            //because tab is used for autocompleting console commands
            if (msgBox != null)
            {
                if (GUI.KeyboardDispatcher.Subscriber == null)                
                {
                    bool chatKeyHit = PlayerInput.KeyHit(InputType.Chat);
                    bool radioKeyHit = PlayerInput.KeyHit(InputType.RadioChat) && (Character.Controlled == null || Character.Controlled.SpeechImpediment < 100);

                    if (chatKeyHit || radioKeyHit)
                    {
                        if (msgBox.Selected)
                        {
                            msgBox.Text = "";
                            msgBox.Deselect();
                        }
                        else
                        {
                            if (Screen.Selected == GameMain.GameScreen)
                            {
                                if (chatKeyHit)
                                {
                                    msgBox.AddToGUIUpdateList();
                                    ChatBox.GUIFrame.Flash(Color.DarkGreen, 0.5f);
                                    if (!chatBox.ToggleOpen)
                                    {
                                        ChatBox.CloseAfterMessageSent = !ChatBox.ToggleOpen;
                                        ChatBox.ToggleOpen = true;
                                    }
                                }

                                if (radioKeyHit)
                                {
                                    msgBox.AddToGUIUpdateList();
                                    ChatBox.GUIFrame.Flash(Color.YellowGreen, 0.5f);
                                    if (!chatBox.ToggleOpen)
                                    {
                                        ChatBox.CloseAfterMessageSent = !ChatBox.ToggleOpen;
                                        ChatBox.ToggleOpen = true;
                                    }
                                    
                                    if (!msgBox.Text.StartsWith(ChatBox.RadioChatString))
                                    {
                                        msgBox.Text = ChatBox.RadioChatString;
                                    }
                                } 
                            }

                            msgBox.Select(msgBox.Text.Length);
                        }
                    }
                }
            }
        }

        public virtual void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD || GUI.DisableUpperHUD) return;

            if (fileReceiver != null && fileReceiver.ActiveTransfers.Count > 0)
            {
                var transfer = fileReceiver.ActiveTransfers.First();
                GameMain.NetLobbyScreen.FileTransferFrame.Visible = true;
                GameMain.NetLobbyScreen.FileTransferFrame.UserData = transfer;
                GameMain.NetLobbyScreen.FileTransferTitle.Text =
                    ToolBox.LimitString(
                        TextManager.GetWithVariable("DownloadingFile", "[filename]", transfer.FileName),
                        GameMain.NetLobbyScreen.FileTransferTitle.Font,
                        GameMain.NetLobbyScreen.FileTransferTitle.Rect.Width);
                GameMain.NetLobbyScreen.FileTransferProgressBar.BarSize = transfer.Progress;
                GameMain.NetLobbyScreen.FileTransferProgressText.Text =
                    MathUtils.GetBytesReadable((long)transfer.Received) + " / " + MathUtils.GetBytesReadable((long)transfer.FileSize);
            }
            else
            {
                GameMain.NetLobbyScreen.FileTransferFrame.Visible = false;
            }

            if (!gameStarted || Screen.Selected != GameMain.GameScreen) { return; }

            inGameHUD.DrawManually(spriteBatch);

            if (EndVoteCount > 0)
            {
                if (EndVoteTickBox.Visible)
                {
                    EndVoteTickBox.Text =
                        (EndVoteTickBox.UserData as string) + " " + EndVoteCount + "/" + EndVoteMax;
                }
                else
                {
                    string endVoteText = TextManager.GetWithVariables("EndRoundVotes", new string[2] { "[votes]", "[max]" }, new string[2] { EndVoteCount.ToString(), EndVoteMax.ToString() });
                    GUI.DrawString(spriteBatch, EndVoteTickBox.Rect.Center.ToVector2() - GUI.SmallFont.MeasureString(endVoteText) / 2,
                        endVoteText,
                        Color.White,
                        font: GUI.SmallFont);
                }
            }
            else
            {
                EndVoteTickBox.Text = EndVoteTickBox.UserData as string;
            }

            if (respawnManager != null)
            {
                string respawnText = "";
                float textScale = 1.0f;
                Color textColor = Color.White;
                if (respawnManager.CurrentState == RespawnManager.State.Waiting &&
                    respawnManager.RespawnCountdownStarted)
                {
                    float timeLeft = (float)(respawnManager.RespawnTime - DateTime.Now).TotalSeconds;
                    respawnText = TextManager.GetWithVariable(respawnManager.UsingShuttle ? "RespawnShuttleDispatching" : "RespawningIn", "[time]", ToolBox.SecondsToReadableTime(timeLeft));
                }
                else if (respawnManager.CurrentState == RespawnManager.State.Transporting && 
                    respawnManager.ReturnCountdownStarted)
                {
                    float timeLeft = (float)(respawnManager.ReturnTime - DateTime.Now).TotalSeconds;
                    respawnText = timeLeft <= 0.0f ?
                        "" :
                        TextManager.GetWithVariable("RespawnShuttleLeavingIn", "[time]", ToolBox.SecondsToReadableTime(timeLeft));
                    if (timeLeft < 20.0f)
                    {
                        //oscillate between 0-1
                        float phase = (float)(Math.Sin(timeLeft * MathHelper.Pi) + 1.0f) * 0.5f;
                        textScale = 1.0f + phase * 0.5f;
                        textColor = Color.Lerp(GUI.Style.Red, Color.White, 1.0f - phase);
                    }
                }
                
                if (!string.IsNullOrEmpty(respawnText))
                {
                    GUI.SmallFont.DrawString(spriteBatch, respawnText, new Vector2(120.0f, 10), textColor, 0.0f, Vector2.Zero, textScale, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0.0f);
                }
            }

            if (!ShowNetStats) return;

            netStats.Draw(spriteBatch, new Rectangle(300, 10, 300, 150));

            int width = 200, height = 300;
            int x = GameMain.GraphicsWidth - width, y = (int)(GameMain.GraphicsHeight * 0.3f);

            GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black * 0.7f, true);
            GUI.Font.DrawString(spriteBatch, "Network statistics:", new Vector2(x + 10, y + 10), Color.White);

            /* TODO: reimplement
            if (client.ServerConnection != null)
            {
                GUI.Font.DrawString(spriteBatch, "Ping: " + (int)(client.ServerConnection.AverageRoundtripTime * 1000.0f) + " ms", new Vector2(x + 10, y + 25), Color.White);

                y += 15;

                GUI.SmallFont.DrawString(spriteBatch, "Received bytes: " + client.Statistics.ReceivedBytes, new Vector2(x + 10, y + 45), Color.White);
                GUI.SmallFont.DrawString(spriteBatch, "Received packets: " + client.Statistics.ReceivedPackets, new Vector2(x + 10, y + 60), Color.White);

                GUI.SmallFont.DrawString(spriteBatch, "Sent bytes: " + client.Statistics.SentBytes, new Vector2(x + 10, y + 75), Color.White);
                GUI.SmallFont.DrawString(spriteBatch, "Sent packets: " + client.Statistics.SentPackets, new Vector2(x + 10, y + 90), Color.White);
            }
            else
            {
                GUI.Font.DrawString(spriteBatch, "Disconnected", new Vector2(x + 10, y + 25), Color.White);
            }*/
        }

        public virtual bool SelectCrewCharacter(Character character, GUIComponent frame)
        {
            if (character == null) return false;

            if (character != myCharacter)
            {
                var client = GameMain.NetworkMember.ConnectedClients.Find(c => c.Character == character);
                if (client == null) return false;

                CreateSelectionRelatedButtons(client, frame);
            }

            return true;
        }

        public virtual bool SelectCrewClient(Client client, GUIComponent frame)
        {
            if (client == null || client.ID == ID) return false;
            CreateSelectionRelatedButtons(client, frame);
            return true;
        }

        private void CreateSelectionRelatedButtons(Client client, GUIComponent frame)
        {
            var content = new GUIFrame(new RectTransform(new Vector2(1f, 1.0f - frame.RectTransform.RelativeSize.Y), frame.RectTransform, Anchor.BottomCenter, Pivot.TopCenter),
                    style: null);

            var mute = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform, Anchor.TopCenter),
                TextManager.Get("Mute"))
            {
                Selected = client.MutedLocally,
                OnSelected = (tickBox) => { client.MutedLocally = tickBox.Selected; return true; }
            };

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.35f), content.RectTransform, Anchor.BottomCenter), isHorizontal: true, childAnchor: Anchor.BottomLeft)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };

            if (!GameMain.Client.GameStarted || (GameMain.Client.Character == null || GameMain.Client.Character.IsDead) && (client.Character == null || client.Character.IsDead))
            {
                var messageButton = new GUIButton(new RectTransform(new Vector2(1f, 0.2f), content.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0f, buttonContainer.RectTransform.RelativeSize.Y) },
                    TextManager.Get("message"), style: "GUIButtonSmall")
                {
                    UserData = client,
                    OnClicked = (btn, userdata) =>
                    {
                        chatBox.InputBox.Text = $"{client.Name}; ";
                        CoroutineManager.StartCoroutine(selectCoroutine());
                        return false;
                    }
                };
            }

            // Need a delayed selection due to the inputbox being deselected when a left click occurs outside of it
            IEnumerable<object> selectCoroutine()
            {
                yield return new WaitForSeconds(0.01f, true);
                chatBox.InputBox.Select(chatBox.InputBox.Text.Length);
            }

            if (HasPermission(ClientPermissions.Ban) && client.AllowKicking)
            {
                var banButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.9f), buttonContainer.RectTransform),
                    TextManager.Get("Ban"), style: "GUIButtonSmall")
                {
                    UserData = client,
                    OnClicked = (btn, userdata) => { GameMain.NetLobbyScreen.BanPlayer(client); return false; }
                };
            }
            if (HasPermission(ClientPermissions.Kick) && client.AllowKicking)
            {
                var kickButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.9f), buttonContainer.RectTransform),
                    TextManager.Get("Kick"), style: "GUIButtonSmall")
                {
                    UserData = client,
                    OnClicked = (btn, userdata) => { GameMain.NetLobbyScreen.KickPlayer(client); return false; }
                };
            }
            else if (serverSettings.Voting.AllowVoteKick && client.AllowKicking)
            {
                var kickVoteButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.9f), buttonContainer.RectTransform),
                    TextManager.Get("VoteToKick"), style: "GUIButtonSmall")
                {
                    UserData = client,
                    OnClicked = (btn, userdata) => { VoteForKick(client); btn.Enabled = false; return true; }
                };
                if (GameMain.NetworkMember.ConnectedClients != null)
                {
                    kickVoteButton.Enabled = !client.HasKickVoteFromID(myID);
                }
            }
        }

        public void CreateKickReasonPrompt(string clientName, bool ban, bool rangeBan = false)
        {
            var banReasonPrompt = new GUIMessageBox(
                TextManager.Get(ban ? "BanReasonPrompt" : "KickReasonPrompt"),
                "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") }, new Vector2(0.25f, 0.22f), new Point(400, 220));

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.6f), banReasonPrompt.InnerFrame.RectTransform, Anchor.Center));
            var banReasonBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.3f), content.RectTransform))
            {
                Wrap = true,
                MaxTextLength = 100
            };

            GUINumberInput durationInputDays = null, durationInputHours = null;
            GUITickBox permaBanTickBox = null;

            if (ban)
            {
                
                var labelContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), content.RectTransform), isHorizontal: false);
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), labelContainer.RectTransform), TextManager.Get("BanDuration")) { Padding = Vector4.Zero };
                var buttonContent = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), labelContainer.RectTransform), isHorizontal: true);
                permaBanTickBox = new GUITickBox(new RectTransform(new Vector2(0.4f, 0.15f), buttonContent.RectTransform), TextManager.Get("BanPermanent"))
                {
                    Selected = true
                };

                var durationContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1f), buttonContent.RectTransform), isHorizontal: true)
                {
                    Visible = false
                };

                permaBanTickBox.OnSelected += (tickBox) =>
                {
                    durationContainer.Visible = !tickBox.Selected;
                    return true;
                };

                durationInputDays = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueFloat = 1000
                };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), TextManager.Get("Days"));
                durationInputHours = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueFloat = 24
                };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), TextManager.Get("Hours"));
            }

            banReasonPrompt.Buttons[0].OnClicked += (btn, userData) =>
            {
                if (ban)
                {
                    if (!permaBanTickBox.Selected)
                    {
                        TimeSpan banDuration = new TimeSpan(durationInputDays.IntValue, durationInputHours.IntValue, 0, 0);
                        BanPlayer(clientName, banReasonBox.Text, ban, banDuration);
                    }
                    else
                    {
                        BanPlayer(clientName, banReasonBox.Text, range: rangeBan);
                    }
                }
                else
                {
                    KickPlayer(clientName, banReasonBox.Text);
                }
                return true;
            };
            banReasonPrompt.Buttons[0].OnClicked += banReasonPrompt.Close;
            banReasonPrompt.Buttons[1].OnClicked += banReasonPrompt.Close;
        }

        public void ReportError(ClientNetError error, UInt16 expectedID = 0, UInt16 eventID = 0, UInt16 entityID = 0)
        {
            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write((byte)ClientPacketHeader.ERROR);
            outMsg.Write((byte)error);
            outMsg.Write(Level.Loaded == null ? 0 : Level.Loaded.EqualityCheckVal);
            switch (error)
            {
                case ClientNetError.MISSING_EVENT:
                    outMsg.Write(expectedID);
                    outMsg.Write(eventID);
                    break;
                case ClientNetError.MISSING_ENTITY:
                    outMsg.Write(eventID);
                    outMsg.Write(entityID);
                    break;
            }
            clientPeer.Send(outMsg, DeliveryMethod.Reliable);

            if (!eventErrorWritten)
            {
                WriteEventErrorData(error, expectedID, eventID, entityID);
                eventErrorWritten = true;
            }
        }

        private bool eventErrorWritten;
        private void WriteEventErrorData(ClientNetError error, UInt16 expectedID, UInt16 eventID, UInt16 entityID)
        {
            List<string> errorLines = new List<string>
            {
                error.ToString(), ""
            };

            if (IsServerOwner)
            {
                errorLines.Add("SERVER OWNER");
            }

            if (error == ClientNetError.MISSING_EVENT)
            {
                errorLines.Add("Expected ID: " + expectedID + ", received " + eventID);
            }
            else if (error == ClientNetError.MISSING_ENTITY)
            {
                errorLines.Add("Event ID: " + eventID + ", entity ID " + entityID);
            }

            if (GameMain.GameSession?.GameMode != null)
            {
                errorLines.Add("Game mode: " + GameMain.GameSession.GameMode.Name);
                if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign)
                {
                    errorLines.Add("Campaign ID: " + campaign.CampaignID);
                    errorLines.Add("Campaign save ID: " + campaign.LastSaveID + "(pending: " + campaign.PendingSaveID + ")");
                }
            }
            if (GameMain.GameSession?.Submarine != null)
            {
                errorLines.Add("Submarine: " + GameMain.GameSession.Submarine.Info.Name);
            }
            if (Level.Loaded != null)
            {
                errorLines.Add("Level: " + Level.Loaded.Seed + ", " + Level.Loaded.EqualityCheckVal);
                errorLines.Add("Entity count before generating level: " + Level.Loaded.EntityCountBeforeGenerate);
                errorLines.Add("Entities:");
                foreach (Entity e in Level.Loaded.EntitiesBeforeGenerate)
                {
                    errorLines.Add("    " + e.ID + ": " + e.ToString());
                }
                errorLines.Add("Entity count after generating level: " + Level.Loaded.EntityCountAfterGenerate);
            }

            errorLines.Add("Entity IDs:");
            List<Entity> sortedEntities = Entity.GetEntityList();
            sortedEntities.Sort((e1, e2) => e1.ID.CompareTo(e2.ID));
            foreach (Entity e in sortedEntities)
            {
                errorLines.Add(e.ID + ": " + e.ToString());
            }

            errorLines.Add("");
            errorLines.Add("Last debug messages:");
            for (int i = DebugConsole.Messages.Count - 1; i > 0 && i > DebugConsole.Messages.Count - 15; i--)
            {
                errorLines.Add("   " + DebugConsole.Messages[i].Time + " - " + DebugConsole.Messages[i].Text);
            }

            string filePath = "event_error_log_client_" + Name + "_" + DateTime.UtcNow.ToShortTimeString() + ".log";
            filePath = Path.Combine(ServerLog.SavePath, ToolBox.RemoveInvalidFileNameChars(filePath));

            if (!Directory.Exists(ServerLog.SavePath))
            {
                Directory.CreateDirectory(ServerLog.SavePath);
            }
            File.WriteAllLines(filePath, errorLines);
        }

#if DEBUG
        public void ForceTimeOut()
        {
            clientPeer?.ForceTimeOut();
        }
#endif
    }
}
