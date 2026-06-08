using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrugiProjekat {
    /*
       Sintaksa upita serveru:
        (<authors>|<title>|<subjects>|<publisher>|<key>|<work_year>|<edition_year>)
        {&(<authors>|<title>|<subjects>|<publisher>|<key>|<work_year>|<edition_year>)}
        [&<fields_sort_lang_variations>]
        Opis argumenata za search:
            sort          - ista semantika kao u OpenLibrary API upitu
            lang          - dvoslovni akronim zeljenog jezika (ne izbacuje ostale jezike, samo promovise rezultate na ciljanom)
            fields        - polja iz OpenLibrary API odgovora koja ce se naci u svakom elementu odgovora servera
            work_year     - solr opseg koji treba da obuhvati godinu u kojoj je rad prvi put objavljen (*)
            edition_year  - solr opseg koji treba da obuhvati godinu u kojoj je zeljena edicija objavljena (*)
            authors       - zarozem odvojen niz vrednosti koji ce se traziti kao podskup autora rada (*)
            title         - pretraga po naslovu rada (*)
            subjects      - zarozem odvojen niz vrednosti koji ce se traziti kao podskup tema u radu (*)
            publisher     - izdavac trazenog naslova (*)
            key           - OpenLibrary API kljuc rada (*)

        *Visevrednosni upiti nad ovim argumentima (x=<a>&x=<b>&...&x=<c>) se prevode kao solr OR lanac (x:f(<a>) OR x:f(<b>) OR ... OR x:f(<c>))
     */
    public class QueryTranslator {
        public QueryTranslator() {
            sourceUnits = new Dictionary<string, Unit> {
                ["sort"]         = new Unit("o=", "sort=", false),
                ["lang"]         = new Unit("l=", "lang=", false),
                ["key"]          = new Unit("k=", "key:"),
                ["title"]        = new Unit("t=", "title:"),
                ["publisher"]    = new Unit("p=", "publisher:"),
                ["work_year"]    = new Unit("w=", "first_publish_year:"),
                ["edition_year"] = new Unit("e=", "publish_year:"),
                ["fields"]       = new Unit("f=", "fields=",
                                            atom => string.Join(',', atom.Split(",")
                                                                         .Select(field => field.Trim())
                                                                         .Distinct()
                                                                         .Order()),
                                            (prefix, fields) => prefix + string.Join(',', fields.Split(",")
                                                                                                .Select(field => field.Trim())
                                                                                                .Distinct()
                                                                                                .Order()),
                                            false),
                ["authors"]      = new Unit("a=", "author:",
                                            unsorted => string.Join(',', unsorted.Split(',')
                                                                                 .Select(value => value.Trim())
                                                                                 .Distinct()
                                                                                 .Order()),
                                            (prefix, values) => string.Join(" AND ", values.Split(',')
                                                                                           .Select(value => prefix + value.Trim()))),
                ["subjects"]     = new Unit("s=", "subject:",
                                            unsorted => string.Join(',', unsorted.Split(',')
                                                                                 .Select(value => value.Trim())
                                                                                 .Distinct()
                                                                                 .Order()),
                                            (prefix, values) => string.Join(" AND ", values.Split(',')
                                                                                           .Select(value => prefix + value.Trim()))),
            };
            caseInsensitive = new HashSet<string>(["title", "authors", "subjects", "publisher", "lang", "fields"]);
        }

        private class Unit {
            public Unit(string srcKey, string trKey, bool isSolr = true)
            : this(
                srcKey,
                trKey,
                value => value.Trim(),
                (prefix, value) => prefix + value.Trim(),
                isSolr) {
            }
            public Unit(string srcKey, string trKey, Func<string, string> canonicalFormatter, Func<string, string, string> atomFormatter, bool isSolr = true) {
                IsSolr = isSolr;
                sourcePrefix = srcKey;
                translatedPrefix = trKey;
                TranslationComponent = "";
                CanonicalValueFormat = canonicalFormatter;
                TranslationAtomFormat = atomFormatter;
            }

            public bool IsSolr { get; private set; }
            public string CanonicalSubquery {
                get => string.Join('&', sourceSubqueries.ToArray()); // vec je sortirano
            }
            public string TranslationComponent { get; private set; }
            public Func<string, string> CanonicalValueFormat { private get; set; }
            public Func<string, string, string> TranslationAtomFormat { private get; set; }
            public Func<string[], string> TranslationFunc { private get; set; }

            public void Add(string[] values) {
                foreach (string value in values)
                    sourceSubqueries.Add(sourcePrefix + CanonicalValueFormat.Invoke(value));
                string delimiter = IsSolr ? " OR " : "&";
                string[] grouping = IsSolr ? [ "(", ")" ] : [ "", "" ];
                TranslationComponent += TranslationComponent != "" ? delimiter : "";
                TranslationComponent += string.Join(delimiter, values.Select(value => grouping[0] + TranslationAtomFormat.Invoke(translatedPrefix, value) + grouping[1]));
            }

            private string sourcePrefix;
            private string translatedPrefix;
            private SortedSet<string> sourceSubqueries = new();
        }

        public string CanonicalSource {
            get => string.Join('&', sourceUnits.Values.Select(unit => unit.CanonicalSubquery)
                                                      .Where(subq => subq != "")
                                                      .Order());
        }
        public string TranslatedQuery {
            get {
                var usedFields = sourceUnits.Values.Where(unit => unit.TranslationComponent != "");
                string solr = "q=" + string.Join(" AND ", usedFields.Where(unit => unit.IsSolr)
                                                                    .Select(unit => $"({unit.TranslationComponent})"));
                string additional = string.Join('&', usedFields.Where(unit => !unit.IsSolr)
                                                               .Select(unit => unit.TranslationComponent));
                return solr + (additional != "" ? "&" + additional : "");
            }
        }

        public void Translate(NameValueCollection query) {
            foreach (string key in query.AllKeys) {
                if (!sourceUnits.ContainsKey(key))
                    continue;
                sourceUnits[key].Add(query.GetValues(key).Select(value => caseInsensitive.Contains(key) ? value.ToLower() : value).ToArray());
            }
        }

        private Dictionary<string, Unit> sourceUnits;
        private HashSet<string> caseInsensitive;
    }
}
