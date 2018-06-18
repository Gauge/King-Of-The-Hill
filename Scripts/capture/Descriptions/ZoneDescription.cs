using ProtoBuf;

namespace KingOfTheHill
{
    [ProtoContract]
    public class ZoneDescription
    {

        [ProtoMember]
        public long GridId { get; set; }

        [ProtoMember]
        public long BlockId { get; set; }

        [ProtoMember]
        public float Progress { get; set; }

        [ProtoMember]
        public long ControlledBy { get; set; } // faction currently capturing the point

        public override string ToString()
        {
            return $"(ZONE) Progress: {Progress}, ControlledBy: {ControlledBy}";
        }
    }
}
