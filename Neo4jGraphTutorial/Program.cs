using Neo4j.Driver;
using System;
using System.Threading.Tasks;

namespace Neo4jGraphTutorial
{
    class Program
    {
        static void Main(string[] args)
        {
            var hwe = new HelloWorldExample("bolt://localhost:7687", "neo4j", "sullevan");
            hwe.PrintGreeting("Hello World!");
        }
    }
    public class HelloWorldExample : IDisposable
    {
        private readonly IDriver _driver;

        public HelloWorldExample(string uri, string userName, string password)
        {
            _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(userName, password));
        }

        public void PrintGreeting(string message)
        {
            using var session = _driver.AsyncSession();
            var greeting = session.WriteTransactionAsync(tx =>
            {
                var result = tx.RunAsync("CREATE (a:Greeting) " +
                    "SET a.message = $message " +
                    "RETURN a.message + ', from node ' + id(a)",
                    new { message });

                return result.Result.SingleAsync();
            }).Result.As<string>();
            Console.WriteLine(greeting);
        }
        public void Dispose()
        {
            _driver.Dispose();
        }
    }
}
