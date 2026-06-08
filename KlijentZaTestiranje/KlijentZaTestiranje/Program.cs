using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace tester {
    class Program {
        private class CaseComponent {
            public CaseComponent(string query, int repetitions = 1) {
                Query = query;
                Repetitions = repetitions;
            }

            public int Repetitions { get; }
            public string Query { get; }
            public string RequestUrl { get => queryUrl + Query; }
        };

        private static async Task Main() {
            ThreadPool.SetMinThreads(100, 100);

            Console.WriteLine("Program za testiranje");
            Console.WriteLine("Test se pokrece unosom koda sekvence upita koji se salje");
            Console.WriteLine("Proces se prekida unosom Enter tastera bez unosa koda");
            Console.WriteLine("Legenda:");
            Console.WriteLine("\"1\" - Slanje jednog zahteva");
            Console.WriteLine("\"2\" - Slanje vise razlicitih zahteva");
            Console.WriteLine("\"3\" - Testiranje stampeda i kanonickog uredjivanja");
            Console.WriteLine("\"4\" - Slucaj kada nema knjiga");
            while (true) {
                string input = Console.ReadLine()!;
                if (input == "") {
                    break;
                }
                if (!testCases.TryGetValue(input, out CaseComponent[]? sequence)) {
                    Console.WriteLine($"Nevazeci kod slucaja: \"{input}\"");
                    continue;
                }

                badCount = 0;
                goodCount = 0;
                bool isStampede = (input == "3");
                int totalParticipants = sequence.Sum(component => component.Repetitions);
                sentCount = totalParticipants;

                using Barrier? stampedeBarrier = isStampede ? new Barrier(totalParticipants) : null;

                List<Task> requestTasks = new();
                foreach (CaseComponent component in sequence) {
                    for (int i = 0; i < component.Repetitions; i++) {
                        string url = component.RequestUrl;
                        requestTasks.Add(Task.Run(() => SendAsync(url, stampedeBarrier)));
                    }
                }

                await Task.WhenAll(requestTasks);

                Console.WriteLine($"Broj uspesnih zahteva: {goodCount}/{sentCount}");
                Console.WriteLine($"Broj neuspelih zahteva: {badCount}/{sentCount}");
                Console.WriteLine($"----------------------------------------------");
            }
        }

        private static async Task SendAsync(string url, Barrier? stampedeBarrier) {
            if (stampedeBarrier != null) {
                stampedeBarrier.SignalAndWait();
            }

            try {
                HttpResponseMessage message = await client.GetAsync(url);
                lock (_lock) {
                    Console.WriteLine($"[{++goodCount}/{sentCount}] Server responded with {message.StatusCode} ({url})");
                }
            }
            catch (Exception e) {
                lock (_lock) {
                    badCount++;
                    Console.WriteLine($"Task {Task.CurrentId} threw exception: {e.Message}");
                }
            }
        }

        private static int badCount = 0;
        private static int goodCount = 0;
        private static int sentCount = 0;
        private static readonly object _lock = new();
        private static readonly HttpClient client = new();
        private const string queryUrl = "http://localhost:8080/search?";
        private static readonly Dictionary<string, CaseComponent[]> testCases = new() {
            // Jedan zahtev
            ["1"] = [
                new CaseComponent("authors=tolkien")
            ],
            // Vise razlicitih zahteva
            ["2"] = [
                new CaseComponent("title=neuromancer"),
                new CaseComponent("title=harry potter"),
                new CaseComponent("authors=george martin"),
                new CaseComponent("subjects=fantasy"),
                new CaseComponent("title=dune"),
                new CaseComponent("authors=tanenbaum"),
                new CaseComponent("title=Hobbit&authors=tolkien"),
                new CaseComponent("authors=tolkien&title=hobbit&fields=*&sort=new&lang=en"),
                new CaseComponent("authors=a"),
                new CaseComponent("publisher=penguin&sort=old"),
                new CaseComponent("subjects=fantasy&work_year=[2000 TO 2010]"),
                new CaseComponent("title=crime and punishment"),
                new CaseComponent("authors=dostoyevsky"),
                new CaseComponent("authors=tolkien, Ian Brodie&title=hobbit"),
                new CaseComponent("authors=tolkien&authors=Ian Brodie&title=hobbit"),
                new CaseComponent("subjects=fantasy,education&fields=author_name,title,subject"),
                new CaseComponent("subjects=fantasy&subjects=education&fields=author_name,title,subject"),
            ],
            // Stampedo (i kanonicko uredjivanje)
            ["3"] = [
                new CaseComponent("authors=asimov&sort=new", 10),
                new CaseComponent("sort=new&authors=asimov", 10),
            ],
            // Nema knjiga
            ["4"] = [
                new CaseComponent("authors=fgsefgidsfgsdfgi"),
            ],
        };
    }
}
