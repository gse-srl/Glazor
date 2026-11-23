# Security

## Reporting a vulnerability

Report it privately, through GitHub's private vulnerability reporting: the repository's **Security** tab, then
**Report a vulnerability**. Please do not open a public issue, pull request or discussion for it.

Say which package and version are affected (`Glazor`, `Glazor.EntityFrameworkCore`), what an attacker can do, and how to
reproduce it, a minimal page or test if you can. The report stays private until a fix is released.

## Supported versions

Fixes go into the latest release only.

## Before you report

Two behaviours are documented trust assumptions rather than vulnerabilities:

- **A memento trusts its store.** A grid's persisted state names its filter types, and restoring it builds them. Store it
  where users cannot write, or sign it, as [Layout persistence](docs/grigliaria.md#layout-persistence) explains.
- **Paged rows are spilled to disk as plaintext.** `EFDataGridModel` writes the rows it has read to files in the temp
  directory, or in `SpillDirectory`, as [Paging stores rows on disk](docs/data-models.md#paging-stores-rows-on-disk)
  explains.

A way around either of them, or a case the docs do not cover, is still worth reporting.
