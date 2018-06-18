using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace KingOfTheHill
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class Core : MySessionComponentBase
    {
        public const string Keyword = "/koth";
        public const string DisplayName = "KotH";
        public const string Filename = "KingOfTheHill.cfg";
        public const ushort ModId = 42511;

        private static Dictionary<long, ScoreDescription> Scores = new Dictionary<long, ScoreDescription>();
        private static List<ZoneBlock> Zones = new List<ZoneBlock>();
        private ICommunicate communications;

        public static void RegisterZone(ZoneBlock zone)
        {
            Zones.Add(zone);
        }

        public static void UnRegisterZone(ZoneBlock zone)
        {
            Zones.Remove(zone);
        }

        public static bool IsAllowedSpecialOperations(ulong steamId)
        {
            return IsAllowedSpecialOperations(MyAPIGateway.Session.GetUserPromoteLevel(steamId));
        }

        public static bool IsAllowedSpecialOperations(MyPromoteLevel level)
        {
            return level == MyPromoteLevel.SpaceMaster || level == MyPromoteLevel.Admin || level == MyPromoteLevel.Owner;
        }

        private static Info GenerateInfoForClient()
        {
            Info info = new Info();
            foreach (ScoreDescription score in Scores.Values)
            {
                info.Scores.Add(score);
            }

            foreach (ZoneBlock zone in Zones)
            {
                info.Zones.Add(zone.GetZone());
            }

            return info;
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            Logger.Log(MyLogSeverity.Info, "Initializing");

            if (MyAPIGateway.Session.IsServer)
            {
                communications = new Server(ModId);
                communications.OnCommandRecived += RecivedMessageFromClient;
                ZoneBlock.OnAwardPoints += AwardPoints;
            }
            else
            {
                communications = new Client(ModId, Keyword);
                communications.OnCommandRecived += RecivedMessageFromServer;
                communications.OnUserInput += UserInput;
            }
        }

        public override void BeforeStart()
        {
            MyAPIGateway.Session.OnSessionReady += SessionReady;
        }

        public override void LoadData()
        {
            if (!MyAPIGateway.Session.IsServer) return;

            try
            {
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(Filename, typeof(Info)))
                {
                    Logger.Log(MyLogSeverity.Info, "Loading saved settings");
                    TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(Filename, typeof(Info));
                    string text = reader.ReadToEnd();
                    reader.Close();

                    LoadInfo(MyAPIGateway.Utilities.SerializeFromXML<Info>(text));              
                }
                else
                {
                    Logger.Log(MyLogSeverity.Info, "Config file not found. Loading default settings");
                    SaveData();
                }
            }
            catch (Exception e)
            {
                Logger.Log(MyLogSeverity.Warning, $"Failed to load saved configuration. Loading defaults\n {e.ToString()}");
                SaveData();
            }
        }

        public override void SaveData()
        {
            if (!MyAPIGateway.Session.IsServer) return;

            try
            {
                Logger.Log(MyLogSeverity.Info, "Saving Settings");
                TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(Filename, typeof(Info));
                writer.Write(MyAPIGateway.Utilities.SerializeToXML(GenerateInfoForClient()));
                writer.Close();
            }
            catch (Exception e)
            {
                Logger.Log(MyLogSeverity.Error, $"Failed to save settings\n{e.ToString()}");
            }

        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Session.IsServer)
            {
                communications.OnCommandRecived -= RecivedMessageFromClient;
                ZoneBlock.OnAwardPoints -= AwardPoints;
            }
            else
            {
                communications.OnCommandRecived -= RecivedMessageFromServer;
                communications.OnUserInput -= UserInput;
            }
        }

        private void AwardPoints(ZoneBlock zone, IMyFaction faction, int enemies)
        {
            if (MyAPIGateway.Session.IsServer)
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

                int points = (((total - current) / total) * 5 * enemies) + 1 + enemies;

                Scores[facId].Points += points;
                SendUpdateToClients($"{faction.Name} Scored {points} Points!");
                SaveData();
            }
        }

        private int GetTotalScore()
        {
            int total = 0;
            foreach (ScoreDescription s in Scores.Values) total += s.Points;
            return total;
        }

        private void SessionReady()
        {
            MyAPIGateway.Session.OnSessionReady -= SessionReady;
            RequestServerUpdate();
        }

        private void RequestServerUpdate()
        {
            if (!communications.IsServer)
            {
                communications.SendCommand($"update");
            }
        }

        private void SendUpdateToClients(string message = null)
        {
            if (communications.IsServer)
            {
                communications.SendCommand(new Command()
                {
                    Message = message,
                    DataDump = GenerateInfoForClient()
                });
            }
        }

        private void LoadInfo(Info info)
        {
            foreach (ScoreDescription score in info.Scores)
            {
                if (!Scores.Keys.Contains(score.FactionId))
                {
                    Scores.Add(score.FactionId, score);
                }
                else
                {
                    Scores[score.FactionId] = score;
                }
            }

            foreach (ZoneDescription zoneDef in info.Zones)
            {
                ZoneBlock zone = Zones.Find((z) => z.ModBlock.CubeGrid.EntityId == zoneDef.GridId && z.ModBlock.EntityId == zoneDef.BlockId);

                if (zone != null)
                {
                    zone.SetZone(zoneDef);
                }
                else
                {
                    Logger.Log(MyLogSeverity.Error, "Failed to sync zone");
                }
            }
        }

        private void RecivedMessageFromServer(Command cmd)
        {
            if (cmd.Message != null)
            {
                MyAPIGateway.Utilities.ShowMessage(DisplayName, cmd.Message);
            }

            if (cmd.DataDump != null)
            {
                LoadInfo(cmd.DataDump as Info);
            }

            //Logger.Log(VRage.Utils.MyLogSeverity.Info, $"Scores updated: {Scores.Count}");
        }

        private void RecivedMessageFromClient(Command cmd)
        {
            if (cmd.Message == "update")
            {
                SendUpdateToClients();
            }
            else if (cmd.Message == "save")
            {
                if (IsAllowedSpecialOperations(cmd.SteamId))
                {
                    SaveData();
                    communications.SendCommand(new Command() { Message = "Score and progress saved to disk" }, cmd.SteamId);
                }
                else
                {
                    communications.SendCommand(new Command() { Message = "Save requires Admin rights" }, cmd.SteamId);
                }
            }
            else
            {
                communications.SendCommand(new Command() { Message = "Invalid Command" }, cmd.SteamId);
            }
            //else if (cmd.Message.StartsWith("add"))
        }

        private void UserInput(string content)
        {
            if (content == "update")
            {
                RequestServerUpdate();
            }
            else if (content == "score")
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
