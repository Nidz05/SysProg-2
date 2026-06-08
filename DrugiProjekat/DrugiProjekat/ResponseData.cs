using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrugiProjekat {
    public class ResponseData {
        public ResponseData(string textMessage, int statusCode = 200)
        : this(Encoding.UTF8.GetBytes(textMessage), "text/plain; charset=utf-8", statusCode) {
        }
        public ResponseData(byte[] body, string contentType, int statusCode = 200) {
            Body = body;
            ContentType = contentType;
            StatusCode = statusCode;
        }

        public int StatusCode { get; private set; }
        public byte[] Body { get; private set; }
        public string ContentType { get; private set; }
    }
}
