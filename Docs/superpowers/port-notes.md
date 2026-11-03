# Port notes

Findings about the C# reference discovered during the Rust port. Each entry: what, where, how Rust handles it.

- Where C# throws `OverflowException` (checked `decimal` paths in `Fx.FromRatio`/`MulInt`), Rust panics; the fixtures list those inputs in `fx_skipped` and the tests assert they fail.
