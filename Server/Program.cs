using ServerConsole;

internal class Program
{
    private static void Main(string[] args)
    {
        Server server = new Server("127.0.0.1", 8088);
        server.Start();
    }
}