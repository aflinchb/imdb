# Sample IMDb data for use with Azure Cosmos DB

This repository contains an extract of 100 movies and associated actors, producers, directors and genres from the IMDb public data available [here](https://www.imdb.com/interfaces/)

The purpose of this repo is to demonstrate some NoSQL modeling and querying techniques and decisions when using Azure Cosmos DB as a data store.

```

IMDb shares their data for non-commercial use only. Please respect their data policies.

Information courtesy of
IMDb
(http://www.imdb.com)
Used with permission.

```

## Installation

* Clone this repo: git clone https://github.com/4-co/imdb
* Create an Azure Cosmos DB Account from the Azure Portal or via the Azure CLI
  * Make sure to use the Core (SQL) API
* Create a Cosmos DB database named imdb
* Create a Cosmos DB collection named movies
  * Use /key as the Partition Key
  * Set Throughput (RU/s) to 2000
* Load the data from Data Explorer in the Azure Portal
  * Expand the imdb database
  * Expand the movies collection
  * Click on Documents
  * Click Upload Document
    * Load each of the json documents from this repo
      * You will need to upload one document at a time or increase the RUs to about 5000
* Reset Throughput (RU/s) to 400 to save cost

Once loaded click the refresh icon in Data Explorer and you should see documents

## Design Decisions

In considering the design, we wanted to follow document design best practices as well as optimizing for this specific problem.

### One Collection

We chose to include different document types in the same collection for simplicity (and to demonstrate). You can read about some of the tradeoffs [here](https://docs.microsoft.com/en-us/azure/cosmos-db/modeling-data) and see some of the side effects in the queries below.

Each document has a type field that is one of: Movie, Actor or Genre

ID has to be unique, so we use movieId, actorId or genre as the ID. Reading by ID is the fastest (and cheapest) way to retrieve a document.

### One Partition

All of the data is stored in one partition (/key = 0). Because the data is small, this is the best approach. As the data scales to all movies / TV shows, additional partitions would improve performance.

You want your partition key to be well distributed from a storage and usage perspective. For Actors, a good partition key could be birthYear mod x. However, this would likely not be a good partition key for Movies as a high percentage of the requests are likely to be for the current year which would create a hot partition. A hash of the title would likely be a good choice. movieId (and actorId) are integers with a character preface (tt or nm), so a mod x on the integer portion would be a good choice as well.

You also want to avoid cross-partition queries unless absolutely necessary as they incur additional work which increases the RUs.

Read more about partitioning strategies [here](https://docs.microsoft.com/en-us/azure/cosmos-db/partition-data)

### Fast Changing Data

Generally, you don't want to combine fast changing data and slow changing data in the same document. In this example, "ratings" is a summary measure that would be periodically updated by a batch process. Because the data updates are known and bounded and the document is small, we chose to combine for ease of use. More information [here](https://docs.microsoft.com/en-us/azure/cosmos-db/partition-data)

### Embedded Links

Movies have actors (and producers and directors and crew ...) and Actors star in Movies. 

In a relational model, you would normally have a "MoviesActors" table and join. In a document world, you normally embed unless the embedded data is fast changing or potentially grows to be very large. More information [here](https://docs.microsoft.com/en-us/azure/cosmos-db/partition-data)

A common usage for this data would be to show the Actor and what movies they were in (or a Movie and the Actors in it). If you embed just the ID, this would be two serial queries. One to get the Actor and the Movie IDs and a second one to pull back the movie information. Given the size of the documents, we chose to optimize this by embedded the entire Movie into the Actor document (and Actors into the Movie document). This simplifies reads but complicate writes. In a high read situation (like showing movies on a web site), this is a good optimization. Just keep an eye on document size and update frequency / complexity.

A good example of what you would not want to embed is the individual ratings. Some movies have over 100K ratings, so you would want to keep the individual ratings in a separate collection and have a process that summarizes and updates the aggregate every n minutes.

### Searching

Some of the sample queries search the Movie Title or Actor Name using a "like" query. For a small amount of documents searching across a small number of fields, this works fine. However, if search is a primary use case or you want "full text" search, you should integrate Cosmos DB with Azure Search as the queries will be richer, faster and less expensive.

The Genre search uses an "array_contains" search. In a relational model, you would likely have a MoviesGenres table and use a join (a Movie has 1..n Genres)

Note that in Cosmos DB, search is case sensitive, so searching Movies for "matrix" will return zero documents but searching for "Matrix" will return the correct number of documents. We chose to address this by adding a "textSearch" field that is a lowercase version of the title or actor name (it could contain other keywords as well). This adds size to the document, but prevents having to use contains(lower(m.title), 'matrix'). By using lower(), you are doing a full table scan and not using the index. For 100 movies, this is minimal work, but at scale, it will be slow and expensive.

Again, for large or advanced search workloads, you should integrate Azure Search (or SOLR) as part of the solution, using the Cosmos DB [change feed](https://docs.microsoft.com/en-us/azure/cosmos-db/change-feed) to keep the index fresh is a proven model.

### Key-Value Cache

Cosmos DB is an excellent key-value cache with simple geo-distribution and replication. Performance is often better than other caching solutions and Cosmos DB is cost competitive. The added simplicity of having one data access API and one data platform to manage makes development and operations more efficient.

Some general guidelines:
  * Use the native (SQL) API
  * Use a separate collection for your key-value cache than your operational data
  * Use an efficient partition hash that distributes storage and access evenly (int mod x works well for numeric keys)
  * Use indexing [policies](https://docs.microsoft.com/en-us/azure/cosmos-db/index-policy) to turn off indexing for the value
  * Use direct access by ID (key) for reads
  * Use Cosmos DB [TTL](https://docs.microsoft.com/en-us/azure/cosmos-db/time-to-live) to automatically remove old items
  * Use Cosmos DB [change feed](https://docs.microsoft.com/en-us/azure/cosmos-db/change-feed) to extract values into other systems

### Understanding RUs

A best practice is to baseline the RUs for each "action" and include as part of your testing suite. Changes to your document model or query can result in significant changes in RUs. The Cosmos DB API has the ability to capture RUs for each action, so building a baseline is straight forward.

General best practices like limiting the columns selected, limiting the documents selected and avoiding table scans are important. The deeper a filter condition is in the document model, the more work the query processor has to do (and the more RUs it consumes), so keep frequent predicates at the root whenever possible and/or use indexing [policies](https://docs.microsoft.com/en-us/azure/cosmos-db/index-policy) to optimize common queries.

Avoid cross partition queries when possible. Cosmos DB will run the query in parallel, but it is more work and thus higher RUs.

### Conclusion

Unlike relational modeling where specific normal forms are verifiable, document modeling is a collection of decisions based heavily on usage patterns. There is no "right" or "correct" answer but there are best practices and trade offs based on usage. It is important to understand the usage patterns early so that you can optimize the document model.

## Sample Queries

Click the new sample query icon in the Data Explorer tool bar and run the default select * query to see the first 100 documents

Cosmos DB Query cheat sheets: https://docs.microsoft.com/en-us/azure/cosmos-db/query-cheat-sheet

```

# Simplest query
select * from m

# List of movies
select m.movieId, m.type, m.title, m.year, m.runtime, m.genres, m.roles 
from m 
where m.type = 'Movie'

# List of Genres
select m.genre 
from m 
where m.type = 'Genre'

# List of Actors
select m.actorId, m.type, m.name, m.birthYear, m.deathYear, m.profession, m.movies 
from m 
where m.type = 'Actor'

# Simple transform
select value m.title
from m 

# Unexpected behavior
# This is a side effect of combining the document types in one collection
select m.title
from m 

# Info about a great movie
select m.movieId, m.type, m.rating, m.votes, m.title, m.year, m.runtime, m.genres, m.roles 
from m 
where m.id = 'tt0133093'

# A list of specific movies
select m.movieId, m.type, m.rating, m.votes, m.title, m.year, m.runtime, m.genres, m.roles
from m
where m.movieId in ("tt0167260", "tt0419781", "tt0367495", "tt0120737", "tt0358456")

# The API has a more efficient way to retrieve exactly one document by ID
#   It is faster and consumes less RUs and should be used in most scenarios
# However, when retrieving 4 or more movies (in this data set), it is less RUs
#   to use a query than single reads and is also easier to use
#   this will vary slightly by model and data size. Single reads are constant.

# An actor from a great movie
select m.actorId, m.type, m.name, m.birthYear, m.deathYear, m.profession, m.movies 
from m 
where m.id = 'nm0000206'

# Movies Jennifer Connelly is in 
select m.movieId, m.type, m.rating, m.votes, m.title, m.year, m.runtime, m.genres, m.roles
from m 
where array_contains(m.roles, { actorId: "nm0000124" }, true) order by m.movieId

# Another way
# note you can't use select * or select m.*
select m.movieId, m.type, m.title, m.year, m.runtime, m.genres, m.roles
from movies m 
join r in m.roles
where r.actorId = 'nm0000124'

# Action Movies
# Note this is case sensitive, so 'action' won't work
select m.movieId, m.type, m.rating, m.votes, m.title, m.year, m.runtime, m.genres, m.roles 
from m 
where array_contains(m.genres, 'Action') 
order by m.movieId

# Search movie title for "rings"
select m.movieId, m.type, m.rating, m.votes, m.title, m.year, m.runtime, m.genres, m.roles
from m 
where contains(m.textSearch, 'rings')

# Search actor names for "tom"
select m.actorId, m.type, m.name, m.birthYear, m.deathYear, m.profession, m.movies 
from m 
where contains(m.textSearch, 'tom')

# Long movies
select top 5 m.movieId, m.type, m.rating, m.votes, m.title, m.year, m.runtime, m.genres, m.roles
from m 
order by m.runtime desc

# Highest rated movies
select top 5 m.movieId, m.type, m.rating, m.votes, m.title, m.year, m.runtime, m.genres, m.roles
from m
order by m.rating desc

# Movies by year
select m.movieId, m.type, m.rating, m.votes, m.title, m.year, m.runtime, m.genres, m.roles
from m
where m.year = 2006

# Actors in more than one movie
select m.actorId, m.type, m.name, m.birthYear, m.deathYear, m.profession, m.movies
from m
where array_length(m.movies) > 1
order by m.name

```
