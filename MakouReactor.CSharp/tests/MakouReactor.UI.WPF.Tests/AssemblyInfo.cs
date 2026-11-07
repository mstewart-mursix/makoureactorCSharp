using Xunit;

// WPF resource loading (Application.LoadComponent -> System.IO.Packaging.PackagePart) is not
// thread-safe across concurrent STA threads: parallel test classes intermittently fail with an
// ArgumentOutOfRangeException in PackagePart.CleanUpRequestedStreamsList while constructing
// XAML views. Run this assembly's tests serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
