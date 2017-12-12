﻿////<auto-generated <- Codemaid exclusion for now (PacketIndex Order is important for maintenance)
using OpenNos.Core;

namespace OpenNos.GameObject.Packets.ClientPackets
{
    [PacketHeader("NoS0575")]
    public class LoginPacket : PacketDefinition
    {
        #region Properties

        [PacketIndex(0)]
        public int Number { get; set; }

        [PacketIndex(1)]
        public string Name { get; set; }

        [PacketIndex(2)]
        public string Password { get; set; }

        [PacketIndex(4)]
        public string ClientData { get; set; }

        #endregion
    }
}
