namespace DrugiProjekat {
    class Program {
        static void Main(string[] args) {
            OLServer server = new OLServer(serverURL, 1000, 100);
            if (!server.Start())
                return;

            Logger.RawConsoleLine("Press Enter to stop the server");
            Console.ReadLine();
            server.Stop();
        }

        private const string serverURL = "http://localhost:8080/";
    }
}

// https://openlibrary.org/
