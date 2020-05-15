using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using NLog.Layouts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Channels;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    public class ChannelSetting
    {
        public bool Send { get; set; } = true;
        public bool Recieve { get; set; } = true;

        public int ChannelId { get; set; }

        public ChannelSetting(int channel, bool send, bool recieve)
        {
            Send = send;
            Recieve = recieve;
            ChannelId = channel;
        }
    }

    public class ChannelGroup
    {
        internal Dictionary<int, ChannelSetting>  Channels = new Dictionary<int, ChannelSetting>();

        public string Name { get; set; } = "Channel Group";

        // Immutable over liftime
        public int ID { get; }

        public ChannelGroup(int id)
        {
            ID = id;
            Name = "Group_" + ID.ToString();
        }

        public void WriteStateMsg(IWriteMessage msg)
        {
            //Group info
            msg.Write(Channels.Count);
            msg.Write(Name);

            //Per channel setting
            foreach (var kvp in Channels)
            {
                msg.Write(kvp.Value.ChannelId);
                msg.Write(kvp.Value.Send);
                msg.Write(kvp.Value.Recieve);
            }
        }

        public void ReadStateMsg(IReadMessage msg)
        {
            //Group info
            int count = msg.ReadInt32();
            string name = msg.ReadString();
            Name = name;

            List<int> activeIDs = new List<int>();

            //Per channel setting
            for (int i = 0; i < count; i++)
            {
                int channelId = msg.ReadInt32();
                bool send = msg.ReadBoolean();
                bool recieve = msg.ReadBoolean();

                //Locate existing and update or create new setting
                ChannelSetting setting;
                if (Channels.TryGetValue(channelId, out setting))
                {
                    setting.Send = send;
                    setting.Recieve = recieve;
                }
                else
                {
                    Channels.Add(channelId, new ChannelSetting(channelId, send, recieve));
                }

                activeIDs.Add(channelId);
            }

            //Remove inactive channel Ids
            {
                List<int> inactiveIDs = new List<int>();
                foreach (var kvp in Channels)
                {
                    if (!activeIDs.Contains(kvp.Key))
                        inactiveIDs.Add(kvp.Key);
                }

                foreach (var ID in inactiveIDs)
                {
                    Channels.Remove(ID);
                }
            }
        }

        public IEnumerable<int> OpenSendChannels
        {
            get
            {
                List<int> openChannels = new List<int>(Channels.Count);
                foreach (var kvp in Channels)
                {
                    if (kvp.Value.Send)
                        openChannels.Add(kvp.Key);
                }
                return openChannels;
            }
        }

        public IEnumerable<int> OpenRecieveChannels
        {
            get
            {
                List<int> openChannels = new List<int>(Channels.Count);
                foreach (var kvp in Channels)
                {
                    if (kvp.Value.Recieve)
                        openChannels.Add(kvp.Key);
                }
                return openChannels;
            }
        }

        public bool CanRecieve(ChannelGroup sender)
        {
            foreach (var sendChannel in sender.OpenSendChannels)
            {
                if (OpenRecieveChannels.Contains(sendChannel))
                    return true;
            }

            return false;
        }

        private int lastAddedChannel = 0;
        const int MAX_AUTO_CHANNELS = 10000;

        public bool AddChannel()
        {
            for(int channel=0;channel<MAX_AUTO_CHANNELS;channel++)
            {
                ChannelSetting existing;
                if (!Channels.TryGetValue(channel, out existing))
                {
                    Channels.Add(channel, new ChannelSetting(channel, true, true));
                    return true;
                }
            }

            return false;
        }

        public bool AddChannel(int channel, bool send=true, bool recieve=true)
        {
            ChannelSetting existing;
            if (!Channels.TryGetValue(channel, out existing))
            {
                Channels.Add(channel, new ChannelSetting(channel, send, recieve));
                lastAddedChannel = channel;
                return true;
            }

            return false;
        }

        public bool RemoveChannel(int channel)
        {
            bool ret = Channels.Remove(channel);
            return ret;
        }

        public bool RemoveChannel(ChannelSetting channel)
        {
            bool ret = Channels.Remove(channel.ChannelId);
            return ret;
        }

        public bool Equals(ChannelGroup other)
        {
            bool equal = false;
            var existingkeys = Channels.Keys.Intersect(other.Channels.Keys);
            if (existingkeys.Count() == other.Channels.Keys.Count())
            {
                equal = true;
                //no new or removed keys - check for alterations to settings
                foreach (var kvp in other.Channels)
                {
                    var otherSetting = kvp.Value;
                    var setting = Channels[kvp.Key];
                    if (setting.Send != otherSetting.Send || setting.Recieve != otherSetting.Recieve)
                    {
                        equal = false;
                        break;
                    }
                }
            }

            return equal;
        }

        public void Clear()
        {
            Channels.Clear();
        }
    }

    partial class WifiComponent : ItemComponent, IClientSerializable, IServerSerializable
    {
        private static List<WifiComponent> list = new List<WifiComponent>();

        private float range;

        private int channel;
        
        private float chatMsgCooldown;

        private string prevSignal;

        private int wifiLocalChannelGroupNetID = 0;

        private const int clientBaseGroupNetID = 0;
        private const int serverBaseGroupNetID = 65535;

        //Channel group container
        List<ChannelGroup> ChannelGroups = new List<ChannelGroup>();

        public int ActiveChannelGroupIndex
        {
            get { return ChannelGroups.IndexOf(activeChannelGroup); }
            set { activeChannelGroup = ChannelGroups[value]; }
        }

        private ChannelGroup activeChannelGroup = null;

        //Channel group accessor - used for comms filtering in multi-channel mode
        public ChannelGroup ActiveChannelGroup
        {
            get { return activeChannelGroup; }
            set {
                if (activeChannelGroup != value)
                {
                    activeChannelGroup = value;
#if CLIENT
                    item.CreateClientEvent(this);
#endif
                }
            }
        }

        [Serialize(false, false)]
        public bool MultiChannel
        {
            get;
            set;
        } = false;

        [Serialize(Character.TeamType.None, true, description: "WiFi components can only communicate with components that have the same Team ID.")]
        public Character.TeamType TeamID { get; set; }

        [Editable, Serialize(20000.0f, false, description: "How close the recipient has to be to receive a signal from this WiFi component.")]
        public float Range
        {
            get { return range; }
            set
            {
                range = Math.Max(value, 0.0f);
#if CLIENT
                item.ResetCachedVisibleSize();
#endif
            }
        }

        [InGameEditable, Serialize(1, true, description: "WiFi components can only communicate with components that use the same channel.")]
        public int Channel
        {
            get { return channel; }
            set
            {
                channel = MathHelper.Clamp(value, 0, 10000);
            }
        }


        [Serialize(false, false, description: "Can the component communicate with wifi components in another team's submarine (e.g. enemy sub in Combat missions, respawn shuttle). Needs to be enabled on both the component transmitting the signal and the component receiving it.")]
        public bool AllowCrossTeamCommunication
        {
            get;
            set;
        }

        [Editable, Serialize(false, false, description: "If enabled, any signals received from another chat-linked wifi component are displayed " +
            "as chat messages in the chatbox of the player holding the item.")]
        public bool LinkToChat
        {
            get;
            set;
        }

        [Editable, Serialize(1.0f, true, description: "How many seconds have to pass between signals for a message to be displayed in the chatbox. " +
            "Setting this to a very low value is not recommended, because it may cause an excessive amount of chat messages to be created " +
            "if there are chat-linked wifi components that transmit a continuous signal.")]
        public float MinChatMessageInterval
        {
            get;
            set;
        }

        [Editable, Serialize(false, true, description: "If set to true, the component will only create chat messages when the received signal changes.")]
        public bool DiscardDuplicateChatMessages
        {
            get;
            set;
        }

        enum CommChannelIds : int
        {
            Default = 0,
            ShipWide,
            Command,
            Engineering,
            Medical,
            Security,
            Traitor,
        };

        public WifiComponent(Item item, XElement element)
            : base (item, element)
        {
            list.Add(this);
            IsActive = true;

#if CLIENT
            wifiLocalChannelGroupNetID = clientBaseGroupNetID;
#elif SERVER
            wifiLocalChannelGroupNetID = serverBaseGroupNetID;
#endif

            SetupCommChannels(element);
            InitProjSpecific(element);
        }

        private void SetupCommChannels(XElement element)
        {
            int baseID = serverBaseGroupNetID;
            int idOffset = 0;
            {
               
                var group = AddChannelGroup(baseID + (idOffset++));
                group.Name = "Default";
                group.AddChannel((int)CommChannelIds.Default, true, true);
                group.AddChannel((int)CommChannelIds.ShipWide, false, true);
                ActiveChannelGroup = group;
            }

            {
                var group = AddChannelGroup(baseID + (idOffset++));
                group.Name = "Shipwide";
                group.AddChannel((int)CommChannelIds.ShipWide, true, true);
            }

            {
                var group = AddChannelGroup(baseID + (idOffset++));
                group.Name = "Commander";
                group.AddChannel((int)CommChannelIds.Command, true, true);
                group.AddChannel((int)CommChannelIds.ShipWide, false, true);
            }

            {
                var group = AddChannelGroup(baseID + (idOffset++));
                group.Name = "Engineering";
                group.AddChannel((int)CommChannelIds.Engineering, true, true);
                group.AddChannel((int)CommChannelIds.ShipWide, false, true);
            }

            {
                var group = AddChannelGroup(baseID + (idOffset++));
                group.Name = "Medical";
                group.AddChannel((int)CommChannelIds.Medical, true, true);
                group.AddChannel((int)CommChannelIds.ShipWide, false, true);
            }

            {
                var group = AddChannelGroup(baseID + (idOffset++));
                group.Name = "Security";
                group.AddChannel((int)CommChannelIds.Security, true, true);
                group.AddChannel((int)CommChannelIds.ShipWide, false, true);
            }

            //TODO need to randomise traitor comms channel and sync it secretly
            {
                var group = AddChannelGroup(baseID + (idOffset++));
                group.Name = "Traitor";
                group.AddChannel((int)CommChannelIds.Traitor, true, true);
                group.AddChannel((int)CommChannelIds.ShipWide, false, true);
            }

        }

        public bool CanTransmit()
        {
            return HasRequiredContainedItems(user: null, addMessage: false);
        }
        
        public IEnumerable<WifiComponent> GetReceiversInRange()
        {
            return list.Where(w => w != this && w.CanReceive(this));
        }

        public bool CanReceive(WifiComponent sender)
        {
            if (sender == null || 
                (!sender.MultiChannel && sender.channel != channel) ||
                (sender.MultiChannel && sender.ActiveChannelGroup != null && !MultiChannelCanRecieve(sender))) 
            {
                return false; 
            }

            if (sender.TeamID != TeamID && !AllowCrossTeamCommunication)
            {
                return false;
            }            

            if (Vector2.DistanceSquared(item.WorldPosition, sender.item.WorldPosition) > sender.range * sender.range) { return false; }

            return HasRequiredContainedItems(user: null, addMessage: false);
        }

        public IEnumerable<int> OpenSendChannels
        {
            get {  return ActiveChannelGroup.OpenSendChannels; }
        }

        public IEnumerable<int> OpenRecieveChannels
        {
            get { return ActiveChannelGroup.OpenRecieveChannels; }
        }

        public bool MultiChannelCanRecieve(WifiComponent sender)
        {
            return ActiveChannelGroup.CanRecieve(sender.ActiveChannelGroup);
        }

        public bool AddChannel(int channel, bool send, bool recieve)
        {
            bool added = ActiveChannelGroup.AddChannel(channel, send, recieve);
#if CLIENT
            if(added)
                item.CreateClientEvent(this);
#endif
            return added;
        }

        public bool RemoveChannel(int channel)
        {
            bool removed = ActiveChannelGroup.RemoveChannel(channel);
#if CLIENT
            if(removed)
                item.CreateClientEvent(this);
#endif
            return removed;
        }

        

        public ChannelGroup AddChannelGroup(int overrideID=-1,bool broadcast = true)
        {
            int groupID = 0;
            if (overrideID != -1)
            {
                groupID = overrideID;
            }
            else
            {
                wifiLocalChannelGroupNetID++;
                groupID = wifiLocalChannelGroupNetID;
            }

            var newGroup = new ChannelGroup(groupID);
            ChannelGroups.Add(newGroup);
#if CLIENT
            if(broadcast)
                item.CreateClientEvent(this);
#endif
            return newGroup;
        }

        public void RemoveChanelGroup(ChannelGroup group)
        {
            bool removed = ChannelGroups.Remove(group);
#if CLIENT
            if (removed)
                item.CreateClientEvent(this);
#endif
        }

        partial void InitProjSpecific(XElement element);
        partial void UpdateProjSpecific();

        public override void Update(float deltaTime, Camera cam)
        {
            chatMsgCooldown -= deltaTime;
            if (chatMsgCooldown <= 0.0f)
            {
                IsActive = false;
            }

            UpdateProjSpecific();
        }

        public void TransmitSignal(int stepsTaken, string signal, Item source, Character sender, bool sendToChat, float signalStrength = 1.0f)
        {
            var senderComponent = source?.GetComponent<WifiComponent>();
            if (senderComponent != null && !CanReceive(senderComponent)) return;

            bool chatMsgSent = false;

            var receivers = GetReceiversInRange();
            foreach (WifiComponent wifiComp in receivers)
            {
                //signal strength diminishes by distance
                float sentSignalStrength = signalStrength *
                    MathHelper.Clamp(1.0f - (Vector2.Distance(item.WorldPosition, wifiComp.item.WorldPosition) / wifiComp.range), 0.0f, 1.0f);
                wifiComp.item.SendSignal(stepsTaken, signal, "signal_out", sender, 0, source, sentSignalStrength);
                
                if (source != null)
                {
                    foreach (Item receiverItem in wifiComp.item.LastSentSignalRecipients)
                    {
                        if (!source.LastSentSignalRecipients.Contains(receiverItem))
                        {
                            source.LastSentSignalRecipients.Add(receiverItem);
                        }
                    }
                }                

                if (DiscardDuplicateChatMessages && signal == prevSignal) continue;

                if (LinkToChat && wifiComp.LinkToChat && chatMsgCooldown <= 0.0f && sendToChat)
                {
                    if (wifiComp.item.ParentInventory != null &&
                        wifiComp.item.ParentInventory.Owner != null &&
                        GameMain.NetworkMember != null)
                    {
                        string chatMsg = signal;
                        if (senderComponent != null)
                        {
                            chatMsg = ChatMessage.ApplyDistanceEffect(chatMsg, 1.0f - sentSignalStrength);
                        }
                        if (chatMsg.Length > ChatMessage.MaxLength) chatMsg = chatMsg.Substring(0, ChatMessage.MaxLength);
                        if (string.IsNullOrEmpty(chatMsg)) continue;

#if CLIENT
                        if (wifiComp.item.ParentInventory.Owner == Character.Controlled)
                        {
                            if (GameMain.Client == null)
                                GameMain.NetworkMember.AddChatMessage(signal, ChatMessageType.Radio, source == null ? "" : source.Name);
                        }
#endif

#if SERVER
                        if (GameMain.Server != null)
                        {
                            Client recipientClient = GameMain.Server.ConnectedClients.Find(c => c.Character == wifiComp.item.ParentInventory.Owner);
                            if (recipientClient != null)
                            {
                                GameMain.Server.SendDirectChatMessage(
                                    ChatMessage.Create(source == null ? "" : source.Name, chatMsg, ChatMessageType.Radio, null), recipientClient);
                            }
                        }
#endif
                        chatMsgSent = true;
                    }
                }
            }
            if (chatMsgSent) 
            { 
                chatMsgCooldown = MinChatMessageInterval;
                IsActive = true;
            }

            prevSignal = signal;
        }
                
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (connection == null || connection.Name != "signal_in") return;
            TransmitSignal(stepsTaken, signal, source, sender, true, signalStrength);
        }

        protected override void RemoveComponentSpecific()
        {
            list.Remove(this);
        }

        public void SharedWriteChannelGroups(IWriteMessage msg)
        {
            //Channel Group sets
            msg.Write(ChannelGroups.Count);
            foreach (var channelGroup in ChannelGroups)
            {
                msg.Write(channelGroup.ID);
                channelGroup.WriteStateMsg(msg);
            }

            //Active channel group
            int activeChannelGroupId = activeChannelGroup!=null ? activeChannelGroup.ID : -1;
            msg.Write(activeChannelGroupId);
        }

        public void SharedReadChannelGroups(IReadMessage msg)
        {
            List<int> activeGroups = new List<int>();

            //Channel Group sets
            int numGroups = msg.ReadInt32();
            for (int i = 0; i < numGroups; i++)
            {
                int channelGroupID = msg.ReadInt32();
                activeGroups.Add(channelGroupID);

                //locate existing group or make a new one
                ChannelGroup updateGroup = ChannelGroups.Find(itgroup => itgroup.ID == channelGroupID);
                if (updateGroup == null)
                {
                    updateGroup = new ChannelGroup(channelGroupID);
                    ChannelGroups.Add(updateGroup);
                }
                updateGroup.ReadStateMsg(msg);
              
            }

            //Remove inactive groups
            {
                List<ChannelGroup> inactiveGroups = new List<ChannelGroup>();
                foreach(var group in ChannelGroups)
                {
                    if (!activeGroups.Contains(group.ID))
                        inactiveGroups.Add(group);
                }

                foreach (var group in inactiveGroups)
                {
                    ChannelGroups.Remove(group);
                }
            }

            {
                //Active channel group
                int activeChannelGroupID = msg.ReadInt32();
                ChannelGroup activeGroup = ChannelGroups.Find(itgroup => itgroup.ID == activeChannelGroupID);
                activeChannelGroup = activeGroup;
            }
        }
    }
}
