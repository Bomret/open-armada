using Microsoft.VisualStudio.TestTools.UnitTesting;

// Log is process-wide state (sink and counts), so the suite runs serially.
[assembly: DoNotParallelize]
