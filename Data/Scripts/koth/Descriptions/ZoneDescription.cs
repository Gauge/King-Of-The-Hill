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

        [ProtoMember]
        public long GridId { get; set; }

        [ProtoMember]
        public long BlockId { get; set; }

        [ProtoMember]
        public float Progress { get; set; }

        [ProtoMember]
        public float ProgressWhenComplete { get; set; }

        [ProtoMember]
        public long ControlledBy { get; set; } // faction currently capturing the point

        [ProtoMember]
        public float Radius { get; set; }

        [ProtoMember]
        public int ActivateOnPlayerCount { get; set; } // Number activators present before activation
        [ProtoMember]
        public bool ActivateOnCharacter { get; set; } // Counts characters as activators
        [ProtoMember]
        public bool ActivateOnSmallGrid { get; set; } // Counts piloted small grids as activators
        [ProtoMember]
        public bool ActivateOnLargeGrid { get; set; } // Counts piloted large grids as activators
        [ProtoMember]
        public bool ActivateOnUnpoweredGrid { get; set; } // Counts non powered grid as activators
        [ProtoMember]
        public bool IgnoreCopilot { get; set; }  // Counts each crew member as an activator

        [ProtoMember]
        public bool AwardPointsAsCredits { get; set; }

        [ProtoMember]
        public int PointsRemovedOnDeath { get; set; }

        [ProtoMember]
        public int MinSmallGridBlockCount { get; set; }
        [ProtoMember]
        public int MinLargeGridBlockCount { get; set; }

        [ProtoMember]
        public float IdleDrainRate { get; set; }
        [ProtoMember]
        public float ContestedDrainRate { get; set; }

        [ProtoMember]
        public bool FlatProgressRate { get; set; }

        [ProtoMember]
        public float ActiveProgressRate { get; set; } // the flat progress rate applied

        [ProtoMember]
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
                PointsRemovedOnDeath = 0,
                MinSmallGridBlockCount = 50,
                MinLargeGridBlockCount = 25,
                ActivateOnPlayerCount = 1,
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
