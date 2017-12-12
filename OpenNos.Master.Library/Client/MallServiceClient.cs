/*
 * This file is part of the OpenNos Emulator Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 */

using OpenNos.Core;
using OpenNos.Master.Library.Data;
using OpenNos.Master.Library.Interface;
using OpenNos.SCS.Communication.Scs.Communication;
using OpenNos.SCS.Communication.Scs.Communication.EndPoints.Tcp;
using OpenNos.SCS.Communication.ScsServices.Client;
using System;
using System.Configuration;
using OpenNos.Data;
using System.Collections.Generic;

namespace OpenNos.Master.Library.Client
{
    public class MallServiceClient : IMallService
    {
        #region Members

        private static MallServiceClient _instance;

        private readonly IScsServiceClient<IMallService> _client;

        #endregion

        #region Instantiation

        public MallServiceClient()
        {
            string ip = ConfigurationManager.AppSettings["MasterIP"];
            int port = Convert.ToInt32(ConfigurationManager.AppSettings["MasterPort"]);
            _client = ScsServiceClientBuilder.CreateClient<IMallService>(new ScsTcpEndPoint(ip, port));
            System.Threading.Thread.Sleep(5000);
            while (_client.CommunicationState != CommunicationStates.Connected)
            {
                try
                {
                    _client.Connect();
                }
                catch
                {
                    Logger.Error(Language.Instance.GetMessageFromKey("RETRY_CONNECTION"), memberName: "MallServiceClient");
                    System.Threading.Thread.Sleep(1000);
                }
            }
        }

        #endregion

        #region Properties

        public static MallServiceClient Instance => _instance ?? (_instance = new MallServiceClient());

        public CommunicationStates CommunicationState => _client.CommunicationState;

        #endregion

        #region Methods

        public bool Authenticate(string authKey) => _client.ServiceProxy.Authenticate(authKey);

        public AccountDTO ValidateAccount(string userName, string passHash) => _client.ServiceProxy.ValidateAccount(userName, passHash);

        public IEnumerable<CharacterDTO> GetCharacters(long accountId) => _client.ServiceProxy.GetCharacters(accountId);

        public void SendItem(long characterId, MallItem item) => _client.ServiceProxy.SendItem(characterId, item);

        public void SendStaticBonus(long characterId, MallStaticBonus item) => _client.ServiceProxy.SendStaticBonus(characterId, item);

        #endregion
    }
}