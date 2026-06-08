using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DrugiProjekat {
    public class OLServer {
        public OLServer(string serverPath, int cacheSize, int maxConcurrentProcessing = 100) {
            this.serverPath = serverPath;
            this.maxConcurrentProcessing = maxConcurrentProcessing;
            httpL.Prefixes.Add(serverPath);
            cache = new OLCache(cacheSize, true);
            processingSlots = new SemaphoreSlim(maxConcurrentProcessing, maxConcurrentProcessing);
        }

        public bool Start() {
            try {
                httpL.Start();
                listenerThread = new Thread(Listen);
                dispatcherThread = new Thread(Dispatch);
                listenerThread.Start();
                dispatcherThread.Start();
            }
            catch (Exception e) {
                Logger.EchoLog(Logger.Event.Critical, $"Server failed to start: {e.Message}");
                return false;
            }
            Logger.EchoLog(Logger.Event.Notify, $"Started server at web address {serverPath} (do {maxConcurrentProcessing} paralelnih obrada)");
            return true;
        }

        public void Stop() {
            Logger.EchoLog(Logger.Event.Notify, "Server is closing");
            cts.Cancel();                
            httpL.Stop();                  
            requestQueue.CompleteAdding();
            listenerThread.Join();
            dispatcherThread.Join();

            // zatvaramo zahteve koji su ostali u redu, a nisu stigli na obradu
            while (requestQueue.TryTake(out HttpListenerContext? leftover)) {
                try { 
                    leftover.Response.Abort(); 
                } 
                catch { 
                    // stream je mozda vec zatvoren
                }
            }

            for (int i = 0; i < maxConcurrentProcessing; i++)
                processingSlots.Wait();

            httpL.Close();
            processingSlots.Dispose();
            requestQueue.Dispose();
            cts.Dispose();
            Logger.EchoLog(Logger.Event.Notify, "Server closed");
        }

        private void Listen() {
            while (httpL.IsListening) {
                try {
                    HttpListenerContext context = httpL.GetContext();
                    Logger.EchoLog(context.Request, "Request recieved");
                    requestQueue.Add(context);
                }
                catch (Exception e) {
                    Logger.Error(e.Message);
                }
            }
            Logger.Log(Logger.Event.Notify, "Server is no longer listening");
        }

        private void Dispatch() {
            try {
                foreach (HttpListenerContext context in requestQueue.GetConsumingEnumerable(cts.Token)) {
                    processingSlots.Wait(cts.Token);
                    int active = Interlocked.Increment(ref activeCount);
                    Logger.Log(Logger.Event.Notify, $"Dispatching (active: {active}/{maxConcurrentProcessing})");

                    Task processing = ProcessRequestAsync(context, cts.Token);

                    processing.ContinueWith(antecedent => {
                        processingSlots.Release();
                        Interlocked.Decrement(ref activeCount);
                        if (antecedent.IsFaulted)
                            Logger.Error("Error during processing request -> " +
                                         antecedent.Exception!.GetBaseException().Message);
                    });
                }
            }
            catch (OperationCanceledException) {
                Logger.Log(Logger.Event.Notify, "Dispatcher stopped");
            }
            catch (Exception e) {
                Logger.Error("Dispatcher error -> " + e.Message);
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken ct) {
            Stopwatch taskTime = Stopwatch.StartNew();
            Logger.Log(context.Request, "Request started processing");

            QueryTranslator translator = new();
            string canonical = "";

            try {
                string arguments = context.Request.Url!.AbsolutePath.ToLower();

                if (arguments == "/") {
                    await SendResponseAsync(context.Response, RootText, 200, ct);
                }
                else if (arguments == "/syntax") {
                    await SendResponseAsync(context.Response, SyntaxText, 200, ct);
                }
                else if (arguments == "/search") {
                    translator.Translate(context.Request.QueryString);
                    canonical = translator.CanonicalSource;
                    string translated = translator.TranslatedQuery;
                    Logger.EchoLog(Logger.Event.Notify, "Translation chain:\n\t" +
                                                       $"   {context.Request.RawUrl}\n\t" +
                                                       $"   -> {canonical}\n\t" +
                                                       $"   -> {translated}");

                    Task<ResponseData> resultTask = cache.GetOrFetchAsync(
                        canonical,
                        () => FetchAndBuildAsync(translated, canonical, ct));

                    ResponseData result = await resultTask;

                    Logger.EchoLog(Logger.Event.Response, $"Sending {result.Body.Length}B long response");
                    await SendResponseAsync(context.Response, result, ct);
                }
                else {
                    await SendResponseAsync(context.Response, "Request unrecognized", 404, ct);
                }
            }
            catch (OperationCanceledException) {
                // nastupa pri gasenju servera (token otkazan) ili ako klijent prekine vezu
                Logger.Log(Logger.Event.Notify, $"Request cancelled: {canonical}");
            }
            catch (Exception e) {
                Logger.Error($"Error while processing request {canonical} -> {e.Message}");
                try {
                    await SendResponseAsync(context.Response, $"Internal server error:\n {e.Message}", 500, ct);
                }
                catch {
                    Logger.Error("Couldn't send an error response back to the client because the communication stream has already been closed");
                }
            }
            finally {
                Logger.EchoLog(Logger.Event.Time, $"Finished processing in {taskTime.ElapsedMilliseconds * .001}s");
            }
        }

        private Task<ResponseData> FetchAndBuildAsync(string translatedQuery, string canonical, CancellationToken ct) {
            Logger.EchoLog(Logger.Event.Network, "Sending async request to OpenLibrary's API");

            Task<string> fetchTask = OLFetchAsync(translatedQuery, ct);

            return fetchTask
                .ContinueWith(t => JsonNode.Parse(t.Result)!.AsObject())
                .ContinueWith(t => BuildResponse(t, canonical));
        }

        private ResponseData BuildResponse(Task<JsonObject> parseTask, string canonical) {
            if (parseTask.IsCanceled)
                throw new OperationCanceledException();
            if (parseTask.IsFaulted) {
                Exception baseEx = parseTask.Exception!.GetBaseException();
                // gasenje servera stize ovde kao OperationCanceledException umotan
                // u faulted stanje => ne tretiramo ga kao gresku, vec ga propustamo kao otkaz
                if (baseEx is OperationCanceledException oce)
                    throw oce;
                throw new WebException("Request to OpenLibrary's API failed -> " + baseEx.Message, baseEx);
            }

            JsonObject json = parseTask.Result;

            // response od OpenLibrary API-a, osim trazenih radova, sadrzi i metapodatke koje korisnika ovog servera verovatno ne interesuju
            if (json["numFound"]!.GetValue<int>() == 0) {
                Logger.Log(Logger.Event.Notify, "The acquired response contains no work data");
                return new ResponseData("Found no results!", 404);
            }

            foreach (string field in responseJunk)
                json.Remove(field);
            string stripped = json.ToJsonString();
            Logger.Log(Logger.Event.Notify, "Stripped excess data from the retrieved JSON object");
            return new ResponseData(Encoding.UTF8.GetBytes(stripped), "application/json; charset=utf-8");
        }

        private async Task<string> OLFetchAsync(string query, CancellationToken ct) {
            string olUrl = apiQueryPrefix + query;
            using HttpResponseMessage olResponse = await olClient.GetAsync(olUrl, ct);
            olResponse.EnsureSuccessStatusCode();
            return await olResponse.Content.ReadAsStringAsync(ct);
        }

        private Task SendResponseAsync(HttpListenerResponse httpResponse, ResponseData response, CancellationToken ct)
            => SendResponseAsync(httpResponse, response.Body, response.ContentType, response.StatusCode, ct);

        private Task SendResponseAsync(HttpListenerResponse httpResponse, string textResponse, int statusCode, CancellationToken ct)
            => SendResponseAsync(httpResponse, Encoding.UTF8.GetBytes(textResponse), "text/plain; charset=utf-8", statusCode, ct);

        private async Task SendResponseAsync(HttpListenerResponse httpResponse, byte[] body, string contentType, int statusCode, CancellationToken ct) {
            httpResponse.StatusCode = statusCode;
            httpResponse.ContentType = contentType;
            httpResponse.ContentLength64 = body.Length;
            await httpResponse.OutputStream.WriteAsync(body, 0, body.Length, ct);
            httpResponse.OutputStream.Close();
            Logger.Log(Logger.Event.Network, $"Response transfer through network initiated. Response status code: {statusCode}");
        }

        private readonly string serverPath;
        private Thread listenerThread = null!;
        private Thread dispatcherThread = null!;
        private readonly OLCache cache;
        private readonly HttpClient olClient = new();
        private readonly HttpListener httpL = new();
        private readonly BlockingCollection<HttpListenerContext> requestQueue = new();
        private readonly SemaphoreSlim processingSlots;
        private readonly int maxConcurrentProcessing;
        private readonly CancellationTokenSource cts = new();
        private int activeCount = 0;
        private const string apiQueryPrefix = "https://openlibrary.org/search.json?";
        private static readonly string[] responseJunk = {
            "start",
            "numFoundExact",
            "num_found",
            "documentation_url",
            "q",
            "offset"
        };

        private const string RootText =
            "Search OpenLibrary by sending a query to ./search.\n" +
            "Request ./syntax for query syntax description.";

        private const string SyntaxText =
            "Syntax:\n" +
            "  (<authors>|<title>|<subjects>|<publisher>|<key>|<work_year>|<edition_year>)\n" +
            "  {&(<authors>|<title>|<subjects>|<publisher>|<key>|<work_year>|<edition_year>)}\n" +
            "  [&<fields_sort_lang_variations>]\n" +
            "  \n" +
            "  Argument description:\n" +
            "      sort          - directly passed to the OLAPI query\n" +
            "      lang          - two letter acronym of the desired language.\n" +
            "                      Two letter acronym will promote matching results,\n" +
            "                      (three letter one will act as a strict query filter (*) - probably will not implement as it doesn't fit nicely in this architecture I have here)\n" +
            "      fields        - OLAPI response fields to forward from each match found at OpenLibrary\n" +
            "      work_year     - solr range for the year the work was first published (*)\n" +
            "      edition_year  - solr range for the year desired edition was published (*)\n" +
            "      authors       - comma separated list of a subset of a title's authors (*)\n" +
            "      title         - a title to search for (*)\n" +
            "      subjects      - comma separated list of a subset of a title's subjects (*)\n" +
            "      publisher     - publisher of a title's editions (*)\n" +
            "      key           - the OLAPI key of a title (*)\n" +
            "  \n" +
            "  *Multi-valued queries on this argument translate to an OR chain\n" +
            "  OLAPI - OpenLibrary API\n";
    }
}
