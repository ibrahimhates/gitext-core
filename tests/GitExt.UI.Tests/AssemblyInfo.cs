// 🔴 REGRESSION (CI, both Linux and Windows): tests failed with "Test Case Cleanup Failure —
// The calling thread cannot access this object because a different thread owns it", and a
// DIFFERENT test failed on each run (InProgressBannerTests, DetachedHeadBannerTests,
// BranchEditTests …). MEASURED locally: 1 in 3 runs reproduces it.
//
// The stack trace points at HeadlessUnitTestSession.EnsureIsolatedApplication →
// AvaloniaHeadlessPlatform.Initialize → Compositor..ctor → Dispatcher.VerifyAccess. The
// headless platform sets up process-global state (Dispatcher.UIThread, the compositor's
// render loop) bound to ONE thread. xUnit v3 runs test collections in parallel by default,
// so a second collection starting on another thread tries to initialise that same global
// state and VerifyAccess throws.
//
// The name of the test that dies is therefore meaningless — whichever one happens to lose
// the race is the one that fails. Only turning off cross-collection parallelism closes it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
