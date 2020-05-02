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

            //shadow settings and read into new grouping
            ChannelGroup prevSettings = MultiChannelConfig;
            ChannelGroup replSettings = new ChannelGroup();
            replSettings.ReadMultiChannelConfigMsg(msg);

            //Swap settings
            MultiChannelConfig = replSettings;

            //Try to not send if nothing has changed
            if (!replSettings.Equals(prevSettings))
            {
                //Broadcast back to clients
                item.CreateServerEvent(this);
            }

        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            MultiChannelConfig.WriteMultiChannelConfigMsg(msg);
        }
    }
}
