using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using KingOfTheHill.Descriptions;
using ModNetworkAPI;

namespace KingOfTheHill
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Core : MySessionComponentBase
    {
        public const string Keyword = "/koth";
        public const string DisplayName = "KotH";
        public const ushort ComId = 42511;

        private static Dictionary<long, ScoreDescription> Scores = new Dictionary<long, ScoreDescription>(); // faction, score
        private static List<ZoneBlock> Zones = new List<ZoneBlock>();

        private bool IsInitilaized = false;
        private int interval = 0;

        private NetworkAPI Network => NetworkAPI.Instance;


        public static void RegisterZone(ZoneBlock zone)
        {
            Zones.Add(zone);
        }

        public static void UnRegisterZone(ZoneBlock zone)
        {
            Zones.Remove(zone);
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            Tools.Log(MyLogSeverity.Info, "Initializing");

            if (!NetworkAPI.IsInitialized)
            {
                NetworkAPI.Init(ComId, DisplayName, Keyword);
            }

            Network.RegisterChatCommand(string.Empty, Chat_Help);
            Network.RegisterChatCommand("help", Chat_Help);

            if (Network.NetworkType == NetworkTypes.Client)
            {
                Network.RegisterChatCommand("score", (args) => Network.SendCommand("score"));
                Network.RegisterChatCommand("save", (args) => Network.SendCommand("save"));
                Network.RegisterChatCommand("force-load", (args) => Network.SendCommand("force-load"));

                Network.RegisterNetworkCommand("update", ClientCallback_Update);
                Network.RegisterNetworkCommand("score", ClientCallback_Score);
                Network.RegisterNetworkCommand("sync_zone", ClientCallback_SyncZone);

            }
            else
            {
                IsInitilaized = true;
                ZoneBlock.OnAwardPoints += AwardPoints;
                ZoneBlock.OnPlayerDied += PlayerDied;

                Network.RegisterChatCommand("score", (args) => { MyAPIGateway.Utilities.ShowMissionScreen(Network.ModName, "King of the Hill", "", FormatScores()); });
                Network.RegisterChatCommand("save", (args) => { ServerCallback_Save(MyAPIGateway.Session.Player.SteamUserId, "save", null); });
                Network.RegisterChatCommand("force-load", (args) => { ServerCallback_ForceLoad(MyAPIGateway.Session.Player.SteamUserId, "force_load", null); });

                Network.RegisterNetworkCommand("update", ServerCallback_Update);
                Network.RegisterNetworkCommand("sync_zone", ServerCallback_SyncZone);
                Network.RegisterNetworkCommand("score", ServerCallback_Score);
                Network.RegisterNetworkCommand("save", ServerCallback_Save);
                Network.RegisterNetworkCommand("force_load", ServerCallback_ForceLoad);
            }


            ZoneBlock.OnUpdate += ZoneUpdate;
        }

        public override void LoadData()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;

            Scores.Clear();
            Session session = Descriptions.Session.Load();
            foreach (ScoreDescription score in session.Scores)
            {
                Scores.Add(score.FactionId, score);
            }
        }

        public override void SaveData()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;

            Session session = new Session();
            foreach (ScoreDescription score in Scores.Values)
            {
                session.Scores.Add(score);
            }
            Descriptions.Session.Save(session);

            foreach (ZoneBlock b in Zones)
            {
                b.Data.Save(b.Entity);
            }
        }

        public override void UpdateAfterSimulation()
        {
            if (!IsInitilaized)
            {
                if (interval == 300)
                {
                    Network.SendCommand("update");
                    IsInitilaized = true;
                }
                interval++;
            }
        }

        protected override void UnloadData()
        {
            Network.Close();
            ZoneBlock.OnUpdate -= ZoneUpdate;

            ZoneBlock.OnAwardPoints -= AwardPoints;
            ZoneBlock.OnPlayerDied -= PlayerDied;
        }

        private void AwardPoints(ZoneBlock zone, IMyFaction faction, int enemies)
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                long facId = faction.FactionId;
                if (!Scores.Keys.Contains(faction.FactionId))
                {
                    Scores.Add(facId, new ScoreDescription()
                    {
                        FactionId = facId,
                        FactionName = faction.Name,
                        FactionTag = faction.Tag,
                        Points = 1
                    });
                }

                int total = GetTotalScore();
                int current = Scores[facId].Points;

                int points = (int)(((float)(total - current) / (float)total) * 5 * enemies) + 1 + enemies;

                Scores[facId].Points += points;

                SaveData();

                string message = $"{faction.Name} Scored {points} Points!";

                MyAPIGateway.Utilities.SendModMessage(Tools.ModMessageId, $"KotH: {message}");
                Network.SendCommand("update", message: message, data: MyAPIGateway.Utilities.SerializeToBinary(GenerateUpdate()));
            }
        }

        private void PlayerDied(ZoneBlock zone, IMyPlayer player, IMyFaction faction)
        {
            if (zone.Data.PointsRemovedOnDeath == 0) return;

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                long facId = faction.FactionId;
                if (!Scores.Keys.Contains(faction.FactionId))
                {
                    Scores.Add(facId, new ScoreDescription()
                    {
                        FactionId = facId,
                        FactionName = faction.Name,
                        FactionTag = faction.Tag,
                        Points = 1
                    });
                }

                Scores[facId].Points -= zone.Data.PointsRemovedOnDeath;

                if (Scores[facId].Points < 1)
                {
                    Scores[facId].Points = 1;
                }

                string message = $"[{faction.Tag}] {player.DisplayName} Died: -{zone.Data.PointsRemovedOnDeath} Points";

                MyAPIGateway.Utilities.SendModMessage(Tools.ModMessageId, $"KotH: {message}");
                Network.SendCommand("update", message: message, data: MyAPIGateway.Utilities.SerializeToBinary(GenerateUpdate()));
            }
        }

        private void ZoneUpdate(ZoneBlock zone)
        {
            SaveData();
            Network.SendCommand("sync_zone", data: MyAPIGateway.Utilities.SerializeToBinary(zone.Data));
        }

        private Update GenerateUpdate()
        {
            Update value = new Update();

            foreach (ZoneBlock block in Zones)
            {
                value.Zones.Add(block.Data);
            }

            return value;
        }

        private int GetTotalScore()
        {
            int total = 0;
            foreach (ScoreDescription s in Scores.Values) total += s.Points;
            return total;
        }

        private string FormatScores()
        {
            StringBuilder formatedScores = new StringBuilder();
            foreach (ScoreDescription score in Scores.Values)
            {
                formatedScores.AppendLine($"[{score.FactionTag}] {score.FactionName}: {score.Points}");
            }
            return formatedScores.ToString();
        }

        private void RequestServerUpdate()
        {
            if (Network.NetworkType == NetworkTypes.Client)
            {
                Network.SendCommand("update");
            }
        }

        #region Network Communication

        private void Chat_Help(string args)
        {
            MyAPIGateway.Utilities.ShowMessage(Network.ModName, "\nSCORE: Displays the current score\nSAVE: saves the current state to disk");
        }

        private void ClientCallback_Update(ulong steamId, string commandString, byte[] data)
        {
            Update content = MyAPIGateway.Utilities.SerializeFromBinary<Update>(data);

            foreach (ZoneDescription zd in content.Zones)
            {
                ZoneBlock zone = Zones.Find(z => z.Entity.EntityId == zd.BlockId && z.ModBlock.CubeGrid.EntityId == zd.GridId);

                if (zone != null)
                {
                    zone.SetZone(zd);
                }
            }
        }

        private void ClientCallback_Score(ulong steamId, string commandString, byte[] data)
        {
            MyAPIGateway.Utilities.ShowMissionScreen(DisplayName, "King of the Hill", "", ASCIIEncoding.ASCII.GetString(data));
        }

        private void ClientCallback_SyncZone(ulong steamId, string commandString, byte[] data)
        {
            ZoneDescription zd = MyAPIGateway.Utilities.SerializeFromBinary<ZoneDescription>(data);

            ZoneBlock zone = Zones.Find(z => z.Entity.EntityId == zd.BlockId && z.ModBlock.CubeGrid.EntityId == zd.GridId);
            zone.SetZone(zd);
        }

        private void ServerCallback_Update(ulong steamId, string commandString, byte[] data)
        {
            Network.SendCommand("update", data: MyAPIGateway.Utilities.SerializeToBinary(GenerateUpdate()), steamId: steamId);
        }

        private void ServerCallback_SyncZone(ulong steamId, string commandString, byte[] data)
        {
            ZoneDescription zd = MyAPIGateway.Utilities.SerializeFromBinary<ZoneDescription>(data);

            ZoneBlock zone = Zones.Find(z => z.Entity.EntityId == zd.BlockId && z.ModBlock.CubeGrid.EntityId == zd.GridId);
            zone.SetZone(zd);

            Network.SendCommand("sync_zone", data: MyAPIGateway.Utilities.SerializeToBinary(zone.Data));
        }

        private void ServerCallback_Score(ulong steamId, string commandString, byte[] data)
        {
            StringBuilder formatedScores = new StringBuilder();
            foreach (ScoreDescription score in Scores.Values)
            {
                formatedScores.AppendLine($"[{score.FactionTag}] {score.FactionName}: {score.Points}");
            }

            Network.SendCommand("score", data: ASCIIEncoding.ASCII.GetBytes(FormatScores()), steamId: steamId);
        }

        private void ServerCallback_Save(ulong steamId, string commandString, byte[] data)
        {
            if (Tools.IsAllowedSpecialOperations(steamId))
            {
                SaveData();
                Network.SendCommand("blank_message", "KotH Saved.", steamId: steamId);
            }
            else
            {
                Network.SendCommand("blank_message", "Requires admin rights", steamId: steamId);
            }
        }

        private void ServerCallback_ForceLoad(ulong steamId, string commandString, byte[] data)
        {
            if (Tools.IsAllowedSpecialOperations(steamId))
            {
                LoadData();
                Network.SendCommand("blank_message", "Scores force loaded", steamId: steamId);
            }
            else
            {
                Network.SendCommand("blank_message", "Requires admin rights", steamId: steamId);
            }
        }

        #endregion
    }
}
