using Sandbox.ModAPI;
using System;
using VRage.Utils;

namespace KingOfTheHill
{
    internal class Server : ICommunicate
    {
        public event Action<Command> OnCommandRecived = delegate { };
        public event Action<string> OnUserInput = delegate { };

        private ushort ModId;

        public bool IsServer => true;

        public Server(ushort modId)
        {
            ModId = modId;
            MyAPIGateway.Multiplayer.RegisterMessageHandler(modId, HandleMessage);
        }

        private void HandleMessage(byte[] msg)
        {
            try
            {
                //Logger.Log(MyLogSeverity.Info, $"[Server] Recived message of length {msg.Length}");
                Command cmd = MyAPIGateway.Utilities.SerializeFromBinary<Command>(msg);
                //Logger.Log(MyLogSeverity.Info, cmd.ToString());

                if (cmd != null)
                {
                    OnCommandRecived.Invoke(cmd);
                }
            }
            catch (Exception e)
            {
                Logger.Log(MyLogSeverity.Warning, "Did not recieve a command packet. Mod Id may be compromise. Please send a list of all mods used with this on to me (the mod owner)");
                Logger.Log(MyLogSeverity.Error, e.ToString());
            }
        }

        public void SendCommand(string message, ulong steamId = ulong.MinValue)
        {
            SendCommand(new Command() { Message = message }, steamId);
        }

        public void SendCommand(Command cmd, ulong steamId = ulong.MinValue)
        {
            byte[] data = MyAPIGateway.Utilities.SerializeToBinary(cmd);
            //Logger.Log(MyLogSeverity.Info, $"[Server] Sending message of length {data.Length}");
            //Logger.Log(MyLogSeverity.Info, cmd.ToString());

            if (steamId == ulong.MinValue)
            {
                MyAPIGateway.Multiplayer.SendMessageToOthers(ModId, data);
            }
            else
            {
                MyAPIGateway.Multiplayer.SendMessageTo(ModId, data, steamId);
            }
        }

        public void Close()
        {
            //Logger.Log(MyLogSeverity.Info, "Unregisering handlers before close");
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(ModId, HandleMessage);
        }
    }
}
