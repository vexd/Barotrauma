using Barotrauma.Networking;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class WifiComponent : ItemComponent, IClientSerializable, IServerSerializable
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            if (c.Character == null) { return; }

            Dictionary<int, ChannelSetting> prevSettings = MultiChannelConfig;
            MultiChannelConfig = new Dictionary<int, ChannelSetting>();
            ReadMultiChannelConfigMsg(msg);

            //Try to not send if nothing has changed
            bool changed = true;

            var existingkeys = MultiChannelConfig.Keys.Intersect(prevSettings.Keys);
            if(existingkeys.Count()==prevSettings.Keys.Count())
            {
                changed = false;
                //no new or removed keys - check for alterations to settings
                foreach (var kvp in prevSettings)
                {
                    var newSetting = kvp.Value;
                    var oldSetting = prevSettings[kvp.Key];
                    if (newSetting.Send != oldSetting.Send || newSetting.Recieve != oldSetting.Recieve)
                    {
                        changed = true;
                        break;
                    }
                }
            }
            
            //Broadcast back to clients
            if(changed)
            {
                item.CreateServerEvent(this);
            }

        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            WriteMultiChannelConfigMsg(msg);
        }
    }
}
