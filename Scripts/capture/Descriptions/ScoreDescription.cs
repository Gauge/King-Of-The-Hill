using ProtoBuf;

namespace KingOfTheHill
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

        public override string ToString()
        {
            return $"(SCORE) [{FactionTag}] {FactionName}: {Points}";
        }
    }
}
