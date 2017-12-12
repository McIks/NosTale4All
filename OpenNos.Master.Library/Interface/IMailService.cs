using OpenNos.SCS.Communication.ScsServices.Service;
using OpenNos.Master.Library.Data;
using System;

namespace OpenNos.Master.Library.Interface
{
    [ScsService(Version = "1.1.0.0")]
    public interface IMailService
    {
        /// <summary>
        /// Authenticates a Client to the Service
        /// </summary>
        /// <param name="authKey">The private Authentication key</param>
        /// <returns>true if successful, else false</returns>
        bool Authenticate(string authKey, Guid serverId);

        /// <summary>
        /// Send Mail
        /// </summary>
        /// <param name="mail"></param>
        void SendMail(Mail mail);
    }
}