using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrugiProjekat {
    public class LRUCache {
        public LRUCache(int capacity, bool useQuaziTTL = false, int ttlMinutes = 60) {
            cache = new Dictionary<string, LinkedListNode<CacheSlot>>(capacity);
            UsesQuaziTTL = useQuaziTTL;
            TTLMinutes = useQuaziTTL ? ttlMinutes : -1;
            emptyCount = capacity;
        }

        public enum InsertionMethod {
            Simple,
            Replacement
        }

        public ResponseData this[string request] {
            get => Read(request);
            set => Add(new CacheSlot(request, value));
        }
        public int TTLMinutes { get; }
        public bool UsesQuaziTTL { get; }

        public bool Contains(string request) {
            if (!cache.TryGetValue(request, out LinkedListNode<CacheSlot> node))
                return false;
            if (UsesQuaziTTL && (DateTime.Now - node.Value.CreationDate).TotalMinutes >= TTLMinutes) {
                Remove(node);
                return false;
            }
            return true;
        }

        // Struktura je implementirana tako da je
        // odgovornost korisnika strukture da proverava da li se
        // nesto nalazi u kesu (sa Contains) pre dodavanja ili citanja,
        // radi izbegavanja eventualnih reduntantnih provera prilikom izvrsenja samih operacija
        public InsertionMethod Add(string request, byte[] body, string contentType)
            => Add(new CacheSlot(request, body, contentType));

        public InsertionMethod Add(CacheSlot value) {
            if (emptyCount > 0) {
                SimpleInsert(value.Requestee, value);
                emptyCount--;
                return InsertionMethod.Simple;
            }
            ReplaceInsert(value.Requestee, value);
            return InsertionMethod.Replacement;
        }
        public ResponseData Read(string request) {
            LinkedListNode<CacheSlot> slot = cache[request];
            lruChain.Remove(slot);
            lruChain.AddFirst(slot);
            return slot.Value.Response;
        }

        private void Remove(LinkedListNode<CacheSlot> node) {
            cache.Remove(node.Value.Requestee);
            lruChain.Remove(node);
            emptyCount++;
        }
        private void RemoveLRU() {
            cache.Remove(lruChain.Last.Value.Requestee);
            lruChain.RemoveLast();
        }
        private void ReplaceInsert(string request, CacheSlot response) {
            RemoveLRU();
            SimpleInsert(request, response);
        }
        private void SimpleInsert(string request, CacheSlot response)
            => cache.Add(request, lruChain.AddFirst(response));

        // Povezana lista je idealna za LRU jer ima O(1) dodavanje na pocetak, izbacivanje sa kraja, kao i promenu pozicije cvora u listi...
        private LinkedList<CacheSlot> lruChain = new();
        // ... Jedina mana je O(n) pretraga, pa zato koristimo pomocni Dictionary koji cuva sve cvorove liste radi O(1) (average) pristupa cvorovima
        private Dictionary<string, LinkedListNode<CacheSlot>> cache;
        private int emptyCount;
    }
}
