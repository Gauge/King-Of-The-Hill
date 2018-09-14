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
        public const ushort ModId = 42511;

        private static Dictionary<long, ScoreDescription> Scores = new Dictionary<long, ScoreDescription>(); // faction, score
        private static List<ZoneBlock> Zones = new List<ZoneBlock>();
        private ICommunicate coms;

        private bool IsInitilaized = false;
        private int interval = 0;

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

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                coms = new Server(ModId, Keyword);
                coms.OnCommandRecived += RecivedMessageFromClient;
                coms.OnTerminalInput += ServerInput;
                ZoneBlock.OnAwardPoints += AwardPoints;
                IsInitilaized = true;
            }
            else
            {
                coms = new Client(ModId, Keyword);
                coms.OnCommandRecived += RecivedMessageFromServer;
                coms.OnTerminalInput += ClientInput;
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
                    coms.SendCommand("update");
                    IsInitilaized = true;
                }
                interval++;
            }
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                coms.OnCommandRecived -= RecivedMessageFromClient;
                coms.OnTerminalInput -= ServerInput;
                ZoneBlock.OnAwardPoints -= AwardPoints;
            }
            else
            {
                coms.OnCommandRecived -= RecivedMessageFromServer;
                coms.OnTerminalInput -= ClientInput;
            }

            ZoneBlock.OnUpdate -= ZoneUpdate;
            coms.Close();
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
                if (coms.MultiplayerType == MultiplayerTypes.Server)
                {
                    MyAPIGateway.Utilities.ShowMessage(DisplayName, message);
                }

                coms.SendCommand(new Command { Message = message, DataType = "Update", XMLData = MyAPIGateway.Utilities.SerializeToXML(GenerateUpdate()) });
            }
        }

        private void ZoneUpdate(ZoneBlock zone)
        {
            SaveData();
            coms.SendCommand(new Command() { DataType = "ZoneDescription", XMLData = MyAPIGateway.Utilities.SerializeToXML(zone.Data) });
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

        private void RequestServerUpdate()
        {
            if (coms.MultiplayerType == MultiplayerTypes.Client)
            {
                coms.SendCommand($"update");
            }
        }

        private void RecivedMessageFromServer(Command cmd)
        {
            if (cmd.Message != null)
            {
                MyAPIGateway.Utilities.ShowMessage(DisplayName, cmd.Message);
            }

            if (cmd.DataType == "Update")
            {
                Update data = MyAPIGateway.Utilities.SerializeFromXML<Update>(cmd.XMLData);

                foreach (ZoneDescription zd in data.Zones)
                {
                    Tools.Log(MyLogSeverity.Info, $@"{zd.GridId}|{zd.BlockId} - Progress: {zd.Progress}/{zd.ProgressWhenComplete} Radius: {zd.Radius}");

                    ZoneBlock zone = Zones.Find(z => z.Entity.EntityId == zd.BlockId && z.ModBlock.CubeGrid.EntityId == zd.GridId);

                    if (zone != null)
                    {
                        zone.SetZone(zd);
                    }
                }
            }
            else if (cmd.DataType == "ZoneDescription")
            {
                ZoneDescription zd = MyAPIGateway.Utilities.SerializeFromXML<ZoneDescription>(cmd.XMLData);

                ZoneBlock zone = Zones.Find(z => z.Entity.EntityId == zd.BlockId && z.ModBlock.CubeGrid.EntityId == zd.GridId);
                zone.SetZone(zd);

            }
            else if (cmd.Arguments == "score")
            {
                MyAPIGateway.Utilities.ShowMissionScreen(DisplayName, "King of the Hill", "", cmd.XMLData.ToString());
            }
        }

        private void RecivedMessageFromClient(Command cmd)
        {
            if (cmd.Arguments == string.Empty || cmd.Arguments == "help")
            {
                coms.SendCommand(new Command() { Message = "\nSCORE: Displays the current score\nSAVE: saves the current state to disk" }, cmd.SteamId);
            }
            else if (cmd.Arguments == "update")
            {
                coms.SendCommand(new Command { DataType = "Update", XMLData = MyAPIGateway.Utilities.SerializeToXML(GenerateUpdate()) }, cmd.SteamId);
            }
            else if (cmd.DataType == "ZoneDescription")
            {
                ZoneDescription zd = MyAPIGateway.Utilities.SerializeFromXML<ZoneDescription>(cmd.XMLData);

                ZoneBlock zone = Zones.Find(z => z.Entity.EntityId == zd.BlockId && z.ModBlock.CubeGrid.EntityId == zd.GridId);
                zone.SetZone(zd);

                coms.SendCommand(cmd);
            }
            else if (cmd.Arguments == "score")
            {
                StringBuilder formatedScores = new StringBuilder();
                foreach (ScoreDescription score in Scores.Values)
                {
                    formatedScores.AppendLine($"[{score.FactionTag}] {score.FactionName}: {score.Points}");
                }

                coms.SendCommand(new Command() { Arguments = "score", XMLData = formatedScores.ToString() }, cmd.SteamId);
            }
            else if (cmd.Arguments == "save")
            {
                if (Tools.IsAllowedSpecialOperations(cmd.SteamId))
                {
                    SaveData();
                    coms.SendCommand(new Command() { Message = "KotH Saved" }, cmd.SteamId);
                }
                else
                {
                    coms.SendCommand(new Command() { Message = "Requires admin rights" }, cmd.SteamId);
                }
            }
            else if (cmd.Arguments == "force-load")
            {
                if (Tools.IsAllowedSpecialOperations(cmd.SteamId))
                {
                    LoadData();
                    coms.SendCommand(new Command() { Message = "Scores force loaded" }, cmd.SteamId);
                }
                else
                {
                    coms.SendCommand(new Command() { Message = "Requires admin rights" }, cmd.SteamId);
                }
            }
            else
            {
                coms.SendCommand(new Command() { Message = "Invalid Command" }, cmd.SteamId);
            }
        }

        private void ClientInput(string content)
        {
            coms.SendCommand(content);
        }

        private void ServerInput(string content)
        {
            if (content == "score")
            {
                StringBuilder description = new StringBuilder();
                foreach (ScoreDescription score in Scores.Values)
                {
                    description.AppendLine($"[{score.FactionTag}] {score.FactionName}: {score.Points}");
                }

                MyAPIGateway.Utilities.ShowMissionScreen(DisplayName, "King of the Hill", "", description.ToString());
            }
        }
    }
}
