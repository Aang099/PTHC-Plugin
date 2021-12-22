using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.NetModules;
using Terraria.Localization;
using Terraria.Net;
using TerrariaApi.Server;
using TShockAPI;

namespace PTHC_Plugin
{

    [ApiVersion(2, 1)]
// ReSharper disable once ClassNeverInstantiated.Global
    public class PthcPlugin : TerrariaPlugin
    {
        public static PthcPlugin? Instance;
        public readonly Dictionary<int, string> AuthenticatedUsers = new Dictionary<int, string>();
        public readonly Thread CommunicatorThread;

        public readonly List<int> PendingUsers = new List<int>();
        private long _graceStartMillis = -1;

        private bool _inGrace = true;
        private long _lastSend;

        public long GraceLengthMillis = -1;

        public PthcPlugin(Main game) : base(game)
        {
            var communicator = new Communicator();
            CommunicatorThread = new Thread(communicator.Start);
            Instance = this;
            _ticks = TicksBetweenPings;
        }

        public override string Author => "Aang099";

        public override string Description => "A plugin to help manage PTHC's";

        public override string Name => "PTHC Manager";

        public override Version Version => new Version(2, 0, 0, 01);

        public override void Initialize()
        {
            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInitialize);
            ServerApi.Hooks.ServerLeave.Register(this, OnPlayerLeave);
            ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);
            ServerApi.Hooks.NetGetData.Register(this, OnNetGetData);
            ServerApi.Hooks.NetSendData.Register(this, OnNetSendData);

            GetDataHandlers.PlayerInfo += OnPlayerInfo;
            GetDataHandlers.TogglePvp += OnTogglePvP;
            GetDataHandlers.PlayerTeam += OnToggleTeam;
            GetDataHandlers.KillMe += OnPlayerDeath;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Communicator.EndConnection();
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInitialize);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnPlayerLeave);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);
                ServerApi.Hooks.NetGetData.Deregister(this, OnNetGetData);
                ServerApi.Hooks.NetSendData.Deregister(this, OnNetSendData);

                GetDataHandlers.PlayerInfo -= OnPlayerInfo;
                GetDataHandlers.TogglePvp -= OnTogglePvP;
                GetDataHandlers.PlayerTeam -= OnToggleTeam;
                GetDataHandlers.KillMe -= OnPlayerDeath;
            }

            base.Dispose(disposing);
        }

        private void OnPostInitialize(EventArgs args)
        {
            Console.WriteLine("Initializing TCP Connection with Bot.");
            CommunicatorThread.Start();
            _graceStartMillis = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _lastSend = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private void OnPlayerLeave(LeaveEventArgs args)
        {
            var playerLeft = TShock.Players[args.Who];
            PendingUsers.Remove(args.Who);
            AuthenticatedUsers.Remove(args.Who);

            if (_inGrace) return;

            var alivePlayers = 0;
            var latestAlivePlayer = playerLeft;

            foreach (var player in TShock.Players)
            {
                if (player.Dead || !player.RealPlayer) continue;

                alivePlayers++;
                latestAlivePlayer = player;
            }


            if (alivePlayers >= 2) return;
            Communicator.AnnounceWinner(latestAlivePlayer.Account.Name);
            Console.WriteLine("Winner: " + latestAlivePlayer.Name);
            shutdownServer("PTHC Ended, Winner: " + latestAlivePlayer.Name);
        }

        private void OnPlayerDeath(object sender, GetDataHandlers.KillMeEventArgs args)
        {
            var deadPlayer = args.Player;
            var deadPlayerName = deadPlayer.Account.Name;

            deadPlayer.SendMessage(
                "You have died, if grace hasn't ended you can still rejoin with a new character and keep playing",
                Color.DarkRed);
            if (_inGrace) TSPlayer.All.SendMessage(deadPlayer.Name + " has died", Color.DarkRed);
            else TSPlayer.All.SendMessage(deadPlayer.Name + " has been eliminated", Color.DarkRed);

            if (_inGrace) return;

            var alivePlayers = 0;
            var latestAlivePlayer = deadPlayer;

            foreach (var player in TShock.Players)
            {
                if (!player.RealPlayer) continue;

                if (player.Dead || player.Account.Name.Equals(deadPlayerName)) continue;
                alivePlayers++;
                latestAlivePlayer = player;
            }


            if (alivePlayers >= 2) return;
            Communicator.AnnounceWinner(latestAlivePlayer.Account.Name);
            Console.WriteLine("Winner: " + latestAlivePlayer.Name);
            shutdownServer("PTHC Ended, Winner: " + latestAlivePlayer.Name);
        }
        
        private const int TicksBetweenPings = 60 * 30;
        private const int MinimumRemainingPlayers = 3;
        
        private int _ticks;
        
        private static void Ping()
        {
            if (TShock.Players.Count(p => p.RealPlayer && !p.Dead) > MinimumRemainingPlayers) return;
            {
                TShock.Utils.Broadcast("The location of all players has been revealed on the map.", Color.Teal);
                foreach (var p in TShock.Players.Where(p => p.RealPlayer && !p.Dead))
                {
                    var packet = NetPingModule.Serialize(new Vector2(p.TileX, p.TileY));
                    NetManager.Instance.Broadcast(packet);
                }
            }
        }
        
        private void OnUpdate(EventArgs args)
        {
            if (GraceLengthMillis <= 0 || _graceStartMillis <= 0 || !_inGrace) return;
            _ticks--;
            if (_ticks <= 0) 
                Ping();
            _ticks = TicksBetweenPings;
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var timeDiff = now - _graceStartMillis;

            if (now >= _lastSend + 1000)
            {
                _lastSend = now;
                var time = TimeSpan.FromMilliseconds(GraceLengthMillis - timeDiff);

                foreach (var player in TShock.Players)
                {
                    if (!(player is {RealPlayer: true})) continue;
                    player.SendData(PacketTypes.Status,
                        $"\n \n \n \n \n \n \n \n \n \n Grace period ends:\n[c/{Color.LightGreen.Hex3()}:{time:mm\\:ss}]",
                        0, 1);
                }
            }

            // If difference between now and start is equal or larger than the length, cancel grace
            if (timeDiff < GraceLengthMillis) return;
            {
                _inGrace = false;
                var playerCount = 0;
                foreach (var player in TShock.Players)
                {
                    if (!player.RealPlayer) continue;

                    Main.player[player.Index].hostile = true;
                    NetMessage.SendData((int) PacketTypes.TogglePvp, -1, -1, null, player.Index);
                    playerCount++;
                }

                TSPlayer.All.SendMessage("Grace has ended!", Color.Red);

                if (playerCount >= 2) return;
                Communicator.AnnounceWinner("null");
                Console.WriteLine("Grace ended with 1 or no players");
                shutdownServer("Grace ended with 1 or no players");
            }
        }

        private static void OnPlayerInfo(object sender, GetDataHandlers.PlayerInfoEventArgs args)
        {
            if (args.Difficulty == 2) return;
            args.Player.Kick("You must be using a hardcore character", true, false, null, true);
            args.Handled = true;
        }

        private void OnTogglePvP(object sender, GetDataHandlers.TogglePvpEventArgs args)
        {
            var player = TShock.Players[args.PlayerId];

            Main.player[player.Index].hostile = !_inGrace;
            player.SendData(PacketTypes.TogglePvp, "", player.Index);
            args.Handled = true;
        }

        private static void OnToggleTeam(object sender, GetDataHandlers.PlayerTeamEventArgs args)
        {
            args.Player.SetTeam(args.Player.Team);
            args.Handled = true;
        }

        private void OnNetGetData(GetDataEventArgs args)
        {
            var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length));
            var playerIndex = args.Msg.whoAmI;

            switch (args.MsgID)
            {
                case PacketTypes.ConnectRequest:
                {
                    /*var version = reader.ReadString();
                    
                    var curRelease = (int)typeof(Main).GetField("curRelease").GetValue(null);
                    
                    if (version != "Terraria" + curRelease)
                    {
                        NetMessage.BootPlayer(playerIndex,
                            new NetworkText("You are using a different version than the server", NetworkText.Mode.Literal));
                        return;
                    }*/

                    if (!_inGrace)
                    {
                        NetMessage.BootPlayer(playerIndex, new NetworkText("Grace has ended", NetworkText.Mode.Literal));
                        return;
                    }

                    NetMessage.SendData((int) PacketTypes.PasswordRequired, playerIndex);
                    Netplay.Clients[playerIndex].State = -1;

                    args.Handled = true;
                    break;
                }
                case PacketTypes.PasswordSend:
                {
                    var password = reader.ReadString();

                    Console.WriteLine(password);

                    PendingUsers.Add(playerIndex);
                    Communicator.SendUserApprovalRequest(password, playerIndex);
                    args.Handled = true;
                    break;
                }
                case PacketTypes.ContinueConnecting2:
                {
                    if (AuthenticatedUsers.TryGetValue(playerIndex, out _))
                    {
                        Netplay.Clients[playerIndex].State = 2;
                        NetMessage.SendData((int) PacketTypes.WorldInfo, playerIndex);

                        // Need to move this code somewhere else, this is such a mess

                        var player = TShock.Players[playerIndex];

                        if (player == null)
                        {
                            Console.WriteLine("player null");
                            return;
                        }

                        if (!AuthenticatedUsers.TryGetValue(playerIndex, out var username))
                            player.Disconnect("Error getting discord id!");

                        var account = TShock.UserAccounts.GetUserAccountByName(username);

                        account.Group = TShock.Config.Settings.DefaultRegistrationGroupName;
                        account.UUID = player.UUID;

                        player.PlayerData = TShock.CharacterDB.GetPlayerData(player, account.ID);

                        player.Group = TShock.Groups.GetGroupByName(account.Group);
                        player.tempGroup = null;
                        player.Account = account;
                        player.IsLoggedIn = true;
                        player.IsDisabledForSSC = false;

                        if (Main.ServerSideCharacter)
                        {
                            if (player.HasPermission(Permissions.bypassssc))
                            {
                                player.PlayerData.CopyCharacter(player);
                                TShock.CharacterDB.InsertPlayerData(player);
                            }

                            Console.WriteLine("Restoring player data");
                            player.PlayerData.RestoreCharacter(player);
                        }

                        player.LoginFailsBySsi = false;

                        if (player.HasPermission(Permissions.ignorestackhackdetection))
                            player.IsDisabledForStackDetection = false;

                        if (player.HasPermission(Permissions.usebanneditem))
                            player.IsDisabledForBannedWearable = false;

                        TShock.Log.ConsoleInfo(player.Name + " authenticated successfully as user: " + account.Name + ".");
                        if (player.LoginHarassed && TShock.Config.Settings.RememberLeavePos)
                        {
                            if (TShock.RememberedPos.GetLeavePos(player.Name, player.IP) != Vector2.Zero)
                            {
                                var pos = TShock.RememberedPos.GetLeavePos(player.Name, player.IP);
                                player.Teleport((int) pos.X * 16, (int) pos.Y * 16);
                            }

                            player.LoginHarassed = false;
                        }

                        TShock.UserAccounts.SetUserAccountUUID(account, player.UUID);
                    }
                    else
                    {
                        NetMessage.BootPlayer(playerIndex,
                            new NetworkText("Error, contact staff", NetworkText.Mode.Literal));
                        Console.WriteLine("Couldn't get authenticatedUser, is this player hacking?");
                    }

                    args.Handled = true;
                    break;
                }
            }
        }

        private void OnNetSendData(SendDataEventArgs args)
        {
        }

        public void OnCommunicatorDisconnect()
        {
            Console.WriteLine("Communicator Disconnected");
        }

        private void shutdownServer(string reason)
        {
            // We don't want people joining with items from other PTHCs
            foreach (var account in TShock.UserAccounts.GetUserAccounts())
                TShock.UserAccounts.RemoveUserAccount(account);

            if (Main.ServerSideCharacter)
                foreach (var player in TShock.Players)
                    if (player is {IsLoggedIn: true, IsDisabledPendingTrashRemoval: false})
                        player.SaveServerCharacter();

            TShock.Utils.StopServer(true, reason);
        }
    }
}