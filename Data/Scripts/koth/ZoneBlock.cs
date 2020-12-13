using KingOfTheHill.Descriptions;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace KingOfTheHill
{
	public enum ZoneStates { Active, Idle, Contested }
	public enum Days { EveryDay, Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday }


	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, "ZoneBlock")]
	public class ZoneBlock : MyGameLogicComponent
	{
		public ZoneStates State { get; private set; } = ZoneStates.Idle;

		public IMyBeacon ModBlock { get; private set; }

		private IMyFaction controlledByFaction = null;
		public IMyFaction ControlledByFaction
		{
			get { return controlledByFaction; }
			set
			{
				controlledByFaction = value;

				if (ControlledBy != null)
				{
					ControlledBy.Value = (value == null) ? 0 : value.FactionId;
				}
			}

		}

		public NetSync<float> Progress;
		public NetSync<float> ProgressWhenComplete;
		public NetSync<long> ControlledBy;
		public NetSync<float> Radius;

		public NetSync<int> ActivateOnPlayerCount;
		public NetSync<bool> ActivateOnCharacter;
		public NetSync<bool> ActivateOnSmallGrid;
		public NetSync<bool> ActivateOnLargeGrid;
		public NetSync<bool> ActivateOnUnpoweredGrid;
		public NetSync<bool> IgnoreCopilot;

		public NetSync<bool> AwardPointsToAllActiveFactions;
		public NetSync<bool> AwardPointsAsCredits;
		public NetSync<int> CreditsPerPoint;
		public NetSync<int> PointsRemovedOnDeath;

		public NetSync<int> MinSmallGridBlockCount;
		public NetSync<int> MinLargeGridBlockCount;

		public NetSync<float> IdleDrainRate;
		public NetSync<float> ContestedDrainRate;
		public NetSync<bool> FlatProgressRate;
		public NetSync<float> ActiveProgressRate;

		public NetSync<bool> AutomaticActivation;
		public NetSync<int> ActivationDay;
		public NetSync<int> ActivationStartTime;
		public NetSync<int> ActivationEndTime;

		public NetSync<float> Opacity;


		/// <summary>
		/// Signal for points to be awarded
		/// </summary>
		public static event Action<ZoneBlock, IMyFaction, int> OnAwardPoints = delegate { };

		public static event Action<ZoneBlock, IMyPlayer, IMyFaction> OnPlayerDied = delegate { };

		private Dictionary<long, int> ActiveEnemiesPerFaction = new Dictionary<long, int>();
		private bool IsInitialized = false;
		private int lastPlayerCount = 0;
		private ZoneStates lastState = ZoneStates.Idle;

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
			ModBlock = Entity as IMyBeacon;

			ZoneDescription desc = ZoneDescription.GetDefaultSettings();

			if (MyAPIGateway.Session.IsServer)
			{
				ZoneDescription temp = ZoneDescription.Load(Entity);

				if (desc == null)
				{
					Tools.Log(MyLogSeverity.Warning, $"The data saved for {Entity.EntityId} returned null. loading defaults");
				}
				else
				{
					desc = temp;
				}
			}

			Progress = new NetSync<float>(this, TransferType.ServerToClient, desc.Progress);
			ProgressWhenComplete = new NetSync<float>(this, TransferType.Both, desc.ProgressWhenComplete);
			ControlledBy = new NetSync<long>(this, TransferType.ServerToClient, desc.ControlledBy);
			Radius = new NetSync<float>(this, TransferType.Both, desc.Radius);
			ActivateOnPlayerCount = new NetSync<int>(this, TransferType.Both, desc.ActivateOnPlayerCount);
			ActivateOnCharacter = new NetSync<bool>(this, TransferType.Both, desc.ActivateOnCharacter);
			ActivateOnSmallGrid = new NetSync<bool>(this, TransferType.Both, desc.ActivateOnSmallGrid);
			ActivateOnLargeGrid = new NetSync<bool>(this, TransferType.Both, desc.ActivateOnLargeGrid);
			ActivateOnUnpoweredGrid = new NetSync<bool>(this, TransferType.Both, desc.ActivateOnUnpoweredGrid);
			IgnoreCopilot = new NetSync<bool>(this, TransferType.Both, desc.IgnoreCopilot);
			AwardPointsToAllActiveFactions = new NetSync<bool>(this, TransferType.Both, desc.AwardPointsToAllActiveFactions);
			AwardPointsAsCredits = new NetSync<bool>(this, TransferType.Both, desc.AwardPointsAsCredits);
			CreditsPerPoint = new NetSync<int>(this, TransferType.Both, desc.CreditsPerPoint);
			PointsRemovedOnDeath = new NetSync<int>(this, TransferType.Both, desc.PointsRemovedOnDeath);
			MinSmallGridBlockCount = new NetSync<int>(this, TransferType.Both, desc.MinSmallGridBlockCount);
			MinLargeGridBlockCount = new NetSync<int>(this, TransferType.Both, desc.MinLargeGridBlockCount);
			IdleDrainRate = new NetSync<float>(this, TransferType.Both, desc.IdleDrainRate);
			ContestedDrainRate = new NetSync<float>(this, TransferType.Both, desc.ContestedDrainRate);
			FlatProgressRate = new NetSync<bool>(this, TransferType.Both, desc.FlatProgressRate);
			ActiveProgressRate = new NetSync<float>(this, TransferType.Both, desc.ActiveProgressRate);
			AutomaticActivation = new NetSync<bool>(this, TransferType.Both, desc.AutomaticActivation);
			ActivationDay = new NetSync<int>(this, TransferType.Both, desc.ActivationDay);
			ActivationStartTime = new NetSync<int>(this, TransferType.Both, desc.ActivationStartTime);
			ActivationEndTime = new NetSync<int>(this, TransferType.Both, desc.ActivationEndTime);
			Opacity = new NetSync<float>(this, TransferType.Both, desc.ActiveProgressRate);

			Core.RegisterZone(this);
		}

		public override MyObjectBuilder_ComponentBase Serialize(bool copy = false)
		{
			Save();
			return base.Serialize(copy);
		}

		public override void Close()
		{
			Core.UnRegisterZone(this);
		}

		public void Save()
		{
			ZoneDescription desc = new ZoneDescription();

			desc.GridId = ModBlock.CubeGrid.EntityId;
			desc.BlockId = ModBlock.EntityId;

			desc.Progress = Progress.Value;
			desc.ProgressWhenComplete = ProgressWhenComplete.Value;
			desc.ControlledBy = ControlledBy.Value;
			desc.Radius = Radius.Value;
			desc.ActivateOnPlayerCount = ActivateOnPlayerCount.Value;
			desc.ActivateOnCharacter = ActivateOnCharacter.Value;
			desc.ActivateOnSmallGrid = ActivateOnSmallGrid.Value;
			desc.ActivateOnLargeGrid = ActivateOnLargeGrid.Value;
			desc.ActivateOnUnpoweredGrid = ActivateOnUnpoweredGrid.Value;
			desc.IgnoreCopilot = IgnoreCopilot.Value;
			desc.AwardPointsToAllActiveFactions = AwardPointsToAllActiveFactions.Value;
			desc.AwardPointsAsCredits = AwardPointsAsCredits.Value;
			desc.CreditsPerPoint = CreditsPerPoint.Value;
			desc.PointsRemovedOnDeath = PointsRemovedOnDeath.Value;
			desc.MinSmallGridBlockCount = MinSmallGridBlockCount.Value;
			desc.MinLargeGridBlockCount = MinLargeGridBlockCount.Value;
			desc.IdleDrainRate = IdleDrainRate.Value;
			desc.ContestedDrainRate = ContestedDrainRate.Value;
			desc.FlatProgressRate = FlatProgressRate.Value;
			desc.ActiveProgressRate = ActiveProgressRate.Value;
			desc.AutomaticActivation = AutomaticActivation.Value;
			desc.ActivationDay = ActivationDay.Value;
			desc.ActivationStartTime = ActivationStartTime.Value;
			desc.ActivationEndTime = ActivationEndTime.Value;
			desc.Opacity = Opacity.Value;

			desc.Save(Entity);
		}

		private List<IMySlimBlock> temp = new List<IMySlimBlock>();
		public override void UpdateBeforeSimulation()
		{
			try
			{
				if (!IsInitialized)
				{
					CreateControls();
					IsInitialized = true;
				}

				if (!ModBlock.IsFunctional || !ModBlock.Enabled || !ModBlock.IsWorking)
					return; // if the block is incomplete or turned off

				if (AutomaticActivation.Value)
				{
					DateTime today = DateTime.UtcNow;

					if (ActivationDay.Value != 0 && (ActivationDay.Value - 1) != (int)today.DayOfWeek)
					{
						int hours = (int)Math.Floor(ActivationStartTime.Value / 60d);
						int minutes = ActivationStartTime.Value % 60;

						ModBlock.CustomName = $"Activates on {(Days)ActivationDay.Value} at {(hours.ToString().Length == 1 ? "0" + hours.ToString() : hours.ToString())}:{(minutes.ToString().Length == 1 ? "0" + minutes.ToString() : minutes.ToString())} UTC";
						Progress.SetValue(0);
						return;
					}

					int currentTime = today.Hour * 60 + today.Minute;

					if (currentTime < ActivationStartTime.Value)
					{
						int startTime = ActivationStartTime.Value - currentTime;
						int hours = (int)Math.Floor(startTime / 60d);
						int minutes = startTime % 60;

						ModBlock.CustomName = $"Activates in {((hours == 0) ? "" : $"{hours}h ")}{minutes}m";
						Progress.SetValue(0);
						return;
					}

					if (currentTime >= ActivationEndTime.Value)
					{
						if (ActivationDay.Value != 0)
						{
							int hours = (int)Math.Floor(ActivationStartTime.Value / 60d);
							int minutes = ActivationStartTime.Value % 60;

							ModBlock.CustomName = $"Activates next {(Days)ActivationDay.Value} at {(hours.ToString().Length == 1 ? "0" + hours.ToString() : hours.ToString())}:{(minutes.ToString().Length == 1 ? "0" + minutes.ToString() : minutes.ToString())} UTC";
							Progress.SetValue(0);
							return;
						}
						else
						{
							int nextStart = 1440 + ActivationStartTime.Value - currentTime;
							int hours = (int)Math.Floor(nextStart / 60d);
							int minutes = nextStart % 60;

							ModBlock.CustomName = $"Activates in {((hours == 0) ? "" : $"{hours}h ")}{minutes}m";
							Progress.SetValue(0);
							return;
						}
					}
				}

				MatrixD matrix = Entity.WorldMatrix;
				Vector3D location = matrix.Translation;

				IMyPlayer localPlayer = MyAPIGateway.Session.LocalHumanPlayer;

				List<IMyPlayer> players = new List<IMyPlayer>();
				List<IMyPlayer> playersInZone = new List<IMyPlayer>();
				List<IMyFaction> factionsInZone = new List<IMyFaction>();

				List<IMyPlayer> validatedPlayers = new List<IMyPlayer>(); // players that meet activation criteria
				Dictionary<long, int> validatedPlayerCountByFaction = new Dictionary<long, int>();
				List<IMyCubeGrid> validatedGrids = new List<IMyCubeGrid>();
				IMyFaction nominatedFaction = null;

				MyAPIGateway.Players.GetPlayers(players);

				foreach (IMyPlayer p in players)
				{
					if (p.Character != null)
					{
						p.Character.CharacterDied -= Died;
					}

					if (Vector3D.Distance(p.GetPosition(), location) > Radius.Value)
						continue;

					playersInZone.Add(p);

					if (p.Character != null)
					{
						p.Character.CharacterDied += Died;
					}

					if (!ActivateOnCharacter.Value && !(p.Controller.ControlledEntity is IMyCubeBlock))
						continue;

					IMyFaction f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(p.IdentityId);
					if (f == null)
						continue;

					validatedPlayers.Add(p);

					if ((p.Controller.ControlledEntity is IMyCubeBlock))
					{
						IMyCubeBlock cube = (p.Controller.ControlledEntity as IMyCubeBlock);
						IMyCubeGrid grid = cube.CubeGrid;

						if (grid.IsStatic)
							continue;

						if (!ActivateOnUnpoweredGrid.Value && !cube.IsWorking)
							continue;

						if (!ActivateOnCharacter.Value)
						{
							if (grid.GridSizeEnum == MyCubeSize.Large)
							{
								if (!ActivateOnLargeGrid.Value)
								{
									validatedPlayers.Remove(p);
									continue;
								}

								int blockCount = 0;
								grid.GetBlocks(temp, (block) => { blockCount++; return false; });
								if (blockCount < MinLargeGridBlockCount.Value)
								{
									validatedPlayers.Remove(p);
									continue;
								}
							}
							else if (grid.GridSizeEnum == MyCubeSize.Small)
							{
								if (!ActivateOnSmallGrid.Value)
								{
									validatedPlayers.Remove(p);
									continue;
								}

								int blockCount = 0;
								grid.GetBlocks(temp, (block) => { blockCount++; return false; });
								if (blockCount < MinSmallGridBlockCount.Value)
								{
									validatedPlayers.Remove(p);
									continue;
								}
							}
						}

						if (IgnoreCopilot.Value)
						{
							if (validatedGrids.Contains(grid))
							{
								validatedPlayers.Remove(p);
								continue;
							}
							else
							{
								validatedGrids.Add(grid);
							}
						}
					}

					if (nominatedFaction == null)
					{
						nominatedFaction = f;
					}

					if (!ActiveEnemiesPerFaction.ContainsKey(f.FactionId))
					{
						ActiveEnemiesPerFaction.Add(f.FactionId, 0);
					}

					if (!validatedPlayerCountByFaction.ContainsKey(f.FactionId))
					{
						validatedPlayerCountByFaction.Add(f.FactionId, 1);
						factionsInZone.Add(f);
					}
					else
					{
						validatedPlayerCountByFaction[f.FactionId]++;
					}
				}

				bool isContested = false;
				for (int i = 0; i < factionsInZone.Count; i++)
				{
					for (int j = 0; j < factionsInZone.Count; j++)
					{
						if (factionsInZone[i] == factionsInZone[j])
							continue;

						if (MyAPIGateway.Session.Factions.GetRelationBetweenFactions(factionsInZone[i].FactionId, factionsInZone[j].FactionId) == MyRelationsBetweenFactions.Enemies)
						{
							isContested = true;
							break;
						}
					}
				}

				int factionCount = validatedPlayerCountByFaction.Keys.Count;
				Color color = Color.Gray;
				lastState = State;

				float speed = 0;
				if (isContested && !AwardPointsToAllActiveFactions.Value)
				{
					State = ZoneStates.Contested;
					color = Color.Orange;
					speed = -GetProgress(ContestedDrainRate.Value);
					Progress.SetValue(Progress.Value + speed);

					if (ControlledByFaction == null)
					{
						ControlledByFaction = nominatedFaction;
					}
				}
				else if (factionCount == 0)
				{
					State = ZoneStates.Idle;
					ControlledByFaction = null;
					speed = -GetProgress(IdleDrainRate.Value);
					Progress.SetValue(Progress.Value + speed);
				}
				else
				{
					State = ZoneStates.Active;
					color = Color.White;
					speed = (AwardPointsToAllActiveFactions.Value) ? 1f : GetProgress(validatedPlayers.Count);
					Progress.SetValue(Progress.Value + speed);
					ControlledByFaction = nominatedFaction;

					foreach (IMyFaction zoneFaction in factionsInZone)
					{
						int enemyCount = 0;
						foreach (IMyPlayer p in players)
						{
							IMyFaction f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(p.IdentityId);

							if (f == null || f.FactionId == zoneFaction.FactionId)
								continue;

							if (MyAPIGateway.Session.Factions.GetRelationBetweenFactions(f.FactionId, zoneFaction.FactionId) == MyRelationsBetweenFactions.Enemies)
							{
								enemyCount++;
							}
						}

						if (ActiveEnemiesPerFaction[zoneFaction.FactionId] < enemyCount)
						{
							ActiveEnemiesPerFaction[zoneFaction.FactionId] = enemyCount;
						}
					}
				}

				if (Progress.Value >= ProgressWhenComplete.Value)
				{
					if (AwardPointsToAllActiveFactions.Value)
					{
						foreach (IMyFaction faction in factionsInZone)
						{
							OnAwardPoints.Invoke(this, faction, ActiveEnemiesPerFaction[faction.FactionId]);
						}
					}
					else
					{
						OnAwardPoints.Invoke(this, ControlledByFaction, ActiveEnemiesPerFaction[ControlledByFaction.FactionId]);
					}

					ResetActiveEnemies();
					Progress.Value = 0;
				}

				if (Progress.Value <= 0)
				{
					Progress.SetValue(0);
				}

				// display info

				if (MyAPIGateway.Multiplayer.IsServer)
				{
					int percent = (int)Math.Floor((Progress.Value / ProgressWhenComplete.Value) * 100f);

					ModBlock.CustomName = $"{State.ToString().ToUpper()} - {percent}% {(State != ZoneStates.Idle ? $"[{ControlledByFaction.Tag}]" : "")}";

					if (lastState != State)
					{
						MyAPIGateway.Utilities.SendModMessage(Tools.ModMessageId, $"KotH: {ModBlock.CustomName}");
					}
				}

				if (localPlayer != null && playersInZone.Contains(localPlayer))
				{
					int allies = 0;
					int enemies = 0;
					int neutral = 0;
					foreach (IMyPlayer p in playersInZone)
					{
						if (!ActivateOnCharacter.Value && !(p.Controller.ControlledEntity is IMyCubeBlock))
							continue;

						switch (localPlayer.GetRelationTo(p.IdentityId))
						{
							case MyRelationsBetweenPlayerAndBlock.Owner:
							case MyRelationsBetweenPlayerAndBlock.FactionShare:
								allies++;
								break;
							case MyRelationsBetweenPlayerAndBlock.Neutral:
								neutral++;
								break;
							case MyRelationsBetweenPlayerAndBlock.Enemies:
							case MyRelationsBetweenPlayerAndBlock.NoOwnership:
								enemies++;
								break;
						}
					}

					string specialColor = "White";
					switch (State)
					{
						case ZoneStates.Contested:
							specialColor = "Red";
							break;
						case ZoneStates.Active:
							specialColor = "Blue";
							break;
					}
					MyAPIGateway.Utilities.ShowNotification($"Allies: {allies}  Neutral: {neutral}  Enemies: {enemies} - {State.ToString().ToUpper()}: {((Progress.Value / ProgressWhenComplete.Value) * 100).ToString("n0")}% Speed: {(speed * 100).ToString("n0")}% {(!AwardPointsToAllActiveFactions.Value ? (ControlledByFaction != null ? $"Controlled By: {ControlledByFaction.Tag}" : "") : "")}", 1, specialColor);
				}

				if (!MyAPIGateway.Utilities.IsDedicated)
				{
					color.A = (byte)Opacity.Value;
					MySimpleObjectDraw.DrawTransparentSphere(ref matrix, Radius.Value, ref color, MySimpleObjectRasterizer.Solid, 20, null, MyStringId.GetOrCompute("KothTransparency"), 0.12f, -1, null);
				}

				if (MyAPIGateway.Multiplayer.IsServer && playersInZone.Count != lastPlayerCount)
				{
					lastPlayerCount = playersInZone.Count;
				}
			}
			catch (Exception e)
			{
				Tools.Log(MyLogSeverity.Error, e.ToString());
			}
		}

		private void Died(IMyCharacter character)
		{
			List<IMyPlayer> players = new List<IMyPlayer>();
			MyAPIGateway.Players.GetPlayers(players);

			foreach (IMyPlayer p in players)
			{
				if (p.Character == character)
				{
					IMyFaction f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(p.IdentityId);
					if (f != null)
					{
						OnPlayerDied.Invoke(this, p, f);
					}

					break;
				}
			}

		}

		private float GetProgress(float progressModifier)
		{
			return ((progressModifier * progressModifier - 1f) / (progressModifier * progressModifier + (3f * progressModifier) + 1f)) + 1f;
		}

		private void ResetActiveEnemies()
		{
			Dictionary<long, int> newDict = new Dictionary<long, int>();

			foreach (long key in ActiveEnemiesPerFaction.Keys)
			{
				newDict.Add(key, 0);
			}

			ActiveEnemiesPerFaction = newDict;
		}

		private void CreateControls()
		{
			if (!MyAPIGateway.Utilities.IsDedicated)
			{
				IMyTerminalControlSlider Slider = null;
				IMyTerminalControlCheckbox Checkbox = null;

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_Radius");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { Radius.Value = value; };
				Slider.Getter = (block) => Radius.Value;
				Slider.Writer = (block, value) => value.Append($"{Math.Round(Radius.Value, 0)}m");
				Slider.Title = MyStringId.GetOrCompute("Radius");
				Slider.Tooltip = MyStringId.GetOrCompute("Capture Zone Radius");
				Slider.SetLimits(Constants.MinRadius, Constants.MaxRadius);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_ProgressWhenComplete");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { ProgressWhenComplete.Value = value; };
				Slider.Getter = (block) => ProgressWhenComplete.Value;
				Slider.Writer = (block, value) => value.Append($"{TimeSpan.FromMilliseconds((ProgressWhenComplete.Value / 60) * 1000).ToString("g").Split('.')[0]}");
				Slider.Title = MyStringId.GetOrCompute("Capture Time");
				Slider.Tooltip = MyStringId.GetOrCompute("The base capture time");
				Slider.SetLimits(Constants.MinCaptureTime, Constants.MaxCaptureTime);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_IdleDrainRate");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { IdleDrainRate.Value = value; };
				Slider.Getter = (block) => IdleDrainRate.Value;
				Slider.Writer = (block, value) => value.Append($"{Math.Round(IdleDrainRate.Value * 100, 0)}%");
				Slider.Title = MyStringId.GetOrCompute("Idle Drain Rate");
				//Slider.Tooltip = MyStringId.GetOrCompute("How quickly the ");
				Slider.SetLimits(0, 5);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_ContestedDrainRate");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { ContestedDrainRate.Value = value; };
				Slider.Getter = (block) => ContestedDrainRate.Value;
				Slider.Writer = (block, value) => value.Append($"{Math.Round(ContestedDrainRate.Value * 100, 0)}%");
				Slider.Title = MyStringId.GetOrCompute("Contested Drain Rate");
				//Slider.Tooltip = MyStringId.GetOrCompute("The minimum blocks considered an activation grid");
				Slider.SetLimits(0, 5);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_PointsRemovedOnDeath");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { PointsRemovedOnDeath.Value = (int)value; };
				Slider.Getter = (block) => PointsRemovedOnDeath.Value;
				Slider.Writer = (block, value) => value.Append($"{PointsRemovedOnDeath.Value}");
				Slider.Title = MyStringId.GetOrCompute("Points Removed On Death");
				Slider.SetLimits(0, 1000);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_ActivateOnCharacter");
				Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Checkbox.Setter = (block, value) => {
					ActivateOnCharacter.Value = value;
					UpdateControls();
				};
				Checkbox.Getter = (block) => ActivateOnCharacter.Value;
				Checkbox.Title = MyStringId.GetOrCompute("Activate On Character");
				Checkbox.Tooltip = MyStringId.GetOrCompute("Only requires a player to activate the zone");
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

				Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_ActivateOnSmallGrid");
				Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !ActivateOnCharacter.Value; };
				Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Checkbox.Setter = (block, value) => { ActivateOnSmallGrid.Value = value; };
				Checkbox.Getter = (block) => ActivateOnSmallGrid.Value;
				Checkbox.Title = MyStringId.GetOrCompute("Activate On Small Grid");
				Checkbox.Tooltip = MyStringId.GetOrCompute("Will activate if a piloted small grid is in the zone");
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

				Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_ActivateOnLargeGrid");
				Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !ActivateOnCharacter.Value; };
				Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Checkbox.Setter = (block, value) => { ActivateOnLargeGrid.Value = value; };
				Checkbox.Getter = (block) => ActivateOnLargeGrid.Value;
				Checkbox.Title = MyStringId.GetOrCompute("Activate On Large Grid");
				Checkbox.Tooltip = MyStringId.GetOrCompute("Will activate if a piloted large grid is in the zone");
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);


				Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_ActivateOnUnpoweredGrid");
				Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !ActivateOnCharacter.Value; };
				Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Checkbox.Setter = (block, value) => { ActivateOnUnpoweredGrid.Value = value; };
				Checkbox.Getter = (block) => ActivateOnUnpoweredGrid.Value;
				Checkbox.Title = MyStringId.GetOrCompute("Activate On Unpowered Grid");
				Checkbox.Tooltip = MyStringId.GetOrCompute("Will activate if piloted grid is unpowered");
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

				Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_IgnoreCopilot");
				Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Checkbox.Setter = (block, value) => { IgnoreCopilot.Value = value; };
				Checkbox.Getter = (block) => IgnoreCopilot.Value;
				Checkbox.Title = MyStringId.GetOrCompute("Ignore Copilot");
				Checkbox.Tooltip = MyStringId.GetOrCompute("Will count a copiloted grid as a single person");
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

				Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_AwardPointsToAllActiveFactions");
				Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Checkbox.Setter = (block, value) => { AwardPointsToAllActiveFactions.Value = value; UpdateControls(); };
				Checkbox.Getter = (block) => AwardPointsToAllActiveFactions.Value;
				Checkbox.Title = MyStringId.GetOrCompute("Award Points To all Active Factions");
				Checkbox.Tooltip = MyStringId.GetOrCompute("All faction in zone get points on cap. No contesting zone. Zone caps at a set rate regardless of player count");
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

				Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_AwardPointsAsCredits");
				Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Checkbox.Setter = (block, value) => { AwardPointsAsCredits.Value = value; UpdateControls(); };
				Checkbox.Getter = (block) => AwardPointsAsCredits.Value;
				Checkbox.Title = MyStringId.GetOrCompute("Award Points As Credits");
				Checkbox.Tooltip = MyStringId.GetOrCompute("Will deposit credit into the capping faction");
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_CreditPerPoint");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && AwardPointsAsCredits.Value; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { CreditsPerPoint.Value = (int)value; };
				Slider.Getter = (block) => CreditsPerPoint.Value;
				Slider.Writer = (block, value) => value.Append($"{CreditsPerPoint.Value}");
				Slider.Title = MyStringId.GetOrCompute("Credits per point");
				Slider.Tooltip = MyStringId.GetOrCompute("The number of credits per point that will be payed out to capping faction");
				Slider.SetLimits(1, 1000000);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_MinSmallGridBlockCount");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !ActivateOnCharacter.Value; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { MinSmallGridBlockCount.Value = (int)value; };
				Slider.Getter = (block) => MinSmallGridBlockCount.Value;
				Slider.Writer = (block, value) => value.Append($"{MinSmallGridBlockCount.Value} blocks");
				Slider.Title = MyStringId.GetOrCompute("SmallGrid min blocks");
				Slider.Tooltip = MyStringId.GetOrCompute("The minimum blocks considered an activation grid");
				Slider.SetLimits(Constants.MinBlockCount, Constants.MaxBlockCount);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_MinLargeGridBlockCount");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !ActivateOnCharacter.Value; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { MinLargeGridBlockCount.Value = (int)value; };
				Slider.Getter = (block) => MinLargeGridBlockCount.Value;
				Slider.Writer = (block, value) => value.Append($"{MinLargeGridBlockCount.Value} blocks");
				Slider.Title = MyStringId.GetOrCompute("LargeGrid min blocks");
				Slider.Tooltip = MyStringId.GetOrCompute("The minimum blocks considered an activation grid");
				Slider.SetLimits(Constants.MinBlockCount, Constants.MaxBlockCount);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_AutomaticActivation");
				Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Checkbox.Setter = (block, value) => { AutomaticActivation.Value = value; UpdateControls(); };
				Checkbox.Getter = (block) => AutomaticActivation.Value;
				Checkbox.Title = MyStringId.GetOrCompute("Auto Activate");
				Checkbox.Tooltip = MyStringId.GetOrCompute("Will allow activation durring a set time period");
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_ActivationDay");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && AutomaticActivation.Value; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { ActivationDay.Value = (int)value; };
				Slider.Getter = (block) => ActivationDay.Value;
				Slider.Writer = (block, value) => value.Append($"{((Days)ActivationDay.Value).ToString()}");
				Slider.Title = MyStringId.GetOrCompute("Activation Day");
				Slider.Tooltip = MyStringId.GetOrCompute("The day or days that koth will activate on");
				Slider.SetLimits(0, 7);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_ActivationStartTime");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && AutomaticActivation.Value; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { ActivationStartTime.Value = (int)value; if (ActivationStartTime.Value > ActivationEndTime.Value) { ActivationEndTime.Value = ActivationStartTime.Value; UpdateControls(); } };
				Slider.Getter = (block) => ActivationStartTime.Value;
				Slider.Writer = (block, value) => value.Append($"{(Math.Floor(ActivationStartTime.Value / 60d).ToString("n0").Length == 1 ? "0" + Math.Floor(ActivationStartTime.Value / 60d).ToString("n0") : Math.Floor(ActivationStartTime.Value / 60d).ToString("n0"))}:{((ActivationStartTime.Value % 60).ToString().Length == 1 ? "0" + (ActivationStartTime.Value % 60).ToString() : (ActivationStartTime.Value % 60).ToString())} UTC");
				Slider.Title = MyStringId.GetOrCompute("Activation Start Time");
				//Slider.Tooltip = MyStringId.GetOrCompute("");
				Slider.SetLimits(0, 1440);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_ActivationEndTime");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && AutomaticActivation.Value; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { ActivationEndTime.Value = (int)value; if (ActivationStartTime.Value > ActivationEndTime.Value) { ActivationStartTime.Value = ActivationEndTime.Value; UpdateControls(); } };
				Slider.Getter = (block) => ActivationEndTime.Value;
				Slider.Writer = (block, value) => value.Append($"{(Math.Floor(ActivationEndTime.Value / 60d).ToString("n0").Length == 1 ? "0" + Math.Floor(ActivationEndTime.Value / 60d).ToString("n0") : Math.Floor(ActivationEndTime.Value / 60d).ToString("n0"))}:{((ActivationEndTime.Value % 60).ToString().Length == 1 ? "0" + (ActivationEndTime.Value % 60).ToString() : (ActivationEndTime.Value % 60).ToString())} UTC");
				Slider.Title = MyStringId.GetOrCompute("Activation End Time");
				//Slider.Tooltip = MyStringId.GetOrCompute("");
				Slider.SetLimits(0, 1440);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

				Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_Opacity");
				Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
				Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

				Slider.Setter = (block, value) => { Opacity.Value = value; };
				Slider.Getter = (block) => Opacity.Value;
				Slider.Writer = (block, value) => value.Append($"{Opacity.Value} alpha");
				Slider.Title = MyStringId.GetOrCompute("Opacity");
				Slider.Tooltip = MyStringId.GetOrCompute("Sphere visiblility");
				Slider.SetLimits(0, 255);
				MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);
			}
		}

		private void UpdateControls()
		{
			List<IMyTerminalControl> controls = new List<IMyTerminalControl>();
			MyAPIGateway.TerminalControls.GetControls<IMyBeacon>(out controls);

			foreach (IMyTerminalControl control in controls)
			{
				control.UpdateVisual();
			}
		}
	}
}
