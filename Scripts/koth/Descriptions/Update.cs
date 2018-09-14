using ProtoBuf;
using System.Collections.Generic;

namespace KingOfTheHill.Descriptions
{
    [ProtoContract]
    public class Update
    {
        [ProtoMember]
        public List<ZoneDescription> Zones { get; set; } = new List<ZoneDescription>();
    }
}
