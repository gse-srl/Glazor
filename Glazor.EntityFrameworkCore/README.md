# Glazor.EntityFrameworkCore

**Entity Framework Core paged data models for the Glazor data grids.**

This package adds the models that put a Glazor grid over an EF Core query:

- **`EFDataGridModel<TDbContext, TGridItem>`** and **`EditableEFDataGridModel<TDbContext, TGridItem>`** — the grid reads a
  window of rows at a time, and the filters and the sort on `EFProperty` columns become SQL over the whole table. Rows
  already read are kept in chunks spilled to disk as plaintext MessagePack, in the OS temp directory unless
  `SpillDirectory` says otherwise, so a million-row table costs a few pages of memory.
- **`EFFolderModel<TDbContext, TGridItem>`** — a tree for `Treegliaria`, read one folder at a time.
- **`EFDistinctDef`** and **`EFValuesInSetDef`** — column filters that list the values to choose from, read from the database
  when the filter dialog opens.

**Server-only.** The paged models fetch rows during rendering and block the circuit while they wait, which the
single-threaded WebAssembly runtime cannot do: use them in a Blazor Server project interactive from the root, with the
application's one `Spinner` in its layout.

## Installation

```bash
dotnet add package Glazor.EntityFrameworkCore
```

It pulls in the `Glazor` package at exactly the same version, since it uses its internals, so the two are released and
upgraded together. It references `Microsoft.EntityFrameworkCore` 10.0.0 or later; the database provider is the host's own
package. Glazor is used and tested on SQL Server, and its test suite and demo run on SQLite.

## Documentation

The guides are in the Glazor repository: `README.md` for the library as a whole, `docs/getting-started.md` for the wiring,
and `docs/data-models.md` for these models, their paging, editing and requirements.

## License

Copyright © 2025-2026 G.S.E. Servizi Evoluti s.r.l. Licensed under Apache-2.0.

**DISCLAIMER:** This documentation and some tests were generated with the assistance of artificial intelligence (AI). 
The final content has been reviewed, edited, and verified by a human professional to ensure accuracy and quality.
