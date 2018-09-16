using KingOfTheHill.Descriptions;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace KingOfTheHill
{
    public enum ZoneStates { Active, Idle, Contested }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Beacon), false, "ZoneBlock")]
    public class ZoneBlock : MyGameLogicComponent
    {
        public ZoneDescription Data { get; private set; }

        /// <summary>
        /// This identifies how progress is calculated
        /// </summary>
        public ZoneStates State { get; private set; } = ZoneStates.Idle;

        /// <summary>
        /// Access to Keens block
        /// </summary>
        public IMyBeacon ModBlock { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        private IMyFaction controlledByFaction = null;

        public IMyFaction ControlledByFaction
        {
            get { return controlledByFaction; }
            set
            {
                controlledByFaction = value;

                if (Data != null)
                {
                    Data.ControlledBy = (value == null) ? 0 : value.FactionId;
                }
            }

        }
        /// <summary>
        /// Signal for points to be awarded
        /// </summary>
        public static event Action<ZoneBlock, IMyFaction, int> OnAwardPoints = delegate { };

        public static event Action<ZoneBlock> OnUpdate = delegate { };

        private Dictionary<long, int> ActiveEnemiesPerFaction = new Dictionary<long, int>();
        private bool IsInitialized = false;
        private int LastPlayerCount = 0;
        private List<IMySlimBlock> temp = new List<IMySlimBlock>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
            ModBlock = Entity as IMyBeacon;

            Data = ZoneDescription.Load(Entity);
            if (Data == null)
            {
                Tools.Log(MyLogSeverity.Warning, $"The data saved for {Entity.EntityId} returned null. loading defaults");   
                Data = ZoneDescription.GetDefaultSettings();
            }
            Data.BlockId = ModBlock.EntityId;
            Data.GridId = ModBlock.CubeGrid.EntityId;

            Core.RegisterZone(this);
        }

        public void SetZone(ZoneDescription zone)
        {
            float progress = Data.Progress;
            long controlledBy = Data.ControlledBy;

            Data = zone;

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                Data.BlockId = ModBlock.EntityId;
                Data.GridId = ModBlock.CubeGrid.EntityId;
                Data.Progress = progress;
                Data.ControlledBy = controlledBy;
            }
        }

        public override void Close()
        {
            Core.UnRegisterZone(this);
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (!IsInitialized)
                {
                    CreateControls();
                    IsInitialized = true;
                }

                if (!ModBlock.IsFunctional || !ModBlock.Enabled || !ModBlock.IsWorking) return; // if the block is incomplete or turned off
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
                    if (Vector3D.Distance(p.GetPosition(), location) > Data.Radius) continue;
                    playersInZone.Add(p);

                    if (!Data.ActivateOnCharacter && !(p.Controller.ControlledEntity is IMyCubeBlock)) continue;

                    IMyFaction f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(p.IdentityId);
                    if (f == null) continue;

                    validatedPlayers.Add(p);

                    if ((p.Controller.ControlledEntity is IMyCubeBlock))
                    {
                        IMyCubeBlock cube = (p.Controller.ControlledEntity as IMyCubeBlock);
                        IMyCubeGrid grid = cube.CubeGrid;

                        if (grid.IsStatic) continue;

                        if (!Data.ActivateOnUnpoweredGrid && !cube.IsWorking) continue;

                        if (!Data.ActivateOnCharacter)
                        {
                            if (grid.GridSizeEnum == MyCubeSize.Large)
                            {
                                if (!Data.ActivateOnLargeGrid)
                                {
                                    validatedPlayers.Remove(p);
                                    continue;
                                }

                                int blockCount = 0;
                                grid.GetBlocks(temp, (block) => { blockCount++; return false; });
                                if (blockCount < Data.MinLargeGridBlockCount)
                                {
                                    validatedPlayers.Remove(p);
                                    continue;
                                }
                            }
                            else if (grid.GridSizeEnum == MyCubeSize.Small)
                            {
                                if (!Data.ActivateOnSmallGrid)
                                {
                                    validatedPlayers.Remove(p);
                                    continue;
                                }

                                int blockCount = 0;
                                grid.GetBlocks(temp, (block) => { blockCount++; return false; });
                                if (blockCount < Data.MinSmallGridBlockCount)
                                {
                                    validatedPlayers.Remove(p);
                                    continue;
                                }
                            }
                        }

                        if (Data.IgnoreCopilot)
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
                        if (factionsInZone[i] == factionsInZone[j]) continue;

                        if (MyAPIGateway.Session.Factions.GetRelationBetweenFactions(factionsInZone[i].FactionId, factionsInZone[j].FactionId) == MyRelationsBetweenFactions.Enemies)
                        {
                            isContested = true;
                            break;
                        }
                    }
                }

                int factionCount = validatedPlayerCountByFaction.Keys.Count;
                Color color = Color.Gray;

                float speed = 0;
                if (isContested)
                {
                    State = ZoneStates.Contested;
                    color = Color.Orange;
                    speed = -GetProgress(Data.ContestedDrainRate);
                    Data.Progress += speed;

                    if (ControlledByFaction == null)
                    {
                        ControlledByFaction = nominatedFaction;
                    }
                }
                else if (factionCount == 0)
                {
                    State = ZoneStates.Idle;
                    ControlledByFaction = null;
                    speed = -GetProgress(Data.IdleDrainRate);
                    Data.Progress += speed;
                }
                else
                {
                    State = ZoneStates.Active;
                    color = Color.White;
                    speed = GetProgress(validatedPlayers.Count);
                    Data.Progress += speed;
                    ControlledByFaction = nominatedFaction;

                    foreach (IMyFaction zoneFaction in factionsInZone)
                    {
                        int enemyCount = 0;
                        foreach (IMyPlayer p in players)
                        {
                            IMyFaction f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(p.IdentityId);

                            if (f == null || f.FactionId == zoneFaction.FactionId) continue;

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

                if (Data.Progress >= Data.ProgressWhenComplete)
                {
                    OnAwardPoints.Invoke(this, ControlledByFaction, ActiveEnemiesPerFaction[ControlledByFaction.FactionId]);
                    ResetActiveEnemies();
                    Data.Progress = 0;
                }

                if (Data.Progress <= 0)
                {
                    Data.Progress = 0;
                }

                // display info

                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    ModBlock.CustomName = $"{State.ToString().ToUpper()} - {((Data.Progress / Data.ProgressWhenComplete) * 100).ToString("g").Split('.')[0]}% {(State != ZoneStates.Idle ? $"[{ControlledByFaction.Tag}]" : "")}";
                }

                if (localPlayer != null && playersInZone.Contains(localPlayer))
                {
                    int allies = 0;
                    int enemies = 0;
                    int neutral = 0;
                    foreach (IMyPlayer p in playersInZone)
                    {
                        if (!Data.ActivateOnCharacter && !(p.Controller.ControlledEntity is IMyCubeBlock)) continue;

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
                    MyAPIGateway.Utilities.ShowNotification($"Allies: {allies}  Neutral: {neutral}  Enemies: {enemies} - {State.ToString().ToUpper()}: {((Data.Progress / Data.ProgressWhenComplete) * 100).ToString("n0")}% Speed: {speed * 100}% {(ControlledByFaction != null ? $"Controlled By: {ControlledByFaction.Tag}" : "")}", 1, specialColor);
                }

                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    color.A = 3;
                    MySimpleObjectDraw.DrawTransparentSphere(ref matrix, Data.Radius, ref color, MySimpleObjectRasterizer.Solid, 20, null, MyStringId.GetOrCompute("???"), 0.12f, -1);
                }

                if (MyAPIGateway.Multiplayer.IsServer && playersInZone.Count != LastPlayerCount)
                {
                    LastPlayerCount = playersInZone.Count;
                    OnUpdate.Invoke(this);
                }
            }
            catch (Exception e)
            {
                Tools.Log(MyLogSeverity.Error, e.ToString());
            }
        }

        private float GetProgress(float progressModifier)
        {
            return (((float)progressModifier * (float)progressModifier - 1) / ((float)progressModifier * (float)progressModifier + (3 * (float)progressModifier) + 1)) + 1;
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

                Slider.Setter = (block, value) => { Data.Radius = value; OnUpdate.Invoke(this); };
                Slider.Getter = (block) => Data.Radius;
                Slider.Writer = (block, value) => value.Append($"{Math.Round(Data.Radius, 0)}m");
                Slider.Title = MyStringId.GetOrCompute("Radius");
                Slider.Tooltip = MyStringId.GetOrCompute("Capture Zone Radius");
                Slider.SetLimits(Constants.MinRadius, Constants.MaxRadius);
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

                Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_ProgressWhenComplete");
                Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
                Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Slider.Setter = (block, value) => { Data.ProgressWhenComplete = value; OnUpdate.Invoke(this); };
                Slider.Getter = (block) => Data.ProgressWhenComplete;
                Slider.Writer = (block, value) => value.Append($"{TimeSpan.FromMilliseconds((Data.ProgressWhenComplete / 60) * 1000).ToString("g").Split('.')[0]}");
                Slider.Title = MyStringId.GetOrCompute("Capture Time");
                Slider.Tooltip = MyStringId.GetOrCompute("The base capture time");
                Slider.SetLimits(Constants.MinCaptureTime, Constants.MaxCaptureTime);
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

                Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_IdleDrainRate");
                Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
                Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Slider.Setter = (block, value) => { Data.IdleDrainRate = value; OnUpdate.Invoke(this); };
                Slider.Getter = (block) => Data.IdleDrainRate;
                Slider.Writer = (block, value) => value.Append($"{Math.Round(Data.IdleDrainRate * 100, 0)}%");
                Slider.Title = MyStringId.GetOrCompute("Idle Drain Rate");
                //Slider.Tooltip = MyStringId.GetOrCompute("How quickly the ");
                Slider.SetLimits(0, 5);
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

                Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_ContestedDrainRate");
                Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
                Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Slider.Setter = (block, value) => { Data.ContestedDrainRate = value; OnUpdate.Invoke(this); };
                Slider.Getter = (block) => Data.ContestedDrainRate;
                Slider.Writer = (block, value) => value.Append($"{Math.Round(Data.ContestedDrainRate * 100, 0)}%");
                Slider.Title = MyStringId.GetOrCompute("Contested Drain Rate");
                //Slider.Tooltip = MyStringId.GetOrCompute("The minimum blocks considered an activation grid");
                Slider.SetLimits(0, 5);
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

                Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_ActivateOnCharacter");
                Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
                Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Checkbox.Setter = (block, value) => {
                    Data.ActivateOnCharacter = value;
                    UpdateControls();
                };
                Checkbox.Getter = (block) => Data.ActivateOnCharacter;
                Checkbox.Title = MyStringId.GetOrCompute("Activate On Character");
                Checkbox.Tooltip = MyStringId.GetOrCompute("Only requires a player to activate the zone");
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

                Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_ActivateOnSmallGrid");
                Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !Data.ActivateOnCharacter; };
                Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Checkbox.Setter = (block, value) => { Data.ActivateOnSmallGrid = value; OnUpdate.Invoke(this); };
                Checkbox.Getter = (block) => Data.ActivateOnSmallGrid;
                Checkbox.Title = MyStringId.GetOrCompute("Activate On Small Grid");
                Checkbox.Tooltip = MyStringId.GetOrCompute("Will activate if a piloted small grid is in the zone");
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

                Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_ActivateOnLargeGrid");
                Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !Data.ActivateOnCharacter; };
                Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Checkbox.Setter = (block, value) => { Data.ActivateOnLargeGrid = value; OnUpdate.Invoke(this); };
                Checkbox.Getter = (block) => Data.ActivateOnLargeGrid;
                Checkbox.Title = MyStringId.GetOrCompute("Activate On Large Grid");
                Checkbox.Tooltip = MyStringId.GetOrCompute("Will activate if a piloted large grid is in the zone");
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);


                Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_ActivateOnUnpoweredGrid");
                Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !Data.ActivateOnCharacter; };
                Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Checkbox.Setter = (block, value) => { Data.ActivateOnUnpoweredGrid = value; OnUpdate.Invoke(this); };
                Checkbox.Getter = (block) => Data.ActivateOnUnpoweredGrid;
                Checkbox.Title = MyStringId.GetOrCompute("Activate On Unpowered Grid");
                Checkbox.Tooltip = MyStringId.GetOrCompute("Will activate if piloted grid is unpowered");
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

                Checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBeacon>("Zone_IgnoreCopilot");
                Checkbox.Enabled = (block) => { return block.EntityId == ModBlock.EntityId; };
                Checkbox.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Checkbox.Setter = (block, value) => { Data.IgnoreCopilot = value; OnUpdate.Invoke(this); };
                Checkbox.Getter = (block) => Data.IgnoreCopilot;
                Checkbox.Title = MyStringId.GetOrCompute("Ignore Copilot");
                Checkbox.Tooltip = MyStringId.GetOrCompute("Will count a copiloted grid as a single person");
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Checkbox);

                Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_MinSmallGridBlockCount");
                Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !Data.ActivateOnCharacter; };
                Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Slider.Setter = (block, value) => { Data.MinSmallGridBlockCount = (int)value; OnUpdate.Invoke(this); };
                Slider.Getter = (block) => Data.MinSmallGridBlockCount;
                Slider.Writer = (block, value) => value.Append($"{Data.MinSmallGridBlockCount} blocks");
                Slider.Title = MyStringId.GetOrCompute("SmallGrid min blocks");
                Slider.Tooltip = MyStringId.GetOrCompute("The minimum blocks considered an activation grid");
                Slider.SetLimits(Constants.MinBlockCount, Constants.MaxBlockCount);
                MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyBeacon>(Slider);

                Slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBeacon>("Zone_MinLargeGridBlockCount");
                Slider.Enabled = (block) => { return block.EntityId == ModBlock.EntityId && !Data.ActivateOnCharacter; };
                Slider.Visible = (block) => { return block.EntityId == ModBlock.EntityId; };

                Slider.Setter = (block, value) => { Data.MinLargeGridBlockCount = (int)value; OnUpdate.Invoke(this); };
                Slider.Getter = (block) => Data.MinLargeGridBlockCount;
                Slider.Writer = (block, value) => value.Append($"{Data.MinLargeGridBlockCount} blocks");
                Slider.Title = MyStringId.GetOrCompute("LargeGrid min blocks");
                Slider.Tooltip = MyStringId.GetOrCompute("The minimum blocks considered an activation grid");
                Slider.SetLimits(Constants.MinBlockCount, Constants.MaxBlockCount);
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
