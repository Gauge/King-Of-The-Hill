using ProtoBuf;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;

namespace KingOfTheHill.Descriptions
{
    [ProtoContract]
    public class ZoneDescription
    {
        public readonly static Guid StorageGuid = new Guid("B7AF750E-68E3-4826-BD0E-A75BF36BA3E5");

        [ProtoMember(1)]
        public long GridId { get; set; }

        [ProtoMember(2)]
        public long BlockId { get; set; }

        [ProtoMember(3)]
        public float Progress { get; set; }

        [ProtoMember(4)]
        public float ProgressWhenComplete { get; set; }

        [ProtoMember(5)]
        public long ControlledBy { get; set; } // faction currently capturing the point

        [ProtoMember(6)]
        public float Radius { get; set; }

        [ProtoMember(7)]
        public int ActivateOnPlayerCount { get; set; } // Number activators present before activation
        [ProtoMember(8)]
        public bool ActivateOnCharacter { get; set; } // Counts characters as activators
        [ProtoMember(9)]
        public bool ActivateOnSmallGrid { get; set; } // Counts piloted small grids as activators
        [ProtoMember(10)]
        public bool ActivateOnLargeGrid { get; set; } // Counts piloted large grids as activators
        [ProtoMember(11)]
        public bool ActivateOnUnpoweredGrid { get; set; } // Counts non powered grid as activators
        [ProtoMember(12)]
        public bool IgnoreCopilot { get; set; }  // Counts each crew member as an activator

        [ProtoMember(13)]
        public bool AwardPointsAsCredits { get; set; }

        [ProtoMember(14)]
        public int CreditsPerPoint { get; set; }

        [ProtoMember(15)]
        public int PointsRemovedOnDeath { get; set; }

        [ProtoMember(16)]
        public int MinSmallGridBlockCount { get; set; }
        [ProtoMember(17)]
        public int MinLargeGridBlockCount { get; set; }

        [ProtoMember(18)]
        public float IdleDrainRate { get; set; }
        [ProtoMember(19)]
        public float ContestedDrainRate { get; set; }

        [ProtoMember(20)]
        public bool FlatProgressRate { get; set; }

        [ProtoMember(21)]
        public float ActiveProgressRate { get; set; } // the flat progress rate applied

        [ProtoMember(22)]
        public bool AutomaticActivation { get; set; }

        [ProtoMember(23)]
        public int ActivationDay { get; set; }

        [ProtoMember(24)]
        public int ActivationStartTime { get; set; }

        [ProtoMember(25)]
        public int ActivationEndTime { get; set; }

        [ProtoMember(26)]
        public bool AwardPointsToAllActiveFactions { get; set; }

        [ProtoMember(27)]
        public bool EncampmentMode { get; set; }

        [ProtoMember(27)]
        public int PointsOnCap { get; set; }

        [ProtoMember(30)]
        public float Opacity { get; set; }

        public void Save(IMyEntity ent)
        {
            MyModStorageComponentBase storage = GetStorage(ent);

            if (storage.ContainsKey(StorageGuid))
            {
                storage[StorageGuid] = MyAPIGateway.Utilities.SerializeToXML(this);
            }
            else
            {
                Tools.Log(MyLogSeverity.Info, $"Saved new Data");
                storage.Add(new KeyValuePair<Guid, string>(StorageGuid, MyAPIGateway.Utilities.SerializeToXML(this)));
            }
        }

        public static ZoneDescription Load(IMyEntity ent)
        {
            MyModStorageComponentBase storage = GetStorage(ent);

            if (storage.ContainsKey(StorageGuid))
            {
                return MyAPIGateway.Utilities.SerializeFromXML<ZoneDescription>(storage[StorageGuid]);
            }
            else
            {

                Tools.Log(MyLogSeverity.Info, $"No data saved for:{ent.EntityId}. Loading Defaults");
                return GetDefaultSettings();
            }
        }

        public static ZoneDescription GetDefaultSettings()
        {
            return new ZoneDescription()
            {
                IdleDrainRate = 3,
                ContestedDrainRate = 0,
                FlatProgressRate = false,
                ActiveProgressRate = 1,

                Radius = 3000f,

                ProgressWhenComplete = 36000,
                Progress = 0,

                ActivateOnCharacter = false,
                ActivateOnSmallGrid = false,
                ActivateOnLargeGrid = true,
                ActivateOnUnpoweredGrid = false,
                IgnoreCopilot = false,

                PointsOnCap = 0,
                AwardPointsAsCredits = false,
                CreditsPerPoint = 1000,
                PointsRemovedOnDeath = 0,
                MinSmallGridBlockCount = 50,
                MinLargeGridBlockCount = 25,
                ActivateOnPlayerCount = 1,

                AutomaticActivation = false,
                ActivationDay = 3,
                ActivationStartTime = 120,
                ActivationEndTime = 1200,

                Opacity = 150
            };
        }

        public override string ToString()
        {
            return $"(ZONE) Progress: {Progress}, ControlledBy: {ControlledBy}";
        }

        public static MyModStorageComponentBase GetStorage(IMyEntity entity)
        {
            return entity.Storage ?? (entity.Storage = new MyModStorageComponent());
        }
    }
}
