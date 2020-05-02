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

            SharedReadChannelGroups(msg);
         
            //Broadcast back to clients
            item.CreateServerEvent(this);
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            SharedWriteChannelGroups(msg);
        }
    }
}
