﻿using ProtoBuf;

namespace KingOfTheHill.Descriptions
{
    [ProtoContract]
    public class ScoreDescription
    {
        [ProtoMember(1)]
        public long FactionId { get; set; }

        [ProtoMember(2)]
        public string FactionName { get; set; }

        [ProtoMember(3)]
        public string FactionTag { get; set; }

        [ProtoMember(4)]
        public int Points { get; set; }
    }
}
