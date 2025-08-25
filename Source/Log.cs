namespace Vini.Upgrade
{
    public static class Log
    {
        public static void Out(string message)
        {
            System.Console.WriteLine(message);
            try { UnityEngine.Debug.Log(message); } catch { /* ignore em build server */ }
        }

        public static void Error(string message)
        {
            System.Console.Error.WriteLine(message);
            try { UnityEngine.Debug.LogError(message); } catch { /* ignore */ }
        }
    }
}
