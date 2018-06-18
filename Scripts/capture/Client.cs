using Sandbox.ModAPI;
using System;
using VRage.Utils;

namespace KingOfTheHill
{
    public class Client : ICommunicate
    {
        public event Action<Command> OnCommandRecived = delegate { };
        public event Action<string> OnUserInput = delegate { }; 

        private ushort ModId;
        private string Keyword;

        public bool IsServer => false;

        /// <summary>
        /// Handles communication with the server
        /// </summary>
        /// <param name="modId">Identifies what communications are picked up by this mod</param>
        /// <param name="keyword">identifies what chat entries should be captured and sent to the server</param>
        public Client(ushort modId, string keyword)
        {
            ModId = modId;
            Keyword = keyword.ToLower();

            MyAPIGateway.Utilities.MessageEntered += HandleChatInput;
            MyAPIGateway.Multiplayer.RegisterMessageHandler(this.ModId, HandleMessage);
        }

        public void Close()
        {
            Logger.Log(MyLogSeverity.Info, "Unregisering handlers before close");
            MyAPIGateway.Utilities.MessageEntered -= HandleChatInput;
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(this.ModId, HandleMessage);
        }

        private void HandleChatInput(string messageText, ref bool sendToOthers)
        {
            string[] args = messageText.Split(' ');
            if (args[0].ToLower() != Keyword) return;
            sendToOthers = false;

            OnUserInput.Invoke(messageText.Substring(Keyword.Length).Trim(' '));
        }

        private void HandleMessage(byte[] msg)
        {
            try
            {
                //Logger.Log(MyLogSeverity.Info, $"[Client] Recived message of length {msg.Length}");
                Command cmd = MyAPIGateway.Utilities.SerializeFromBinary<Command>(msg);
                //Logger.Log(MyLogSeverity.Info, cmd.ToString());

                if (cmd != null)
                {
                    OnCommandRecived.Invoke(cmd);
                }
            }
            catch
            {
                Logger.Log(MyLogSeverity.Warning, "Did not recieve a command packet. Mod Id may be compromise. Please send a list of all mods used with this on to me (the mod owner)");
            }
        }

        public void SendCommand(string message, ulong steamId = ulong.MinValue)
        {
            SendCommand(new Command { Message = message });
        }

        public void SendCommand(Command cmd, ulong steamId = ulong.MinValue)
        {
            cmd.SteamId = MyAPIGateway.Session.Player.SteamUserId;
            byte[] data = MyAPIGateway.Utilities.SerializeToBinary(cmd);
            //Logger.Log(MyLogSeverity.Info, $"[Server] Sending message of length {data.Length}");
            //Logger.Log(MyLogSeverity.Info, cmd.ToString());

            MyAPIGateway.Multiplayer.SendMessageToServer(ModId, data, true);
        }
    }
}
