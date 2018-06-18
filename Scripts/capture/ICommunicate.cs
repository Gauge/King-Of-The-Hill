using System;

namespace KingOfTheHill
{
    public interface ICommunicate
    {
        bool IsServer { get; }

        event Action<Command> OnCommandRecived;
        event Action<string> OnUserInput;

        void SendCommand(string message, ulong steamId = ulong.MinValue);
        void SendCommand(Command cmd, ulong steamId = ulong.MinValue);
        void Close();
    }
}
