using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DrugiProjekat {
    public class OLCache {
        public OLCache(int capacity, bool useQuaziTTL = true, int ttlMinutes = 60) {
            store = new LRUCache(capacity, useQuaziTTL, ttlMinutes);
        }

        public Task<ResponseData> GetOrFetchAsync(string canonical, Func<Task<ResponseData>> fetch) {
            lock (_lock) {
                // cache hit => nema API poziva
                if (store.Contains(canonical)) {
                    Logger.Log(Logger.Event.Notify, "Found response in local cache");
                    return Task.FromResult(store[canonical]);
                }
                // neki task vec pribavlja isti resurs => vracamo prethodno pokrenuti task
                if (inFlight.TryGetValue(canonical, out Task<ResponseData> pending)) {
                    Logger.EchoLog(Logger.Event.Synchro, $"Awaiting on Task {pending.Id} to fetch response from OpenLibrary)");
                    return pending;
                }
                // cache miss => pokrecemo tacno jedan task koji poziva API i kesira
                Logger.EchoLog(Logger.Event.Notify, "Request not cached, API callback initiated");
                Task<ResponseData> fetchTask = FetchStoreCompleteAsync(canonical, fetch);
                inFlight[canonical] = fetchTask;
                return fetchTask;
            }
        }

        private async Task<ResponseData> FetchStoreCompleteAsync(string canonical, Func<Task<ResponseData>> fetch) {
            try {
                ResponseData result = await fetch();

                lock (_lock) {
                    CacheSlot newEntry = new CacheSlot(canonical, result);
                    LRUCache.InsertionMethod insertionType = store.Add(newEntry);
                    Logger.Log(Logger.Event.Notify, $"Cached {newEntry.Body.Length}B of data; operation type: {insertionType}");
                }
                return result;
            }
            finally {
                lock (_lock) {
                    inFlight.Remove(canonical);
                }
            }
        }

        private readonly LRUCache store;                                          
        private readonly object _lock = new();                                     
        private readonly Dictionary<string, Task<ResponseData>> inFlight = new();
    }
}
