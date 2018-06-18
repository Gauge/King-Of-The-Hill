using ProtoBuf;
using System.Collections.Generic;
using System.Text;

namespace KingOfTheHill
{
    [ProtoContract]
    public class Info
    {
        [ProtoMember]
        public List<ScoreDescription> Scores { get; set; } = new List<ScoreDescription>();

        [ProtoMember]
        public List<ZoneDescription> Zones { get; set; } = new List<ZoneDescription>();

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"[Scores: {Scores.Count}, Zones: {Zones.Count}]");

            foreach (ScoreDescription s in Scores) { sb.AppendLine(s.ToString()); }

            foreach (ZoneDescription z in Zones) { sb.AppendLine(z.ToString());  }

            return sb.ToString();
        }
    }
}
