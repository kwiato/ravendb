using System.Collections.Generic;
using System.Linq;
using FastTests;
using Raven.Client.Documents.Session;
using Xunit;

namespace SlowTests.Tests.Linq
{
    public class RavenDB14 : RavenTestBase
    {
        private class User
        {
            public string Name { get; set; }

            public bool Active { get; set; }
        }

        [Fact(Skip = "RavenDB-8199")]
        public void WhereThenFirstHasAND()
        {
            var queries = new List<string>();

            using (var store = GetDocumentStore())
            {
                void RecordQueries(object sender, BeforeQueryExecutedEventArgs args)
                {
                    queries.Add(args.QueryCustomization.ToString());
                    store.OnBeforeQueryExecuted -= RecordQueries;
                }

                var documentSession = store.OpenSession();
                store.OnBeforeQueryExecuted += RecordQueries;

                var _ = documentSession.Query<User>().Where(x => x.Name == "ayende").FirstOrDefault(x => x.Active);

                Assert.Equal(1, queries.Count);
                Assert.Equal("Name:ayende AND Active:true", queries[0]);
            }
        }

        [Fact(Skip = "RavenDB-8199")]
        public void WhereThenSingleHasAND()
        {
            var queries = new List<string>();

            using (var store = GetDocumentStore())
            {
                void RecordQueries(object sender, BeforeQueryExecutedEventArgs args)
                {
                    queries.Add(args.QueryCustomization.ToString());
                    store.OnBeforeQueryExecuted -= RecordQueries;
                }

                var documentSession = store.OpenSession();
                store.OnBeforeQueryExecuted += RecordQueries;

                var _ = documentSession.Query<User>().Where(x => x.Name == "ayende").SingleOrDefault(x => x.Active);

                Assert.Equal(1, queries.Count);
                Assert.Equal("Name:ayende AND Active:true", queries[0]);
            }
        }
    }
}
