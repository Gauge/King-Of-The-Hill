using ProtoBuf;

namespace KingOfTheHill
{
    [ProtoContract]
    public class Command
    {
        [ProtoMember]
        public ulong SteamId { get; set; }

        [ProtoMember]
        public string Message { get; set; }

        [ProtoMember]
        public Info DataDump { get; set; } = new Info();

        public override string ToString()
        {
            return $"SteamId: {SteamId}, Message: \'{Message}\', Data: {DataDump.ToString()}";
        }
    }
}
