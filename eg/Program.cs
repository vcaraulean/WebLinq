namespace WebLinq.Samples
{
    #region Imports

    using System;
    using System.Collections.Specialized;
    using System.Data;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Reactive.Linq;
    using System.Web;
    using System.Xml.Linq;
    using Text;
    using TryParsers;
    using Html;
    using Modules;
    using Xsv;
    using static Modules.HttpModule;
    using static Modules.SpawnModule;
    using static Modules.XmlModule;

    #endregion

    static class Program
    {
        public static void Main(string[] args)
        {
            var ruler1 = new string('=', Console.BufferWidth - 1);
            var ruler2 = new string('-', Console.BufferWidth - 1);

            var samples =
                from s in new[]
                {
                    //new { Title = nameof(GoogleSearch)          , Query = GoogleSearch()           },
                    //new { Title = nameof(QueenSongs)            , Query = QueenSongs()             },
                    //new { Title = nameof(ScheduledTasksViaSpawn), Query = ScheduledTasksViaSpawn() },
                    //new { Title = nameof(TopHackerNews)         , Query = TopHackerNews(100)       },
                    //new { Title = nameof(MsdnBooksXmlSample)    , Query = MsdnBooksXmlSample()     },
                    //new { Title = nameof(MockarooCsv)           , Query = MockarooCsv()            },
                    new { Title = nameof(Biotradestatus)        , Query = Biotradestatus()         },
                }
                where args.Length == 0
                   || args.Any(a => s.Title.Equals(a, StringComparison.OrdinalIgnoreCase))
                select s;

            foreach (var sample in samples)
            {
                Console.WriteLine(ruler1);
                Console.WriteLine(sample.Title);
                Console.WriteLine(ruler2);
                foreach (var e in sample.Query.ToEnumerable())
                    Console.WriteLine(e);
            }
        }

        static IObservable<object> GoogleSearch() =>

            from sr in Http.Get(new Uri("http://google.com/"))
                           .Submit(0, new NameValueCollection { ["q"] = "foobar" })
                           .Html()
                           .Expand(curr =>
                           {
                               var next = curr.Content.TryBaseHref(curr.Content.QuerySelectorAll("#foot a.fl")
                                                                               .Last() // Next
                                                                               .GetAttributeValue("href"));
                               return curr.Client.Get(new Uri(next)).Html();
                           })
                           .TakeWhile(h => (TryParse.Int32(HttpUtility.ParseQueryString(h.Content.BaseUrl.Query)["start"]) ?? 1) < 30)
            select sr.Content into sr
            from r in sr.QuerySelectorAll(".g")
            select new
            {
                Title = r.QuerySelector(".r")?.InnerText,
                Summary = r.QuerySelector(".st")?.InnerText,
                Href = sr.TryBaseHref(r.QuerySelector(".r a")?.GetAttributeValue("href")),
            }
            into e
            where !string.IsNullOrWhiteSpace(e.Title)
                && e.Href != null
                && !string.IsNullOrWhiteSpace(e.Summary)
            select e;

        static IObservable<object> ScheduledTasksViaSpawn() =>

            from xml in Spawn("schtasks", "/query /xml ONE").Delimited(Environment.NewLine)
            let ns = XNamespace.Get("http://schemas.microsoft.com/windows/2004/02/mit/task")
            from t in ParseXml(xml).Elements("Tasks").Elements(ns + "Task")
            from e in t.Elements(ns + "Actions").Elements(ns + "Exec")
            select new
            {
                Name      = ((XComment)t.PreviousNode).Value.Trim(),
                Command   = (string)e.Element(ns + "Command"),
                Arguments = (string)e.Element(ns + "Arguments"),
            };

        static IObservable<object> QueenSongs() =>

            from t in Http.Get(new Uri("https://en.wikipedia.org/wiki/Queen_discography")).Tables().Content((http, t) => new { Http = http, Table = t })
                          .Where(t => t.Table.HasClass("wikitable"))
                          .Take(1)
            from tr in t.Table.TableRows((_, trs) => trs)
            let th = tr.FirstOrDefault(e => e?.AttributeValueEquals("scope", "row") == true)
            where th != null
            let a = th.QuerySelector("a[href]")
            select new
            {
                t.Http,
                Title = a.GetAttributeValue("title")?.Trim(),
                Href = a.Owner.TryBaseHref(a.GetAttributeValue("href")?.Trim()),
            }
            into e
            select new
            {
                e.Http,
                e.Title,
                Url = TryParse.Uri(e.Href, UriKind.Absolute),
            }
            into e
            where !string.IsNullOrEmpty(e.Title) && e.Url != null
            select e
            into album

            from html in album.Http.Get(album.Url).Html().Content()

            from tb in html.Tables(".tracklist").Take(2)
            let trs = tb.QuerySelectorAll("tr")
            let hdrs =
                trs.FirstOrDefault(tr => tr.QuerySelectorAll("th").Take(4).Count() >= 3)
                    ?.QuerySelectorAll("th")
                    .Select(th => th.InnerTextSource.Decoded.Trim())
                    .ToArray()
            where hdrs != null
            let idxs =
                new[] { "Title", "Writer(s)", "Length" }
                    .Select(h => Array.FindIndex(hdrs, he => he == h))
                    .ToArray()
            let his = new
            {
                Title   = idxs[0],
                Writers = idxs[1],
                Length  = idxs[2],
            }
            from tr in trs
            let tds =
                tr.QuerySelectorAll("td")
                    .Select(td => td.InnerTextSource.Decoded)
                    .ToArray()
            where tds.Length >= 3
            select new
            {
                Album    = album.Title,
                Title    = tds[his.Title],
                Author   = his.Writers >= 0 ? tds[his.Writers] : null,
                Duration = tds[his.Length],
            };

        static IObservable<object> TopHackerNews(int score) =>

            from sp in Http.Get(new Uri("https://news.ycombinator.com/")).Html().Content()
            let scores =
                from s in sp.QuerySelectorAll(".score")
                select new
                {
                    Id = Regex.Match(s.GetAttributeValue("id"), @"(?<=^score_)[0-9]+$").Value,
                    Score = s.InnerText,
                }
            from e in
                from r in sp.QuerySelectorAll(".athing")
                select new
                {
                    Id = r.GetAttributeValue("id"),
                    Link = r.QuerySelector(".storylink")?.GetAttributeValue("href"),
                }
                into r
                join s in scores on r.Id equals s.Id
                select new
                {
                    r.Id,
                    Score = int.Parse(Regex.Match(s.Score, @"\b[0-9]+(?= +points)").Value),
                    r.Link,
                }
                into e
                where e.Score > score
                select e
            select e;

        static IObservable<object> MsdnBooksXmlSample() =>

            from html in
                Http.Get(new Uri("https://msdn.microsoft.com/en-us/library/ms762271.aspx"))
                    .Html()
                    .Content()
            select html.QuerySelector(".codeSnippetContainerCode").InnerText.TrimStart()
            into xml
            from book in ParseXml(xml).Descendants("book")
            select new
            {
                Id            = (string)   book.Attribute("id"),
                Title         = (string)   book.Element("title"),
                Author        = (string)   book.Element("author"),
                Genre         = (string)   book.Element("genre"),
                Price         = (float)    book.Element("price"),
                PublishedDate = (DateTime) book.Element("publish_date"),
                Description   = ((string)  book.Element("description")).NormalizeWhitespace(),
            };

        static string NormalizeWhitespace(this string str) =>
            string.IsNullOrEmpty(str)
            ? str
            : string.Join(" ", str.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        static IObservable<object> MockarooCsv() =>

            from cols in Observable.Return(new
            {
                Id        = new DataColumn("id", typeof(int)),
                FirstName = new DataColumn("first_name"),
                LastName  = new DataColumn("last_name"),
                Email     = new DataColumn("email"),
                Gender    = new DataColumn("gender"),
                IpAddress = new DataColumn("ip_address"),
            })

            from t in Http.Get(new Uri("https://www.mockaroo.com/"))
                          .SubmitTo(new Uri("https://www.mockaroo.com/schemas/download"), "#schema_form", new NameValueCollection
                          {
                              ["preview"]             = "false",
                              ["schema[file_format]"] = "csv",
                          })
                          .Accept("text/csv")
                          .CsvToDataTable(cols.Id, cols.FirstName, cols.LastName, cols.Gender, cols.Email, cols.IpAddress)
                          .Content()

            from row in t.AsEnumerable()
            select new
            {
                Id        = row[cols.Id       ],
                FirstName = row[cols.FirstName],
                LastName  = row[cols.LastName ],
                Email     = row[cols.Email    ],
                Gender    = row[cols.Gender   ],
                IpAddress = row[cols.IpAddress],
            };

        static IObservable<object> Biotradestatus() => 
            from cols in Observable.Return(new
            {
                Country = new DataColumn("Country"),
                Company = new DataColumn("Company"),
                Product = new DataColumn("Product")
            })
            
            from html in Http.Get(new Uri("http://www.biotradestatus.com/"))
                             .SubmitTo(new Uri("http://www.biotradestatus.com/"), "form[name='DisclaimerForm']", new NameValueCollection())
                             .SubmitTo(new Uri("http://www.biotradestatus.com/results.cfm"), "#commercialapprovalstatus", new NameValueCollection
                             {
                                 ["oecd_status_id"] = "1",
                                 ["approval_id"] = "41",
                                 ["selectedcommodity"] = "-1",
                                 ["selectedcompany"] = "-1",
                                 ["oecd_event_option"] = "event",
                                 ["selectedevent"] = "-1",
                                 ["country_id"] = "-1",
                                 ["alldates"] = "1",
                                 ["startdate"] = "02/08/2011",
                                 ["enddate"] = "01/18/2017",
                                 ["submitform"] = "1",
                             })
                             .Html()
                             .Content()

            from byCountry in html.QuerySelectorAll("#body > div.selectheader")
                             .Zip(html.QuerySelectorAll("#body > table"), (head, table) => new { name = head.InnerText.Trim(), table })
            from row in byCountry.table
                            .TableRows((_, rowCells) => rowCells
                                .Take(2)    // for simplicity take only 2 first columns
                                .Select(c => c.InnerText.Trim())
                                .Select(cell => cell.Contains(",") ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell) // csv excaping
                                .Aggregate((acc, next) => $"{acc},{next}"))
                            .Select(x => $"{byCountry.name},{x}")
                            .ToObservable()
                            .CsvToDataTable(cols.Country, cols.Company, cols.Product)

            from tableRow in row.AsEnumerable()
            select new
            {
                Country = tableRow[cols.Country],
                Company = tableRow[cols.Company],
                Product = tableRow[cols.Product]
            };
    }
}
