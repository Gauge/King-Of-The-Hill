using Sandbox.ModAPI;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using KingOfTheHill.Descriptions;
using SENetworkAPI;
using System;
using VRage.ModAPI;

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

		public static List<IMyCubeGrid> StaticGrids = new List<IMyCubeGrid>();

		private NetworkAPI Network => NetworkAPI.Instance;

		public static void RegisterZone(ZoneBlock zone)
		{
			Zones.Add(zone);
		}

		public static void UnRegisterZone(ZoneBlock zone)
		{
			Zones.Remove(zone);
		}

		private void EntityAdded(IMyEntity e)
		{
			IMyCubeGrid g = e as IMyCubeGrid;
			if (g == null || !g.IsStatic)
				return;

			StaticGrids.Add(g);
		}

		private void EntityRemoved(IMyEntity e)
		{
			IMyCubeGrid g = e as IMyCubeGrid;
			if (g == null || !g.IsStatic)
				return;

			if (StaticGrids.Contains(g))
			{
				StaticGrids.Remove(g);
			}
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

			if (!MyAPIGateway.Session.IsServer)
			{
				Network.RegisterChatCommand("score", (args) => Network.SendCommand("score"));
				Network.RegisterChatCommand("save", (args) => Network.SendCommand("save"));
				Network.RegisterChatCommand("force-load", (args) => Network.SendCommand("force-load"));

				Network.RegisterNetworkCommand("score", ClientCallback_Score);
			}
			else
			{
				ZoneBlock.OnAwardPoints += AwardPoints;
				ZoneBlock.OnPlayerDied += PlayerDied;

				Network.RegisterChatCommand("score", (args) => { MyAPIGateway.Utilities.ShowMissionScreen(Network.ModName, "King of the Hill", "", FormatScores()); });
				Network.RegisterChatCommand("save", (args) => { ServerCallback_Save(MyAPIGateway.Session.Player.SteamUserId, "save", null, DateTime.Now); });
				Network.RegisterChatCommand("force-load", (args) => { ServerCallback_ForceLoad(MyAPIGateway.Session.Player.SteamUserId, "force_load", null, DateTime.Now); });

				Network.RegisterNetworkCommand("score", ServerCallback_Score);
				Network.RegisterNetworkCommand("save", ServerCallback_Save);
				Network.RegisterNetworkCommand("force_load", ServerCallback_ForceLoad);
			}

			MyAPIGateway.Entities.OnEntityAdd += EntityAdded;
			MyAPIGateway.Entities.OnEntityRemove += EntityRemoved;
		}

		public override void LoadData()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			Scores.Clear();
			Session session = Descriptions.Session.Load();
			foreach (ScoreDescription score in session.Scores)
			{
				Scores.Add(score.FactionId, score);
			}
		}

		public override void SaveData()
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			Session session = new Session();
			foreach (ScoreDescription score in Scores.Values)
			{
				session.Scores.Add(score);
			}
			Descriptions.Session.Save(session);

			foreach (ZoneBlock b in Zones)
			{
				b.Save();
			}
		}

		protected override void UnloadData()
		{
			ZoneBlock.OnAwardPoints -= AwardPoints;
			ZoneBlock.OnPlayerDied -= PlayerDied;
		}

		private void AwardPoints(ZoneBlock zone, IMyFaction faction, int enemies)
		{
			if (!MyAPIGateway.Multiplayer.IsServer)
				return;

			long facId = faction.FactionId;
			if (!Scores.Keys.Contains(faction.FactionId))
			{
				Scores.Add(facId, new ScoreDescription() {
					FactionId = facId,
					FactionName = faction.Name,
					FactionTag = faction.Tag,
					Points = 1
				});
			}

			int total = GetTotalScore();
			int current = Scores[facId].Points;

			int points;
			if (zone.PointsOnCap.Value == 0)
			{
				points = (int)(((float)(total - current) / (float)total) * 5f * enemies) + 1 + enemies;
			}
			else
			{
				points = zone.PointsOnCap.Value;
			}

			Scores[facId].Points += points;

			string message = "";
			if (zone.AwardPointsAsCredits.Value)
			{
				faction.RequestChangeBalance(points * zone.CreditsPerPoint.Value);
				message = $"{faction.Name} Scored {points} Points! ({points * zone.CreditsPerPoint.Value} credits)";
			}
			else
			{
				message = $"{faction.Name} Scored {points} Points!";
			}

			SaveData();

			MyAPIGateway.Utilities.SendModMessage(Tools.ModMessageId, $"KotH: {message}");
			Network.Say(message);
		}

		private void PlayerDied(ZoneBlock zone, IMyPlayer player, IMyFaction faction)
		{
			if (zone.PointsRemovedOnDeath.Value == 0)
				return;

			if (MyAPIGateway.Multiplayer.IsServer)
			{
				long facId = faction.FactionId;
				if (!Scores.Keys.Contains(faction.FactionId))
				{
					Scores.Add(facId, new ScoreDescription() {
						FactionId = facId,
						FactionName = faction.Name,
						FactionTag = faction.Tag,
						Points = 1
					});
				}

				int original = Scores[facId].Points;
				if (original - zone.PointsRemovedOnDeath.Value < 1)
				{
					Scores[facId].Points = 1;
				}
				else
				{
					Scores[facId].Points = original - zone.PointsRemovedOnDeath.Value;
				}

				string message = $"[{faction.Tag}] {player.DisplayName} Died: -{Scores[facId].Points-original} Points";

				MyAPIGateway.Utilities.SendModMessage(Tools.ModMessageId, $"KotH: {message}");
				Network.Say(message);
			}
		}

		private int GetTotalScore()
		{
			int total = 0;
			foreach (ScoreDescription s in Scores.Values)
				total += s.Points;
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

		#region Network Communication

		private void Chat_Help(string args)
		{
			MyAPIGateway.Utilities.ShowMessage(Network.ModName, "\nSCORE: Displays the current score\nSAVE: saves the current state to disk");
		}

		private void ClientCallback_Score(ulong steamId, string commandString, byte[] data, DateTime timestamp)
		{
			MyAPIGateway.Utilities.ShowMissionScreen(DisplayName, "King of the Hill", "", ASCIIEncoding.ASCII.GetString(data));
		}

		private void ServerCallback_Score(ulong steamId, string commandString, byte[] data, DateTime timestamp)
		{
			StringBuilder formatedScores = new StringBuilder();
			foreach (ScoreDescription score in Scores.Values)
			{
				formatedScores.AppendLine($"[{score.FactionTag}] {score.FactionName}: {score.Points}");
			}

			Network.SendCommand("score", data: ASCIIEncoding.ASCII.GetBytes(FormatScores()), steamId: steamId);
		}

		private void ServerCallback_Save(ulong steamId, string commandString, byte[] data, DateTime timestamp)
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

		private void ServerCallback_ForceLoad(ulong steamId, string commandString, byte[] data, DateTime timestamp)
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
