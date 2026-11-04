# SdkConsumer fixture

A minimal project that the `SdkFixture` tests copy into a temporary workspace and build with the real
Codelab.Build.Sdk (source mode). Each test adds one violating file and expects the matching build error
(CLB1001-CLB1004, RS0030 for `Thread.Sleep`), or builds it clean with `CODELAB_RELEASE`/`CODELAB_COMMIT` and reads
the stamped version back. It is not in `codelab-starter.slnx` and is never built in place.
