using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrugiProjekat {
    public class CacheSlot {
        public CacheSlot(string requestee, ResponseData response) {
            Response = response;
            Requestee = requestee;
            CreationDate = DateTime.Now;
        }
        public CacheSlot(string requestee, byte[] body, string contentType)
        : this(requestee, new ResponseData(body, contentType)) {
        }

        public byte[] Body { get => Response.Body; }
        public string Requestee { get; private set; }
        public string ContentType { get => Response.ContentType; }
        public DateTime CreationDate { get; }
        public ResponseData Response { get; private set; }
    }
}
