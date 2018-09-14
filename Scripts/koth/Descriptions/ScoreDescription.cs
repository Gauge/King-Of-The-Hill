using ProtoBuf;

namespace KingOfTheHill.Descriptions
{
    [ProtoContract]
    public class ScoreDescription
    {
        [ProtoMember]
        public long FactionId { get; set; }

        [ProtoMember]
        public string FactionName { get; set; }

        [ProtoMember]
        public string FactionTag { get; set; }

        [ProtoMember]
        public int Points { get; set; }
    }
}
