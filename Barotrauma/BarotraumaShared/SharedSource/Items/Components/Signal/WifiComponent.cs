using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using NLog.Layouts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    public class ChannelSetting
    {
        public bool Send { get; set; } = true;
        public bool Recieve { get; set; } = true;

        public ChannelSetting(bool send, bool recieve)
        {
            Send = send;
            Recieve = recieve;
        }
    }

    public class ChannelGroup
    {
        private Dictionary<int, ChannelSetting>  Channels = new Dictionary<int, ChannelSetting>();

        public void WriteMultiChannelConfigMsg(IWriteMessage msg)
        {
            // Write channel updates to server to allow send/recieve
            msg.Write(Channels.Count);
            foreach (var kvp in Channels)
            {
                msg.Write(kvp.Key);
                msg.Write(kvp.Value.Send);
                msg.Write(kvp.Value.Recieve);
            }
        }

        public void ReadMultiChannelConfigMsg(IReadMessage msg)
        {
            int count = msg.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                int channel = msg.ReadInt32();
                bool send = msg.ReadBoolean();
                bool recieve = msg.ReadBoolean();

                Channels.Add(channel, new ChannelSetting(send, recieve));
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

        public bool AddChannel(int channel, bool send, bool recieve)
        {
            ChannelSetting existing;
            if (!Channels.TryGetValue(channel, out existing))
            {
                Channels.Add(channel, new ChannelSetting(send, recieve));
                return true;
            }

            return false;
        }

        public bool RemoveChannel(int channel)
        {
            bool ret = Channels.Remove(channel);
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

        List<ChannelGroup> ChannelGroups;

        //multi channel
        ChannelGroup MultiChannelConfig = new ChannelGroup();

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

        public WifiComponent(Item item, XElement element)
            : base (item, element)
        {
            list.Add(this);
            IsActive = true;

            InitProjSpecific(element);
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
                (sender.MultiChannel && !MultiChannelCanRecieve(sender))) 
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
            get {  return MultiChannelConfig.OpenSendChannels; }
        }

        public IEnumerable<int> OpenRecieveChannels
        {
            get { return MultiChannelConfig.OpenRecieveChannels; }
        }

        public bool MultiChannelCanRecieve(WifiComponent sender)
        {
            return MultiChannelConfig.CanRecieve(sender.MultiChannelConfig);
        }

        public bool AddChannel(int channel, bool send, bool recieve)
        {
            bool added = MultiChannelConfig.AddChannel(channel, send, recieve);
#if CLIENT
            if(added)
                item.CreateClientEvent(this);
#endif
            return added;
        }

        public bool RemoveChannel(int channel)
        {
            bool removed =  MultiChannelConfig.RemoveChannel(channel);
#if CLIENT
            if(removed)
                item.CreateClientEvent(this);
#endif
            return removed;
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
    }
}
