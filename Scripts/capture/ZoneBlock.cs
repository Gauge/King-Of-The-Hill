using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
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
        /// <summary>
        /// the completion progress in percent (0 to 1) 
        /// </summary>
        public float Progress { get { return progress / Settings.ProgressWhenComplete; } }

        /// <summary>
        /// This identifies how progress is calculated
        /// </summary>
        public ZoneStates State { get; private set; } = ZoneStates.Idle;

        /// <summary>
        /// The faction holding the point (can be null)
        /// </summary>
        public IMyFaction ControlledBy { get; private set; } = null;

        /// <summary>
        /// Access to Keens block
        /// </summary>
        public IMyBeacon ModBlock { get; private set; }
        
        /// <summary>
        /// Signal for points to be awarded
        /// </summary>
        public static event Action<ZoneBlock, IMyFaction, int> OnAwardPoints = delegate { };


        private Dictionary<long, int> ActiveEnemiesPerFaction = new Dictionary<long, int>();
        private float progress = 0;
        private bool isInitialized = false;

        public ZoneDescription GetZone()
        {
            return new ZoneDescription()
            {
                GridId = ModBlock.CubeGrid.EntityId,
                BlockId = ModBlock.EntityId,
                Progress = progress,
                ControlledBy = (ControlledBy == null) ? 0 : ControlledBy.FactionId
            };
        }

        public void SetZone(ZoneDescription zone)
        {
            progress = zone.Progress;

            if (zone.ControlledBy != 0)
            {
                ControlledBy = MyAPIGateway.Session.Factions.TryGetFactionById(zone.ControlledBy);
            }
            else
            {
                ControlledBy = null;
            }
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            ModBlock = Entity as IMyBeacon;
            Core.RegisterZone(this);
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void Close()
        {
            Core.UnRegisterZone(this);
        }

        public override void UpdateBeforeSimulation()
        {
            if (!ModBlock.IsFunctional) return;

            try
            {
                MatrixD matrix = Entity.WorldMatrix;
                Vector3D location = matrix.Translation;
                IMyPlayer localPlayer = MyAPIGateway.Session.LocalHumanPlayer;

                List<IMyPlayer> players = new List<IMyPlayer>();
                List<IMyPlayer> playerInZone = new List<IMyPlayer>();
                List<IMyFaction> factionInZone = new List<IMyFaction>();
                Dictionary<long, int> playersInZoneByFaction = new Dictionary<long, int>();
                List<IMyCubeGrid> controlledGridsInZone = new List<IMyCubeGrid>();
                IMyFaction nominatedFaction = null;

                MyAPIGateway.Players.GetPlayers(players);

                foreach (IMyPlayer p in players)
                {
                    if (p.Controller.ControlledEntity == null) continue;

                    if (Vector3D.Distance(p.Controller.ControlledEntity.Entity.GetPosition(), location) > Settings.CaptureZoneRadius) continue;
                    playerInZone.Add(p); // ensures that every player in the zone is added

                    if (!(p.Controller.ControlledEntity is IMyCubeBlock)) continue;

                    IMyCubeGrid g = (p.Controller.ControlledEntity as IMyCubeBlock).CubeGrid;
                    IMyFaction f = MyAPIGateway.Session.Factions.TryGetPlayerFaction(p.IdentityId);
                    if (f == null) continue;

                    // assigns the first faction it sees as the possible new controlling faction
                    if (nominatedFaction == null) nominatedFaction = f;

                    if (!ActiveEnemiesPerFaction.ContainsKey(f.FactionId))
                    {
                        ActiveEnemiesPerFaction.Add(f.FactionId, 0);
                    }

                    if (!playersInZoneByFaction.ContainsKey(f.FactionId))
                    {
                        playersInZoneByFaction.Add(f.FactionId, 1);
                        factionInZone.Add(f);
                    }
                    else
                    {
                        playersInZoneByFaction[f.FactionId]++;
                    }

                    // this condition is not executed above because more than one person may be piloting a grid. I wanted to ensure that if two enemy factions got control of 1 grid it would count as contention
                    if (controlledGridsInZone.Contains(g)) continue;
                    controlledGridsInZone.Add(g);
                }

                    bool isContested = false;
                for (int i = 0; i < factionInZone.Count; i++)
                {
                    for (int j = 0; j < factionInZone.Count; j++)
                    {
                        if (factionInZone[i] == factionInZone[j]) continue;

                        if (MyAPIGateway.Session.Factions.GetRelationBetweenFactions(factionInZone[i].FactionId, factionInZone[j].FactionId) == MyRelationsBetweenFactions.Enemies)
                        {
                            isContested = true;
                            break;
                        }
                    }
                }

                int factionCount = playersInZoneByFaction.Keys.Count;
                Color color = Color.Gray;

                if (isContested)
                {
                    State = ZoneStates.Contested;
                    color = Color.Orange;
                    progress -= GetProgress(1);

                    if (ControlledBy == null)
                    {
                        ControlledBy = nominatedFaction;
                    }
                }
                else if (factionCount == 0)
                {
                    State = ZoneStates.Idle;
                    ControlledBy = null;
                    progress -= GetProgress(2);
                }
                else
                {
                    State = ZoneStates.Active;
                    color = Color.White;
                    progress += GetProgress(controlledGridsInZone.Count);
                    ControlledBy = nominatedFaction;

                    foreach (IMyFaction zoneFaction in factionInZone)
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

                if (progress >= Settings.ProgressWhenComplete)
                {
                    OnAwardPoints.Invoke(this, ControlledBy, ActiveEnemiesPerFaction[ControlledBy.FactionId]);
                    ResetActiveEnemies();
                    progress = 0;
                }

                if (progress <= 0)
                {
                    progress = 0;
                }

                // display info

                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    ModBlock.CustomName = $"{State.ToString().ToUpper()} - {(Progress * 100).ToString("g").Split('.')[0]}% {(State != ZoneStates.Idle ? $"[{ControlledBy.Tag}]" : "")}";
                }

                if (localPlayer != null && playerInZone.Contains(localPlayer))
                {
                    int allies = 0;
                    int enemies = 0;
                    int neutral = 0;
                    foreach (IMyPlayer p in playerInZone)
                    {
                        if (!(p.Controller.ControlledEntity is IMyCubeBlock)) continue;

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
                    MyAPIGateway.Utilities.ShowNotification($"Allies: {allies}  Neutral: {neutral}  Enemies {enemies} - {State.ToString().ToUpper()} - {(Progress * 100).ToString("g").Split('.')[0]}%  {(ControlledBy != null ? $"Controlled By: {ControlledBy.Tag}" : "")}", 1, specialColor);
                }

                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    color.A = 3;
                    MySimpleObjectDraw.DrawTransparentSphere(ref matrix, Settings.CaptureZoneRadius + 1.5f, ref color, MySimpleObjectRasterizer.Solid, 20, null, MyStringId.GetOrCompute("abc"), 0.12f, -1);
                }
            }
            catch
            {
                Logger.Log(MyLogSeverity.Warning, "getting the error");
            }
        }

        private float GetProgress(int progressModifier)
        {
            return ((progressModifier * progressModifier - 1) / (progressModifier * progressModifier + (3 * progressModifier) + 1)) + 1;
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
    }
}
