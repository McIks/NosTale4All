using OpenNos.SCS.Communication.ScsServices.Service;
using OpenNos.Data;
using OpenNos.Master.Library.Data;
using System.Collections.Generic;

namespace OpenNos.Master.Library.Interface
{
    [ScsService(Version = "1.1.0.0")]
    public interface IMallService
    {
        /// <summary>
        /// Authenticates a Client to the Service
        /// </summary>
        /// <param name="authKey">The private Authentication key</param>
        /// <returns>true if successful, else false</returns>
        bool Authenticate(string authKey);

        /// <summary>
        /// Checks if the given Credentials are Valid
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="passHash"></param>
        /// <returns></returns>
        AccountDTO ValidateAccount(string userName, string passHash);

        /// <summary>
        /// Get a List of all Characters associated with the given Account
        /// </summary>
        /// <param name="accountId"></param>
        /// <returns></returns>
        IEnumerable<CharacterDTO> GetCharacters(long accountId);

        /// <summary>
        /// Deliver Item to the Purchaser
        /// </summary>
        /// <param name="item"></param>
        void SendItem(long characterId, MallItem item);

        /// <summary>
        /// Deliver StaticBonus to the Purchaser
        /// </summary>
        /// <param name="item"></param>
        void SendStaticBonus(long characterId, MallStaticBonus item);
    }
}