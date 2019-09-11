using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;
using System.Threading;
using Microsoft.Azure.Documents;

namespace imdb_import
{
    class Program
    {
        static int count = 0;
        static DocumentClient client;
        static Uri collectionLink;
        static object rowLock = new object();

        static async Task Main(string[] args)
        {
            if (args.Length != 4)
            {
                Usage();
                Environment.Exit(-1);
            }

            Console.WriteLine("Reading Data\n");

            string cosmosUrl = string.Format("https://{0}.documents.azure.com:443/", args[0].Trim().ToLower());
            string cosmosKey = args[1].Trim();
            string cosmosDatabase = args[2].Trim();
            string cosmosCollection = args[3].Trim();

            client = new DocumentClient(new Uri(cosmosUrl), cosmosKey);
            await client.OpenAsync();

            // create the collection link
            collectionLink = UriFactory.CreateDocumentCollectionUri(cosmosDatabase, cosmosCollection);

            // load actors from the json files
            List<dynamic> Actors = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(@"data/actors.json"));

            Console.WriteLine("Importing Data ...");

            List<Task> tasks = new List<Task>();

            int batchSize = 100;
            int max = Actors.Count / batchSize;

            // load actors batchSize rows at a time
            for (int i = 0; i < max; i++)
            {
                tasks.Add(LoadData(Actors, i * batchSize, batchSize));
            }

            // load remainder
            if (Actors.Count > max * batchSize)
            {
                tasks.Add(LoadData(Actors, max * batchSize, Actors.Count - max * batchSize));
            }

            // load movies
            List<dynamic> Movies = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(@"data/movies.json"));
            tasks.Add(LoadData(Movies, 0, Movies.Count));

            // load genres
            List<dynamic> Genres = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(@"data/genres.json"));
            tasks.Add(LoadData(Genres, 0, Genres.Count));

            // wait for loads to finish
            foreach (Task t in tasks)
            {
                await t;
            }

            Console.WriteLine("Rows Loaded: {0}\n\nImport Complete", count);
        }

        static async Task LoadData(List<dynamic> list, int start, int length)
        {
            int max = start + length;
            int i = start;

            while (i < max)
            {
                try
                {
                    await client.UpsertDocumentAsync(collectionLink, list[i]);
                    i++;
                    IncrementRowCount();
                }
                catch (Exception ex)
                {
                    // catch the CosmosDB RU exceeded exception and retry

                    DocumentClientException dce = ex as DocumentClientException;

                    // Could be an aggregate exception
                    if (dce == null)
                    {
                        dce = ex.InnerException as DocumentClientException;
                    }

                    // something else went wrong
                    if (dce == null)
                    {
                        Console.WriteLine(ex);
                        return;
                    }

                    // sleep and then retry
                    Thread.Sleep(dce.RetryAfter);
                }
            }
        }

        static void IncrementRowCount()
        {
            lock (rowLock)
            {
                count++;

                // update progress
                if (count % 50 == 0)
                {
                    Console.WriteLine("Rows Loaded: {0}", count);
                }
            }
        }

        static void Usage()
        {
            Console.WriteLine("Usage: imdb-import Name Key Database Collection");
        }
    }
}
