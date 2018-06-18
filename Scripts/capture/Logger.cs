using VRage.Utils;

namespace KingOfTheHill
{

    public class Logger
    {

        public static void Log(MyLogSeverity level, string message)
        {
            MyLog.Default.Log(level, $"[KingOfTheHill] {message}");
            //MyLog.Default.Flush();
        }
    }
}
