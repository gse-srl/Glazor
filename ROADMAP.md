# Roadmap

What is planned, in no order. Nothing here is promised.

- Multi-column sort.
- Keyset pagination as an option for `EFDataGridModel`, in place of `OFFSET` paging deep in a table:
  https://learn.microsoft.com/en-us/ef/core/querying/pagination.
- Database providers other than SQL Server, PostgreSQL first: the sorted-column search compares in memory as SQL Server
  orders, nulls first and by its default collation, while PostgreSQL puts nulls last.
- A tree model base class that defines what a sorted or filtered tree means, see Treegliaria in the docs.
- Paged models over sources other than EF Core (Dapper, a web service): opening the editing and in-memory filter
  support the EF models use, so a model derived from `AGridModel` can reuse them.
- Turning the touch scroller on only where the device has a touch screen.
- `AsyncContext` from https://github.com/StephenCleary/AsyncEx in place of `AsyncHelper`, to be evaluated.
- Comparing strings in memory by a culture's sort keys, as a database's collation orders them:
  https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-globalization-sortkey.
