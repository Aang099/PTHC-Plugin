using Terraria;
using Terraria.Localization;
using TShockAPI;
using TShockAPI.DB;

namespace PTHC_Plugin
{

    internal static class Handler
    {
        public static void HandleUserApprovalResponse(string discordId, int playerIndex)
        {
            if (PthcPlugin.Instance != null && !PthcPlugin.Instance.PendingUsers.Contains(playerIndex)) return;

            if (discordId.Equals("null"))
            {
                NetMessage.BootPlayer(playerIndex, new NetworkText("Authorization denied", NetworkText.Mode.Literal));
                return;
            }

            PthcPlugin.Instance?.PendingUsers.Remove(playerIndex);
            PthcPlugin.Instance?.AuthenticatedUsers.Add(playerIndex, discordId);


            if (TShock.UserAccounts.GetUserAccountByName(discordId) == null)
            {
                var isAdmin = discordId.Equals("412770799284387850") || discordId.Equals("376522837693038593");
                var newAccount = new UserAccount(discordId, "", "",
                    isAdmin ? "superadmin" : TShock.Config.Settings.DefaultRegistrationGroupName, "", "", "");
                newAccount.CreateBCryptHash("setthisplease");

                TShock.UserAccounts.AddUserAccount(newAccount);
            }

            Netplay.Clients[playerIndex].State = 1;
            NetMessage.SendData((int) PacketTypes.ContinueConnecting, playerIndex, -1, null, playerIndex);
        }

        public static void HandleSetGraceTime(int minutes)
        {
            if (PthcPlugin.Instance != null) PthcPlugin.Instance.GraceLengthMillis = minutes * 60000;
        }
    }
}